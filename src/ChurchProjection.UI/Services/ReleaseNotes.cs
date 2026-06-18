namespace ChurchProjection.UI.Services;

/// <summary>
/// User-facing "What's new" highlights per release, shown once after the app auto-updates to a new
/// version. Keep entries short, friendly and non-technical — operators, not engineers, read these.
///
/// To add a release: add a new entry keyed by the bare version (no leading "v"), newest first. The
/// version must match the tag/csproj version the build ships (e.g. tag v0.7.11 -> key "0.7.11").
/// </summary>
public static class ReleaseNotes
{
    private static readonly Dictionary<string, string[]> Notes = new()
    {
        ["0.7.13"] =
        [
            "More accurate AI transcription — improved how your microphone audio is processed so spoken words are recognised more reliably.",
            "Spoken scripture references now appear as you say them, instead of lagging a sentence behind.",
            "Smarter verse handling — if an exact verse isn't found we now show the chapter instead of nothing, and obviously misheard numbers are ignored.",
        ],
        ["0.7.12"] =
        [
            "Smarter verse search — find a passage from just a few spoken or typed words, even when they aren't word-for-word exact.",
            "Quicker-feeling launch — a new startup screen shows progress instead of a blank wait.",
            "Your AI listening minutes now stay visible in the top bar at all times.",
            "New \u201CMic sensitivity\u201D slider to boost quiet microphones, and switching microphones while listening now takes effect instantly.",
            "More reliable sign-in — if your session expires you're returned to the sign-in screen automatically.",
            "This update screen! We'll show a short summary of what's changed each time you update.",
        ],
    };

    /// <summary>Returns the highlights for a version (bare, e.g. "0.7.11" — a leading "v" is tolerated),
    /// or null when there are no notes to show for it.</summary>
    public static string[]? ForVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var key = version.TrimStart('v', 'V');
        return Notes.TryGetValue(key, out var items) && items.Length > 0 ? items : null;
    }
}
