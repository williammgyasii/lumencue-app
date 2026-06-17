using System.Text.RegularExpressions;

namespace ChurchProjection.Core.Parsing;

/// <summary>The translation a speaker asked for out loud, e.g. "can I get it in the King James".</summary>
public sealed record SpokenTranslationRequest(string Code, string DisplayName);

/// <summary>
/// Detects a spoken request to switch Bible translation — "can I get it in the King James", "in the
/// NIV", "read it in the Message". Deliberately conservative: common-word names ("the message", "the
/// passion") only match alongside a request cue ("get"/"read"/"in"/"version"…), so ordinary preaching
/// that merely mentions "the message" never flips the translation.
///
/// It reports the requested translation (including ones the app may not carry, like TPT/ESV) so the
/// caller can either switch or tell the operator that translation isn't available.
/// </summary>
public static class SpokenTranslationParser
{
    private enum Strictness { Acronym, Distinctive, Ambiguous }

    private sealed record Entry(string Code, string Display, Strictness Strictness, string[] Aliases);

    // Ordered most-specific first (e.g. "new king james" before "king james").
    private static readonly Entry[] Entries =
    [
        new("NKJV", "New King James Version", Strictness.Distinctive, ["nkjv", "new king james"]),
        new("KJV",  "King James Version",     Strictness.Distinctive, ["kjv", "king james"]),
        new("NIV",  "New International Version", Strictness.Distinctive, ["niv", "new international"]),
        new("NLT",  "New Living Translation",  Strictness.Distinctive, ["nlt", "new living"]),
        new("AMP",  "Amplified Bible",         Strictness.Distinctive, ["amp", "amplified"]),
        new("CSB",  "Christian Standard Bible", Strictness.Distinctive, ["csb", "christian standard"]),
        new("BSB",  "Berean Standard Bible",   Strictness.Distinctive, ["bsb", "berean"]),
        new("ESV",  "English Standard Version", Strictness.Distinctive, ["esv", "english standard"]),
        // Acronyms for the two common-word names are unambiguous; the phrases need a request cue.
        new("MSG",  "The Message",             Strictness.Acronym,   ["msg"]),
        new("MSG",  "The Message",             Strictness.Ambiguous, ["the message", "message translation", "message version", "message bible"]),
        new("TPT",  "The Passion Translation", Strictness.Acronym,   ["tpt"]),
        new("TPT",  "The Passion Translation", Strictness.Ambiguous, ["the passion", "passion translation", "passion version", "passion bible"]),
    ];

    private static readonly Regex RequestVerb = new(
        @"\b(get|getting|gets|read|reading|give|giving|gimme|switch|switching|put|putting|want|change|changing|see|show|showing|have|pull|bring|use|using|do)\b",
        RegexOptions.Compiled);

    private static readonly Regex Preposition = new(@"\b(in|into|to)\b", RegexOptions.Compiled);
    private static readonly Regex Modifier = new(@"\b(version|translation|bible)\b", RegexOptions.Compiled);

    public static SpokenTranslationRequest? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var normalized = Normalize(text);
        var hasRequestVerb = RequestVerb.IsMatch(normalized);
        var hasPreposition = Preposition.IsMatch(normalized);
        var hasModifier = Modifier.IsMatch(normalized);

        foreach (var entry in Entries)
        {
            foreach (var alias in entry.Aliases)
            {
                if (!ContainsPhrase(normalized, alias)) continue;

                var ok = entry.Strictness switch
                {
                    // A bare acronym ("kjv", "msg") is a strong enough signal on its own.
                    Strictness.Acronym => true,
                    // Distinctive names ("king james") need any hint that a translation is meant.
                    Strictness.Distinctive => hasRequestVerb || hasPreposition || hasModifier,
                    // Common-word names ("the message") need an explicit request, not a passing mention.
                    Strictness.Ambiguous => hasModifier || (hasRequestVerb && hasPreposition),
                    _ => false,
                };

                if (ok) return new SpokenTranslationRequest(entry.Code, entry.Display);
            }
        }

        return null;
    }

    // Whole-phrase containment on the normalized, space-padded text so "niv" doesn't hit "deniving".
    private static bool ContainsPhrase(string normalized, string phrase)
        => normalized.Contains(' ' + phrase + ' ', StringComparison.Ordinal);

    private static string Normalize(string text)
    {
        var lower = text.ToLowerInvariant();
        // Keep only letters/digits as separators-collapsed tokens.
        lower = Regex.Replace(lower, @"[^a-z0-9]+", " ");
        lower = Regex.Replace(lower, @"\s+", " ").Trim();
        // Collapse spelled-out acronyms ("k j v" -> "kjv", "n i v" -> "niv") so STT letter-spacing
        // still matches. Only runs of single letters are joined; real words are untouched.
        lower = Regex.Replace(lower, @"\b([a-z])(?:\s([a-z]))+\b", m => m.Value.Replace(" ", ""));
        // Pad so ContainsPhrase can match at the boundaries.
        return ' ' + lower + ' ';
    }
}
