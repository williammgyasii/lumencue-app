using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using Serilog;

namespace ChurchProjection.UI.Services;

/// <summary>
/// Initializes LibVLC native libraries. On Apple Silicon the NuGet Mac package ships x64-only,
/// so we fall back to a locally installed VLC.app when present.
/// </summary>
internal static class LibVlcBootstrap
{
    private static readonly object Lock = new();
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        lock (Lock)
        {
            if (_initialized) return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                foreach (var libDir in MacLibDirectories())
                {
                    try
                    {
                        LibVLCSharp.Shared.Core.Initialize(libDir);
                        _initialized = true;
                        Log.Information("LibVLC initialized from {Path}", libDir);
                        return;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "LibVLC init failed for {Path}", libDir);
                    }
                }
            }

            LibVLCSharp.Shared.Core.Initialize();
            _initialized = true;
        }
    }

    private static IEnumerable<string> MacLibDirectories()
    {
        const string vlcAppLib = "/Applications/VLC.app/Contents/MacOS/lib";
        if (Directory.Exists(vlcAppLib))
            yield return vlcAppLib;

        // Homebrew cask/alternate install locations.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var brewVlc = Path.Combine(home, "Applications/VLC.app/Contents/MacOS/lib");
        if (Directory.Exists(brewVlc))
            yield return brewVlc;
    }
}
