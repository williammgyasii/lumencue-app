using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class NdiRuntimeLocatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lc-ndi-").FullName;

    [Fact]
    public void V6_env_path_is_used()
    {
        var v6 = Plant("v6");

        var path = NdiRuntimeLocator.FindWindowsLibrary(v6, runtimeDirV5: null);

        Assert.Equal(Library(v6), path);
    }

    [Fact]
    public void V5_env_path_is_used_when_v6_missing()
    {
        var v5 = Plant("v5");

        var path = NdiRuntimeLocator.FindWindowsLibrary(runtimeDirV6: null, v5);

        Assert.Equal(Library(v5), path);
    }

    [Fact]
    public void Library_directory_leads_PATH()
    {
        var v6 = Plant("v6");
        var library = Library(v6);

        var path = NdiRuntimeLocator.WithLibraryDirectoryFirst("C:\\Windows\\System32", library);

        Assert.StartsWith(v6 + Path.PathSeparator, path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_runtime_on_disk_is_empty()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);

        var path = NdiRuntimeLocator.FindWindowsLibrary(empty, empty);

        Assert.Null(path);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* temp cleanup */ }
    }

    private string Plant(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Library(dir), []);
        return dir;
    }

    private static string Library(string dir) =>
        Path.Combine(dir, NdiRuntimeLocator.WindowsLibraryFile);
}
