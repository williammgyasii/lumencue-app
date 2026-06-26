using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ChurchProjection.Core.Models.Tenancy;
using ChurchProjection.Core.Services;
using ChurchProjection.Infrastructure.Audio;
using ChurchProjection.Infrastructure.Bible;
using ChurchProjection.Infrastructure.Data;
using ChurchProjection.Infrastructure.Matching;
using ChurchProjection.Infrastructure.Search;
using ChurchProjection.Infrastructure.Services;
using ChurchProjection.UI.Services;
using ChurchProjection.UI.ViewModels;
using ChurchProjection.UI.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ChurchProjection.App;

public class App : Application
{
    private IServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        // Show a lightweight splash immediately. DB init, DI wiring and the first library load below
        // take a few seconds, during which no window would otherwise appear — leaving the operator
        // unsure the app even launched. The splash gives instant feedback and is closed once the real
        // window (sign-in or operator) is on screen.
        SplashWindow? splash = null;
        var splashShownAt = DateTime.UtcNow;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime bootDesktop)
        {
            bootDesktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose;
            splash = new SplashWindow();
            splash.Show();
            splash.SetProgress(8);
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var neonConnStr = config["ConnectionStrings:Neon"] ?? "";
        var cloudApiBaseUrl = config["CloudApi:BaseUrl"] ?? "";

        Log.Information("Cloud API configured: {Configured}", !string.IsNullOrWhiteSpace(cloudApiBaseUrl));
        Log.Information("Neon (dev) configured: {Configured}", !string.IsNullOrWhiteSpace(neonConnStr));
        Log.Information("Free Bible API (bible.helloao.org): always available");

        var services = new ServiceCollection();

        // Holds the active seat token in memory for cloud-API calls (Bible proxy, STT token), plus
        // the machine fingerprint that binds the seat to this device (attached to every request).
        var seatTokens = new SeatTokenProvider();
        seatTokens.SetHardware(HardwareFingerprint.Get());
        services.AddSingleton<ISeatTokenProvider>(seatTokens);

        splash?.SetStatus("Preparing database…");
        splash?.SetProgress(24);
        var dbService = new DatabaseService(AppPaths.DatabasePath);
        await dbService.InitializeAsync();

        services.AddSingleton(dbService);
        services.AddSingleton<ITenantContext, TenantContext>();
        services.AddSingleton<ScriptureRepository>();
        services.AddSingleton<SongRepository>();
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<NotesRepository>();
        services.AddSingleton<BibleCacheService>();
        services.AddSingleton<FreeBibleApiClient>(sp =>
            new FreeBibleApiClient(sp.GetRequiredService<BibleCacheService>()));
        // Premium translations come through the cloud API's /bible/ proxy (api.bible key stays
        // server-side); the seat token authenticates each request.
        ApiBibleClient? apiBibleClient = null;
        if (!string.IsNullOrWhiteSpace(cloudApiBaseUrl))
        {
            var bibleHttp = new HttpClient(new SeatAuthHandler(seatTokens, new HttpClientHandler()))
            {
                BaseAddress = new Uri(cloudApiBaseUrl.TrimEnd('/') + "/bible/"),
                Timeout = TimeSpan.FromSeconds(25),
            };
            apiBibleClient = new ApiBibleClient(bibleHttp);
        }
        if (apiBibleClient is not null)
            services.AddSingleton(apiBibleClient);
        services.AddSingleton<IBibleApiService>(sp =>
            new CombinedBibleService(sp.GetRequiredService<FreeBibleApiClient>(), apiBibleClient));
        // Tenancy / cloud sign-in + song sync. The hosted HTTP API is the single source of truth:
        // it keeps every DB/provider credential server-side and enforces seats, hardware binding and
        // entitlements. When no API is configured we refuse sign-in rather than accept anything
        // (no "any password works" path).
        services.AddSingleton<ISessionStore, SessionStore>();
        // Resolved entitlements (plan, AI allowance, premium features) that the in-app paywall binds to.
        services.AddSingleton<IEntitlementService, EntitlementService>();
        if (!string.IsNullOrWhiteSpace(cloudApiBaseUrl))
        {
            Log.Information("Cloud backend: hosted API at {BaseUrl}", cloudApiBaseUrl);
            // Route through SeatAuthHandler so every authenticated call carries the seat token and the
            // hardware fingerprint; sign-in runs before a token exists and the handler omits the token.
            var cloudHttp = new HttpClient(new SeatAuthHandler(seatTokens, new HttpClientHandler()))
            {
                BaseAddress = new Uri(cloudApiBaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(20),
            };
            services.AddSingleton<ICloudGateway>(new HttpCloudGateway(cloudHttp));
        }
        else
        {
            Log.Error("Cloud backend: no CloudApi:BaseUrl configured; sign-in is disabled.");
            services.AddSingleton<ICloudGateway>(new UnavailableCloudGateway(
                "Sign-in is unavailable: this build is not connected to the LumenCue service."));
        }
        services.AddTransient<SignInViewModel>();

        // Background org-level song sync (push/pull). Active only when a cloud API is configured.
        services.AddSingleton<ISyncScheduler, SyncScheduler>();

        services.AddSingleton<IContentLibraryService, ContentLibraryService>();
        services.AddSingleton<IProjectionService, ProjectionService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IThemeAssetStore>(new ThemeAssetStore(AppPaths.DataDirectory));
        services.AddSingleton<ILiveBackgroundService, LiveBackgroundService>();
        services.AddSingleton<IAnnouncementService, AnnouncementService>();
        services.AddSingleton<ILayerService, LayerService>();
        services.AddSingleton<IProPresenterService, ProPresenterService>();

        // Speech-to-text is cloud-only: stream to Deepgram using a short-lived token minted by the
        // backend (no key on the client). Deepgram auto-reconnects and surfaces a clear status if the
        // connection or token is unavailable; there is deliberately no offline engine, because every
        // on-device option proved either too inaccurate or too slow to run in real time, and a bad
        // transcript actively fires the wrong scripture/song cues on screen. When the cloud is down the
        // operator drives cues manually instead.
        // Confidence gate for noisy rooms: finals below this are dropped (0 disables, default 0.5).
        var minConfidence = double.TryParse(config["Deepgram:MinConfidence"], out var mc) ? mc : 0.3;
        // Software boost for quiet capture devices (built-in mic arrays often sit low). 1.0 = no change.
        var inputGain = double.TryParse(config["Deepgram:InputGain"], out var ig) ? ig : 1.0;
        // Cost control: only stream to Deepgram during speech. Disable by setting Deepgram:VadGate=false.
        var vadGate = !bool.TryParse(config["Deepgram:VadGate"], out var vg) || vg;
        var vadThreshold = double.TryParse(config["Deepgram:VadThreshold"], out var vt) ? vt : 0.01;
        if (!string.IsNullOrWhiteSpace(cloudApiBaseUrl))
        {
            Log.Information("STT: Deepgram (cloud, token-based), confidence gate {Min:P0}", minConfidence);
            var sttHttp = new HttpClient(new SeatAuthHandler(seatTokens, new HttpClientHandler()))
            {
                BaseAddress = new Uri(cloudApiBaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(15),
            };
            var sttTokenProvider = new HttpSttTokenProvider(sttHttp);
            services.AddSingleton<ISttTokenProvider>(sttTokenProvider);
            services.AddSingleton<ITranscriptionService>(_ =>
                new DeepgramTranscriptionService(sttTokenProvider, minConfidence, inputGain, vadGate, vadThreshold));
        }
        else
        {
            Log.Warning("STT: no cloud backend configured; speech-to-text disabled (manual mode only)");
            services.AddSingleton<ITranscriptionService>(_ => new UnavailableTranscriptionService());
        }

        services.AddSingleton<SemanticEmbeddingService>();
        services.AddSingleton<FuzzyAiMatcherService>();
        services.AddSingleton<IAiMatcherService, HybridAiMatcherService>();
        services.AddSingleton<ISuggestionEngine, SuggestionEngine>();
        services.AddSingleton<IScriptureSearchService, ScriptureSearchService>();
        services.AddSingleton<IScriptureParaphraseWatcher, ScriptureParaphraseWatcher>();
        services.AddSingleton<ISongSearchService, SongSearchService>();
        services.AddSingleton<IUpdateService>(new VelopackUpdateService(config));
        services.AddSingleton<OperatorViewModel>();
        services.AddSingleton<ProjectorViewModel>();
        services.AddSingleton<WindowManager>();

        _services = services.BuildServiceProvider();

        // If any authenticated cloud call is rejected with 401, the seat token is dead — drop the
        // session and return to sign-in instead of leaving the operator running on a session the
        // server no longer accepts.
        seatTokens.Unauthorized += OnSeatUnauthorized;

        splash?.SetStatus("Loading settings…");
        splash?.SetProgress(58);
        await _services.GetRequiredService<IThemeService>().LoadAsync();
        await _services.GetRequiredService<IProPresenterService>().LoadSettingsAsync();
        await _services.GetRequiredService<ILiveBackgroundService>().LoadAsync();
        await _services.GetRequiredService<IAnnouncementService>().LoadAsync();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose;

            _ = Task.Run(async () =>
            {
                try
                {
                    var embedding = _services.GetRequiredService<SemanticEmbeddingService>();
                    await embedding.InitializeAsync();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Semantic embedding init failed (non-fatal)");
                }
            });

            splash?.SetStatus("Loading your library…");
            splash?.SetProgress(80);
            await GateAndStartAsync();

            // The real window (sign-in or operator) is now on screen; retire the splash. Fill the bar,
            // then hold the splash for a moment (and let the bar animation finish) so a fast,
            // cached-session launch still reads as a deliberate load rather than a flicker.
            splash?.SetStatus("Ready");
            splash?.SetProgress(100);
            var elapsed = DateTime.UtcNow - splashShownAt;
            var minOnScreen = TimeSpan.FromMilliseconds(1600);
            if (elapsed < minOnScreen)
                await Task.Delay(minOnScreen - elapsed);

            // Closing the splash after the main window opens keeps at least one window alive so
            // OnLastWindowClose doesn't shut the app down.
            splash?.Close();

            // Fire-and-forget OTA update check once a window is on screen. The operator UI shows a
            // persistent toast if an update is found. No-ops in dev / non-installed builds.
            _ = _services.GetRequiredService<IUpdateService>().CheckAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private bool _signOutWired;
    private bool _inOperator;
    private bool _forcedReauthInProgress;

    /// <summary>Decides whether to start straight into the operator (valid/offline-grace session) or gate on sign-in.</summary>
    private async Task GateAndStartAsync()
    {
        if (_services is null) return;
        var store = _services.GetRequiredService<ISessionStore>();
        var tenant = _services.GetRequiredService<ITenantContext>();
        var gateway = _services.GetRequiredService<ICloudGateway>();

        var session = await store.LoadAsync();
        var withinGrace = session is not null
            && (!gateway.IsConfigured || session.LastValidatedUtc > DateTime.UtcNow.AddDays(-30));

        if (session is not null && withinGrace)
        {
            _services.GetRequiredService<ISeatTokenProvider>().Set(session.Token);
            _services.GetRequiredService<IEntitlementService>().Update(session);
            tenant.Set(session.OrganizationId, session.OrganizationName, session.BranchId);
            await StartOperatorAsync(session);
            _ = RevalidateInBackgroundAsync(gateway, store, _services.GetRequiredService<IEntitlementService>(), session);
        }
        else
        {
            // Sign-in is bypassed for now: rather than gate on the cloud, boot straight into the
            // operator on a local, unlimited (master) session. A previously-signed-in church keeps
            // its own organization (so its library stays visible); a fresh install uses the local
            // default library. No seat token is set, so cloud features stay offline until sign-in returns.
            await StartLocalAsync(session);
        }
    }

    /// <summary>Starts the operator without sign-in, on a locally-synthesized unlimited session.</summary>
    private async Task StartLocalAsync(AuthSession? prior)
    {
        if (_services is null) return;

        var local = LocalSession.Master();
        if (prior is not null && !string.IsNullOrWhiteSpace(prior.OrganizationId))
        {
            // Preserve an existing church's library scoping, but keep the account display neutral
            // ("Local library" + "Sign in") since there's no real seat token in local mode.
            local.OrganizationId = prior.OrganizationId;
            _services.GetRequiredService<ITenantContext>().Set(prior.OrganizationId, prior.OrganizationName, prior.BranchId);
        }

        _services.GetRequiredService<IEntitlementService>().Update(local);
        await StartOperatorAsync(local);
    }

    private void ShowSignIn(string? notice = null)
    {
        if (_services is null) return;
        _inOperator = false;
        _forcedReauthInProgress = false;
        var vm = _services.GetRequiredService<SignInViewModel>();
        if (!string.IsNullOrWhiteSpace(notice))
            vm.ShowNotice(notice);
        var window = new SignInWindow { DataContext = vm };
        vm.SignedIn += async session =>
        {
            var tenant = _services!.GetRequiredService<ITenantContext>();
            var songs = _services!.GetRequiredService<SongRepository>();
            _services!.GetRequiredService<ISeatTokenProvider>().Set(session.Token);
            _services!.GetRequiredService<IEntitlementService>().Update(session);
            tenant.Set(session.OrganizationId, session.OrganizationName, session.BranchId);
            await songs.AdoptDefaultLibraryAsync(session.OrganizationId);
            await StartOperatorAsync(session);
            window.Close();
        };
        window.Show();
    }

    private async Task StartOperatorAsync(AuthSession session)
    {
        if (_services is null) return;
        var operatorVm = _services.GetRequiredService<OperatorViewModel>();
        operatorVm.SetAccount(session.OrganizationName, session.BranchName, session.SeatsUsed, session.SeatCount);

        if (!_signOutWired)
        {
            operatorVm.SignOutRequested += OnSignOutRequested;
            operatorVm.SignInRequested += OnSignInRequested;
            _signOutWired = true;
        }

        await operatorVm.InitializeAsync();
        _services.GetRequiredService<ISyncScheduler>().Start();
        _services.GetRequiredService<WindowManager>().ShowAll();
        _inOperator = true;
    }

    // Fired (off the UI thread) when an authenticated request was rejected with 401. Marshal to the
    // UI thread and force a single return to sign-in, ignoring the burst of follow-on 401s.
    private void OnSeatUnauthorized()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!_inOperator || _forcedReauthInProgress) return;
            _forcedReauthInProgress = true;
            _ = ForceReauthAsync("You were signed out (your session expired or the seat was released). Please sign in again.");
        });
    }

    private async Task ForceReauthAsync(string notice)
    {
        if (_services is null) return;
        try
        {
            Log.Warning("Seat token rejected (401); dropping session and returning to sign-in");
            _services.GetRequiredService<ISyncScheduler>().Stop();

            // The token is already dead server-side, so skip the network sign-out and just clear local state.
            await _services.GetRequiredService<ISessionStore>().ClearAsync();
            _services.GetRequiredService<ISeatTokenProvider>().Set(null);
            _services.GetRequiredService<IEntitlementService>().Clear();
            _services.GetRequiredService<ITenantContext>().Reset();

            ShowSignIn(notice);                                      // open sign-in before closing operator (keep >= 1 window)
            _services.GetRequiredService<WindowManager>().CloseAll();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Forced re-authentication failed");
        }
    }

    private async void OnSignOutRequested()
    {
        if (_services is null) return;
        try
        {
            var store = _services.GetRequiredService<ISessionStore>();
            var gateway = _services.GetRequiredService<ICloudGateway>();
            var tenant = _services.GetRequiredService<ITenantContext>();

            _services.GetRequiredService<ISyncScheduler>().Stop();

            var session = await store.LoadAsync();
            if (session is not null)
            {
                try { await gateway.SignOutAsync(session); } catch { /* best-effort seat release */ }
            }
            await store.ClearAsync();
            _services.GetRequiredService<ISeatTokenProvider>().Set(null);
            _services.GetRequiredService<IEntitlementService>().Clear();
            tenant.Reset();

            ShowSignIn();                                            // open sign-in before closing operator (keep >= 1 window)
            _services.GetRequiredService<WindowManager>().CloseAll();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sign-out failed");
        }
    }

    /// <summary>From local (signed-out) mode, opens the sign-in window so the church can claim a seat
    /// and unlock cloud features (live AI transcription, premium Bible, sync). A successful sign-in
    /// reopens the operator on the real session via <see cref="ShowSignIn"/>'s SignedIn handler.</summary>
    private void OnSignInRequested()
    {
        if (_services is null) return;
        try
        {
            _services.GetRequiredService<ISyncScheduler>().Stop();     // local sync (no token) — stop before re-gating
            ShowSignIn();                                              // open sign-in before closing operator (keep >= 1 window)
            _services.GetRequiredService<WindowManager>().CloseAll();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sign-in (from local mode) failed to open");
        }
    }

    private static async Task RevalidateInBackgroundAsync(
        ICloudGateway gateway, ISessionStore store, IEntitlementService entitlements, AuthSession session)
    {
        if (!gateway.IsConfigured) return;
        try
        {
            var result = await gateway.ValidateAsync(session);
            if (result is { Success: true, Session: not null })
            {
                await store.SaveAsync(result.Session);
                entitlements.Update(result.Session);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Background session revalidation failed (continuing offline)");
        }
    }
}
