using System.Reactive.Subjects;
using ChurchProjection.UI.Services;
using Microsoft.Extensions.Configuration;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace ChurchProjection.App;

/// <summary>
/// OTA updates via Velopack against the private "lumencue-app" GitHub Releases. Because the repo
/// is private, a read-only GitHub token (Updates:GitHubToken, shipped in appsettings.local.json)
/// is required to read releases. No-ops gracefully in dev / non-installed builds.
/// </summary>
internal sealed class VelopackUpdateService : IUpdateService
{
    private readonly BehaviorSubject<UpdateState> _state = new(new UpdateState());
    private readonly string _repoUrl;
    private readonly string? _token;

    private UpdateManager? _manager;
    private UpdateInfo? _pending;

    public VelopackUpdateService(IConfiguration config)
    {
        _repoUrl = (config["Updates:RepoUrl"] ?? "").Trim();
        _token = config["Updates:GitHubToken"];
    }

    public IObservable<UpdateState> State => _state;

    public async Task CheckAsync(bool userInitiated = false)
    {
        if (string.IsNullOrWhiteSpace(_repoUrl))
        {
            Log.Information("Updates: no Updates:RepoUrl configured, skipping.");
            EmitTransient("Updates are not configured for this build.", userInitiated);
            return;
        }

        Push(new UpdateState { Phase = UpdatePhase.Checking });

        try
        {
            var source = new GithubSource(_repoUrl, _token, prerelease: false);
            var mgr = new UpdateManager(source);

            if (!mgr.IsInstalled)
            {
                Log.Information("Updates: not an installed build, skipping check.");
                Reset();
                EmitTransient("Updates only run in the installed app.", userInitiated);
                return;
            }

            var info = await mgr.CheckForUpdatesAsync();
            if (info is null)
            {
                Log.Information("Updates: already up to date.");
                Reset();
                EmitTransient("You're on the latest version.", userInitiated);
                return;
            }

            _manager = mgr;
            _pending = info;
            var version = info.TargetFullRelease.Version.ToString();
            Log.Information("Updates: version {Version} available.", version);
            Push(new UpdateState { Phase = UpdatePhase.Available, Version = version });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Updates: check failed.");
            Reset();
            EmitTransient("Couldn't check for updates. Try again later.", userInitiated);
        }
    }

    public async Task InstallAndRestartAsync()
    {
        if (_manager is null || _pending is null)
        {
            Log.Warning("Updates: install requested but no pending update.");
            return;
        }

        var version = _pending.TargetFullRelease.Version.ToString();

        try
        {
            Push(new UpdateState { Phase = UpdatePhase.Downloading, Version = version, DownloadProgress = 0 });

            await _manager.DownloadUpdatesAsync(_pending, progress =>
                Push(new UpdateState { Phase = UpdatePhase.Downloading, Version = version, DownloadProgress = progress }));

            Log.Information("Updates: download complete, applying and restarting.");
            Push(new UpdateState { Phase = UpdatePhase.Installing, Version = version, DownloadProgress = 100 });
            _manager.ApplyUpdatesAndRestart(_pending);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Updates: failed to download/install update.");
            // Keep the update available so the user can retry.
            Push(new UpdateState { Phase = UpdatePhase.Available, Version = version, TransientMessage = "Update failed. Please try again." });
        }
    }

    private void Push(UpdateState state) => _state.OnNext(state);

    private void Reset() => _state.OnNext(new UpdateState());

    private void EmitTransient(string message, bool userInitiated)
    {
        if (!userInitiated) return;
        // Preserve current availability/version while flashing the message.
        _state.OnNext(_state.Value with { TransientMessage = message });
    }
}
