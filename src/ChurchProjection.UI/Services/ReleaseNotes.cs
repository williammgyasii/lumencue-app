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
        ["0.7.18"] =
        [
            "More Bible translations to choose from — The Passion Translation, The Living Bible, ESV, NET, Good News, Amplified (Classic), Easy-to-Read and ASV.",
            "Pick a translation once and it downloads in the background, then works fully offline like the rest.",
            "Verses detected while you're preaching now have their own \"Paraphrases\" tab, so they no longer crowd your scripture search.",
        ],
        ["0.7.17"] =
        [
            "Sign in is now optional — the app still opens straight to your library, with a new \"Sign in\" button up top whenever you're ready.",
            "Sign in to turn on live AI transcription, premium Bible translations and library sync across your machines.",
        ],
        ["0.7.16"] =
        [
            "The app now opens straight to your library — no sign-in needed to get started.",
            "Everything you project locally — songs, scripture, themes and media — works the moment you launch.",
        ],
        ["0.7.15"] =
        [
            "Fine-tune imported lower-third designs right in the app — nudge and reposition your artwork freely, even when it fills the whole frame, so there's no more trips back to Photoshop.",
            "Design themes by feel: click any element directly on the canvas to select and drag it, with cleaner resize handles and a live size readout.",
            "Name your theme layers (double-click to rename) so busy designs stay easy to follow.",
            "Your media folders now live in the main sidebar, switching alongside whatever mode you're in.",
        ],
        ["0.7.14"] =
        [
            "Import your church's own lower-third designs — drop in your artwork and overlay live scripture and titles on top, automatically sized to fit the screen.",
            "Organise media into folders, and the app now skips files you've already added so your library stays clean.",
            "A media control bar now stays on screen wherever you are, so you can stop what's playing without hopping back to the Media tab.",
            "Choose which speaker or audio device a video plays through, and switch it live without interrupting playback.",
        ],
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
