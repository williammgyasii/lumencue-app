using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace ChurchProjection.App;

/// <summary>
/// Over-the-air updates via Velopack, hosted on the private "lumencue-app" GitHub Releases.
/// Because the repo is private, a read-only GitHub token (Updates:GitHubToken, shipped in
/// appsettings.local.json) is required to read releases. No-ops in dev / non-installed builds.
/// </summary>
internal sealed class Updater
{
    private readonly string _repoUrl;
    private readonly string? _token;

    public Updater(IConfiguration config)
    {
        _repoUrl = (config["Updates:RepoUrl"] ?? "").Trim();
        _token = config["Updates:GitHubToken"];
    }

    /// <summary>
    /// Checks GitHub for a newer release; if found, prompts the user and (on accept) downloads
    /// and applies it, then restarts. Safe to fire-and-forget: all failures are swallowed/logged.
    /// </summary>
    public async Task CheckAndPromptAsync()
    {
        if (string.IsNullOrWhiteSpace(_repoUrl))
        {
            Log.Information("Updates: no Updates:RepoUrl configured, skipping update check.");
            return;
        }

        try
        {
            var source = new GithubSource(_repoUrl, _token, prerelease: false);
            var mgr = new UpdateManager(source);

            // Only installed (Velopack-packaged) builds can self-update. Dev/debug runs are skipped.
            if (!mgr.IsInstalled)
            {
                Log.Information("Updates: not an installed build, skipping update check.");
                return;
            }

            var info = await mgr.CheckForUpdatesAsync();
            if (info is null)
            {
                Log.Information("Updates: already up to date.");
                return;
            }

            var newVersion = info.TargetFullRelease.Version.ToString();
            Log.Information("Updates: new version {Version} available.", newVersion);

            var accept = await ShowPromptAsync(newVersion);
            if (!accept)
            {
                Log.Information("Updates: user declined update to {Version}.", newVersion);
                return;
            }

            await mgr.DownloadUpdatesAsync(info);
            Log.Information("Updates: downloaded {Version}, applying and restarting.", newVersion);
            mgr.ApplyUpdatesAndRestart(info);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Updates: update check/apply failed (continuing on current version).");
        }
    }

    private static Task<bool> ShowPromptAsync(string newVersion)
    {
        var tcs = new TaskCompletionSource<bool>();

        // Build/show the dialog on the UI thread; the awaiting caller may be on a thread-pool thread.
        Dispatcher.UIThread.Post(() =>
        {
            var title = new TextBlock
            {
                Text = "Update available",
                FontSize = 18,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White
            };

            var message = new TextBlock
            {
                Text = $"LumenCue {newVersion} is ready to install. The app will restart to finish updating.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xCD, 0xD6))
            };

            var later = new Button { Content = "Later", MinWidth = 90 };
            var install = new Button
            {
                Content = "Restart & update",
                MinWidth = 130,
                Background = new SolidColorBrush(Color.FromRgb(0x4F, 0x8A, 0xFF)),
                Foreground = Brushes.White
            };

            var window = new Window
            {
                Title = "LumenCue update",
                Width = 420,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1E, 0x24)),
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(24),
                    Spacing = 16,
                    Children =
                    {
                        title,
                        message,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 10,
                            Children = { later, install }
                        }
                    }
                }
            };

            later.Click += (_, _) => { tcs.TrySetResult(false); window.Close(); };
            install.Click += (_, _) => { tcs.TrySetResult(true); window.Close(); };
            window.Closed += (_, _) => tcs.TrySetResult(false);

            window.Show();
        });

        return tcs.Task;
    }
}
