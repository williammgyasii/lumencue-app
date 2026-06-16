namespace ChurchProjection.App;

/// <summary>
/// Resolves per-user writable locations for app data (database, logs). Keeps writes out of the
/// install directory so the app runs correctly when installed under Program Files.
/// </summary>
internal static class AppPaths
{
    /// <summary>%LocalAppData%\ChurchProjection — created on access.</summary>
    public static string DataDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChurchProjection");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Full path to the local SQLite database file.</summary>
    public static string DatabasePath => Path.Combine(DataDirectory, "churchprojection.db");
}
