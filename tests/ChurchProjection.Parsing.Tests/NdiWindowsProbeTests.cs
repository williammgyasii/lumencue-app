using ChurchProjection.Core.Services;
using ChurchProjection.UI.Services;
using NewTek;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

/// <summary>
/// Windows-only. The 0.7.31 crash: we SetDllImportResolver, then NDIlib's cctor tried to set
/// another. This test plants a dummy runtime DLL so the locator hits, then probes.
/// </summary>
public class NdiWindowsProbeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lc-ndi-probe-").FullName;
    private readonly string? _previousV6;

    public NdiWindowsProbeTests()
    {
        _previousV6 = Environment.GetEnvironmentVariable("NDI_RUNTIME_DIR_V6");
        var dir = Path.Combine(_root, "v6");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, NdiRuntimeLocator.WindowsLibraryFile), []);
        Environment.SetEnvironmentVariable("NDI_RUNTIME_DIR_V6", dir);
    }

    [Fact]
    public void Second_resolver_is_not_set()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var probe = new NdiOutputService();

        var ex = Record.Exception(() => NDIlib.initialize());

        if (ex is TypeInitializationException { InnerException: InvalidOperationException inner }
            && inner.Message.Contains("resolver is already set", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail("NDIlib type initializer hit a second DllImport resolver.");
        }
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("NDI_RUNTIME_DIR_V6", _previousV6);
        try { Directory.Delete(_root, recursive: true); }
        catch { /* temp cleanup */ }
    }
}
