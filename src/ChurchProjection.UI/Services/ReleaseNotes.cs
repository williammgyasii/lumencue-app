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
        ["0.7.26"] =
        [
            "Live backgrounds: pick a photo or looping video from the tiles when a theme is set to Placeholder — it fills the projector.",
            "Shift+click two verses to highlight a range, then right-click Bookmark Verse to save the whole span (like Genesis 1:3-8).",
            "Compare Translations sits next to Program — pick up to two translations and double-click a card to send it live.",
            "Theme Studio: delete a theme with the trash button next to the name. The + menu is create-only now.",
        ],
        ["0.7.25"] =
        [
            "Cloud sign-in, library sync and live transcription are back — the app now connects to our new hosting.",
            "Custom Bible translations (The Passion Translation, Living Bible, ESV, and others) download again — pick one in Settings and it caches for offline use.",
            "Blank screen now stays solid black instead of showing your theme's background colour.",
        ],
        ["0.7.24"] =
        [
            "Switching Bible translations while a verse is live no longer blanks the slide — if the new translation isn't ready yet, what's on screen stays up.",
            "Bookmarked verses: right-click for Send to Live, Show in Chapter, or Show Entire Book.",
            "Notes editor with slide splitting, plus click a note to browse its slides before sending live.",
            "Mac (Apple Silicon) builds and NDI output for OBS.",
        ],
        ["0.7.23"] =
        [
            "LumenCue now runs natively on Mac (Apple Silicon) — install and project from your MacBook or Mac mini.",
            "NDI output for OBS: turn on the NDI screen in Settings and add “LumenCue Program” as an NDI Source in OBS (DistroAV plugin).",
            "Notes got a big upgrade — paste long teaching notes, split them into slides by paragraph or section, preview with your theme, and click a note to browse its slides before sending live.",
            "Bookmarked verses now offer Send to Live, Show in Chapter, and Show Entire Book from the right-click menu.",
        ],
        ["0.7.22"] =
        [
            "New Notes tab — write a title and body (great for prayer points). Each saved note becomes a slide card; double-click it to show it on screen.",
            "Add a note with “+ Add note”, and right-click any note to edit or delete it.",
            "Notes are styled with your Scripture theme, so they look consistent with how you send scripture and songs.",
        ],
        ["0.7.21"] =
        [
            "Scripture references read out digit-by-digit — like saying a Psalm as \"one-oh-nine\" for Psalm 109 — are now recognised correctly.",
            "The Message (MSG) now jumps to the exact verse you ask for instead of the whole chapter, even where it groups verses together — and passages you'd already downloaded refresh automatically so they're fixed too.",
        ],
        ["0.7.20"] =
        [
            "Scripture references read out digit-by-digit are now understood — saying a Psalm as \"one-oh-nine\" correctly finds Psalm 109 instead of getting lost in the spaces.",
            "The Message (MSG) now jumps to the exact verse you asked for, even where it groups several verses together, instead of showing the whole chapter.",
        ],
        ["0.7.19"] =
        [
            "The song editor now opens full screen with a bigger middle panel, so there's more room to arrange verses, chorus and bridge.",
            "Adding a song is simpler — every \"add song\" button now opens the same editor, where you paste lyrics and it auto-detects the sections.",
            "Tidied up the editor: panels snap into place instead of sliding off, and buttons and labels are properly centred.",
        ],
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
