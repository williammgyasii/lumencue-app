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

        // Holds the active seat token in memory for cloud-API calls (Bible proxy, STT token).
        var seatTokens = new SeatTokenProvider();
        services.AddSingleton<ISeatTokenProvider>(seatTokens);

        var dbService = new DatabaseService(AppPaths.DatabasePath);
        await dbService.InitializeAsync();

        services.AddSingleton(dbService);
        services.AddSingleton<ITenantContext, TenantContext>();
        services.AddSingleton<ScriptureRepository>();
        services.AddSingleton<SongRepository>();
        services.AddSingleton<SettingsRepository>();
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
        // Tenancy / cloud sign-in + song sync. Priority:
        //   1) Hosted HTTP API (the shipping default — keeps all DB credentials server-side).
        //   2) Direct Neon, dev-only escape hatch when a Neon string is present in local config.
        //   3) Local stub (offline) otherwise.
        services.AddSingleton<ISessionStore, SessionStore>();
        if (!string.IsNullOrWhiteSpace(cloudApiBaseUrl))
        {
            Log.Information("Cloud backend: hosted API at {BaseUrl}", cloudApiBaseUrl);
            // Route through SeatAuthHandler so song-sync (Pull/Push) carries the seat token; sign-in
            // happens before a token exists and the handler simply omits the header in that case.
            var cloudHttp = new HttpClient(new SeatAuthHandler(seatTokens, new HttpClientHandler()))
            {
                BaseAddress = new Uri(cloudApiBaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(20),
            };
            services.AddSingleton<ICloudGateway>(new HttpCloudGateway(cloudHttp));
        }
        else if (!string.IsNullOrWhiteSpace(neonConnStr))
        {
            Log.Information("Cloud backend: direct Neon (dev fallback, no hosted API)");
            services.AddSingleton<ICloudGateway>(new NeonCloudGateway(neonConnStr));
        }
        else
        {
            // No cloud configured (dev or prod): refuse sign-in rather than accept anything. Real
            // credentials are always validated against the cloud API (or direct Neon in dev); we never
            // fall back to an "any password works" path.
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
        services.AddSingleton<ILiveBackgroundService, LiveBackgroundService>();
        services.AddSingleton<IAnnouncementService, AnnouncementService>();
        services.AddSingleton<ILayerService, LayerService>();
        services.AddSingleton<IProPresenterService, ProPresenterService>();

        // Speech-to-text. When a cloud API is configured, stream to Deepgram using a short-lived
        // token minted by the backend (no key on the client), falling back to offline Vosk when no
        // token can be obtained. Without a cloud API (pure offline build), use Vosk directly.
        // Confidence gate for noisy rooms: finals below this are dropped (0 disables, default 0.5).
        var minConfidence = double.TryParse(config["Deepgram:MinConfidence"], out var mc) ? mc : 0.5;
        if (!string.IsNullOrWhiteSpace(cloudApiBaseUrl))
        {
            Log.Information("STT: Deepgram (cloud, token-based) with Vosk offline fallback, confidence gate {Min:P0}", minConfidence);
            var sttHttp = new HttpClient(new SeatAuthHandler(seatTokens, new HttpClientHandler()))
            {
                BaseAddress = new Uri(cloudApiBaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(15),
            };
            var sttTokenProvider = new HttpSttTokenProvider(sttHttp);
            services.AddSingleton<ISttTokenProvider>(sttTokenProvider);
            services.AddSingleton<ITranscriptionService>(_ => new ResilientTranscriptionService(
                new DeepgramTranscriptionService(sttTokenProvider, minConfidence),
                new VoskTranscriptionService(),
                sttTokenProvider));
        }
        else
        {
            Log.Information("STT: Vosk (local offline)");
            services.AddSingleton<ITranscriptionService, VoskTranscriptionService>();
        }

        services.AddSingleton<SemanticEmbeddingService>();
        services.AddSingleton<FuzzyAiMatcherService>();
        services.AddSingleton<IAiMatcherService, HybridAiMatcherService>();
        services.AddSingleton<ISuggestionEngine, SuggestionEngine>();
        services.AddSingleton<IScriptureSearchService, ScriptureSearchService>();
        services.AddSingleton<ISongSearchService, SongSearchService>();
        services.AddSingleton<IUpdateService>(new VelopackUpdateService(config));
        services.AddSingleton<OperatorViewModel>();
        services.AddSingleton<ProjectorViewModel>();
        services.AddSingleton<WindowManager>();

        _services = services.BuildServiceProvider();

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

            await GateAndStartAsync();

            // Fire-and-forget OTA update check once a window is on screen. The operator UI shows a
            // persistent toast if an update is found. No-ops in dev / non-installed builds.
            _ = _services.GetRequiredService<IUpdateService>().CheckAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private bool _signOutWired;

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
            tenant.Set(session.OrganizationId, session.OrganizationName, session.BranchId);
            await StartOperatorAsync(session);
            _ = RevalidateInBackgroundAsync(gateway, store, session);
        }
        else
        {
            ShowSignIn();
        }
    }

    private void ShowSignIn()
    {
        if (_services is null) return;
        var vm = _services.GetRequiredService<SignInViewModel>();
        var window = new SignInWindow { DataContext = vm };
        vm.SignedIn += async session =>
        {
            var tenant = _services!.GetRequiredService<ITenantContext>();
            var songs = _services!.GetRequiredService<SongRepository>();
            _services!.GetRequiredService<ISeatTokenProvider>().Set(session.Token);
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
            _signOutWired = true;
        }

        await operatorVm.InitializeAsync();
        _services.GetRequiredService<ISyncScheduler>().Start();
        _services.GetRequiredService<WindowManager>().ShowAll();
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
            tenant.Reset();

            ShowSignIn();                                            // open sign-in before closing operator (keep >= 1 window)
            _services.GetRequiredService<WindowManager>().CloseAll();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sign-out failed");
        }
    }

    private static async Task RevalidateInBackgroundAsync(ICloudGateway gateway, ISessionStore store, AuthSession session)
    {
        if (!gateway.IsConfigured) return;
        try
        {
            var result = await gateway.ValidateAsync(session);
            if (result is { Success: true, Session: not null })
                await store.SaveAsync(result.Session);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Background session revalidation failed (continuing offline)");
        }
    }
}
