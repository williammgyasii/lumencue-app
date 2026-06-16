using Avalonia;
using Avalonia.Fonts.Inter;
using Avalonia.ReactiveUI;
using Serilog;
using Velopack;

namespace ChurchProjection.App;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack must run before anything else: it handles the install/update/uninstall
        // lifecycle hooks and may exit the process early (e.g. right after an update is applied).
        VelopackApp.Build().Run();

        // Write logs to a per-user, writable location so the app works when installed under
        // Program Files (where the install directory is read-only for standard users).
        var logPath = Path.Combine(AppPaths.DataDirectory, "logs", "church-projection-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Log.Information("LumenCue starting...");

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application crashed");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .UseReactiveUI()
            .LogToTrace();
}
