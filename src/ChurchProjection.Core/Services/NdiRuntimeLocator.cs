namespace ChurchProjection.Core.Services;

/// <summary>
/// Finds the NDI native library churches already installed (NDI Tools / Runtime).
/// Windows P/Invoke is <c>NDILib</c>; the file on disk is <see cref="WindowsLibraryFile"/>.
/// </summary>
public static class NdiRuntimeLocator
{
    public const string WindowsLibraryFile = "Processing.NDI.Lib.x64.dll";

    public static string? FindWindowsLibrary(
        string? runtimeDirV6,
        string? runtimeDirV5,
        params string[] extraRoots)
    {
        foreach (var dir in Candidates(runtimeDirV6, runtimeDirV5, extraRoots))
        {
            var path = Path.Combine(dir, WindowsLibraryFile);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static IEnumerable<string> Candidates(
        string? runtimeDirV6,
        string? runtimeDirV5,
        string[] extraRoots)
    {
        if (!string.IsNullOrWhiteSpace(runtimeDirV6))
            yield return runtimeDirV6.Trim();
        if (!string.IsNullOrWhiteSpace(runtimeDirV5))
            yield return runtimeDirV5.Trim();
        foreach (var root in extraRoots)
        {
            if (!string.IsNullOrWhiteSpace(root))
                yield return root.Trim();
        }
    }
}
