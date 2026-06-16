using System.Text.RegularExpressions;

namespace ChurchProjection.Core.Parsing;

/// <summary>Spoken navigation intents the operator can trigger hands-free.</summary>
public enum NavCommand
{
    None,
    NextVerse,
    PreviousVerse,
}

/// <summary>
/// Detects spoken navigation commands ("next verse", "go back a verse", ...) from a single final
/// transcription utterance. Kept deliberately tight so ordinary preaching speech does not trip it.
/// </summary>
public static partial class VoiceCommandParser
{
    [GeneratedRegex(
        @"\b(?:next|following|the\s+next)\s+(?:verse|one)\b|\bgo\s+(?:on\s+)?(?:to\s+)?(?:the\s+)?next\s+verse\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NextPattern();

    [GeneratedRegex(
        @"\b(?:previous|prior|last)\s+(?:verse|one)\b|\bverse\s+before\b|\bgo\s+back(?:\s+(?:a|one)\s+verse)?\b|\bback\s+(?:a|one)\s+verse\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PreviousPattern();

    /// <summary>
    /// Returns the navigation command spoken in <paramref name="text"/>, or <see cref="NavCommand.None"/>.
    /// When both directions appear, the one spoken later wins.
    /// </summary>
    public static NavCommand Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return NavCommand.None;

        var next = NextPattern().Match(text);
        var prev = PreviousPattern().Match(text);

        if (next.Success && prev.Success)
            return next.Index >= prev.Index ? NavCommand.NextVerse : NavCommand.PreviousVerse;
        if (next.Success) return NavCommand.NextVerse;
        if (prev.Success) return NavCommand.PreviousVerse;
        return NavCommand.None;
    }
}
