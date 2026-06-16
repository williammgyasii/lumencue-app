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
    private readonly BehaviorSubject<UpdateState> _state = new(new UpdateState(false, null));
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
            if (userInitiated) Emit(transient: "Updates are not configured for this build.");
            return;
        }

        try
        {
            var source = new GithubSource(_repoUrl, _token, prerelease: false);
            var mgr = new UpdateManager(source);

            if (!mgr.IsInstalled)
            {
                Log.Information("Updates: not an installed build, skipping check.");
                if (userInitiated) Emit(transient: "Updates only run in the installed app.");
                return;
            }

            var info = await mgr.CheckForUpdatesAsync();
            if (info is null)
            {
                Log.Information("Updates: already up to date.");
                if (userInitiated) Emit(transient: "You're on the latest version.");
                return;
            }

            _manager = mgr;
            _pending = info;
            var version = info.TargetFullRelease.Version.ToString();
            Log.Information("Updates: version {Version} available.", version);
            _state.OnNext(new UpdateState(true, version));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Updates: check failed.");
            if (userInitiated) Emit(transient: "Couldn't check for updates. Try again later.");
        }
    }

    public async Task InstallAndRestartAsync()
    {
        if (_manager is null || _pending is null)
        {
            Log.Warning("Updates: install requested but no pending update.");
            return;
        }

        try
        {
            Log.Information("Updates: downloading and applying update, will restart.");
            await _manager.DownloadUpdatesAsync(_pending);
            _manager.ApplyUpdatesAndRestart(_pending);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Updates: failed to install update.");
            Emit(transient: "Update failed to install. Please try again.");
        }
    }

    // Emits a one-off message while preserving the current availability/version state.
    // The view model is responsible for auto-dismissing the message after a short delay.
    private void Emit(string transient)
        => _state.OnNext(_state.Value with { TransientMessage = transient });
}
