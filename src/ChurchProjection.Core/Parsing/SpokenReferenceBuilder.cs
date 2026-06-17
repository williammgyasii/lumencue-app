using System.Text.RegularExpressions;
using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Core.Parsing;

/// <summary>
/// Assembles a scripture reference that a speaker utters in fragments across pauses — e.g.
/// "Matthew" … (long pause) … "chapter two" … (pause) … "verse number three" → Matthew 2:3.
///
/// The matcher's normal path only recognises a reference when book/chapter/verse are contiguous
/// inside one sliding-window of speech, which a preacher's stop-start delivery defeats. This builder
/// is fed the per-utterance segment stream instead and keeps a single <em>pending</em> reference
/// alive across gaps, surfacing the book's opening verse the moment a book is named, then refining it
/// to the whole chapter and finally the exact verse as those are spoken.
///
/// It is deliberately conservative: a bare number is ignored until a book is pending, naming a new
/// book restarts the reference, and the pending state expires after a configurable idle timeout so
/// unrelated numbers spoken much later are never stitched on.
/// </summary>
public sealed class SpokenReferenceBuilder
{
    private readonly TimeSpan _timeout;
    private readonly object _gate = new();

    private string? _book;
    private int _chapter;
    private int _verse;
    private int _verseEnd;
    // True once a range connector ("to"/"through") has followed a known start verse. Persisted as a
    // field (not a per-utterance local) so a range split across pauses — "verse one to" … (pause) …
    // "five" — still attaches the trailing number as the range end.
    private bool _expectRangeEnd;
    private DateTimeOffset _lastUpdate = DateTimeOffset.MinValue;
    private string? _lastEmittedId;

    public SpokenReferenceBuilder(TimeSpan? timeout = null)
        => _timeout = timeout ?? TimeSpan.FromSeconds(30);

    /// <summary>Drops any half-built reference (e.g. when listening stops or a song takes over).</summary>
    public void Reset()
    {
        lock (_gate) ClearLocked();
    }

    private void ClearLocked()
    {
        _book = null;
        _chapter = 0;
        _verse = 0;
        _verseEnd = 0;
        _expectRangeEnd = false;
        _lastEmittedId = null;
    }

    /// <summary>
    /// Feeds one final utterance. Returns a reference to surface when the pending reference gained
    /// new, showable information (book+chapter, or a refined verse); otherwise null.
    /// </summary>
    public ScriptureReference? Accept(string? segmentText, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(segmentText)) return null;

        lock (_gate)
        {
            // Expire a stale pending reference before considering this utterance, so a number heard
            // long after the book is never glued on.
            if (_book is not null && now - _lastUpdate > _timeout)
                ClearLocked();

            var changed = ScanLocked(segmentText, now);
            if (!changed || _book is null) return null;

            var hasChapter = _chapter > 0;
            var hasVerse = _verse > 0;
            var hasRange = hasVerse && _verseEnd > _verse;

            ScriptureReference reference;
            string id;
            if (hasVerse)
            {
                reference = new ScriptureReference(_book, _chapter, _verse, hasRange ? _verseEnd : (int?)null);
                id = hasRange ? $"{_book}|{_chapter}|{_verse}|{_verseEnd}" : $"{_book}|{_chapter}|{_verse}";
            }
            else if (hasChapter)
            {
                reference = new ScriptureReference(_book, _chapter, VerseStart: 1, VerseEnd: ScriptureReference.WholeChapterSentinel);
                id = $"{_book}|{_chapter}|chapter";
            }
            else
            {
                // Only the book has been named so far ("let's go to Matthew"). Surface its opening verse
                // (Matthew 1:1) so the operator can load it immediately and navigate from there once the
                // preacher follows up with the chapter/verse. Internal _chapter stays 0 so a chapter
                // spoken next is still read as the chapter, not a verse.
                reference = new ScriptureReference(_book, Chapter: 1, VerseStart: 1, VerseEnd: null);
                id = $"{_book}|book";
            }

            if (id == _lastEmittedId) return null; // nothing new to show
            _lastEmittedId = id;
            return reference;
        }
    }

    private enum Expect { Auto, Chapter, Verse }

    /// <summary>Walks the utterance's tokens, updating the pending reference. Returns true if any
    /// field (book/chapter/verse) changed.</summary>
    private bool ScanLocked(string segmentText, DateTimeOffset now)
    {
        var normalized = Normalize(segmentText);
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var changed = false;
        var expect = Expect.Auto;

        for (var i = 0; i < tokens.Length; i++)
        {
            // Try to read a book name spanning up to three tokens ("song of solomon", "1 john").
            var (book, consumed) = TryReadBook(tokens, i);
            if (book is not null)
            {
                if (!string.Equals(book, _book, StringComparison.OrdinalIgnoreCase))
                {
                    // A different book starts a brand-new reference.
                    _book = book;
                    _lastEmittedId = null;
                }
                // Naming the book (even the same one) restarts positional chapter/verse filling for
                // this utterance, so restating "Psalm 23" re-reads 23 as the chapter rather than
                // appending it as a verse. The dedupe id is kept for the same book so an identical
                // restatement is not surfaced twice.
                _chapter = 0;
                _verse = 0;
                _verseEnd = 0;
                _expectRangeEnd = false;
                expect = Expect.Chapter;
                changed = true;
                _lastUpdate = now;
                i += consumed - 1;
                continue;
            }

            var word = tokens[i];
            if (word is "chapter") { expect = Expect.Chapter; continue; }
            if (word is "verse" or "verses") { expect = Expect.Verse; continue; }

            // A range connector after a known start verse arms the next number as the range end
            // ("verse one to seven", "verses one through seven", "1-7" → "1 to 7").
            if (word is "to" or "through" or "thru")
            {
                if (_verse > 0) { _expectRangeEnd = true; _lastUpdate = now; }
                continue;
            }

            if (TryReadNumber(word, out var n))
            {
                if (_book is null) { expect = Expect.Auto; _expectRangeEnd = false; continue; } // never fabricate from a stray number

                if (_expectRangeEnd && _verse > 0)
                {
                    if (IsPlausibleVerse(n) && n > _verse) { _verseEnd = n; changed = true; _lastUpdate = now; }
                    _expectRangeEnd = false;
                    expect = Expect.Auto;
                    continue;
                }

                if (expect == Expect.Verse && _chapter > 0)
                {
                    if (IsPlausibleVerse(n) && n != _verse) { _verse = n; _verseEnd = 0; _expectRangeEnd = false; changed = true; _lastUpdate = now; }
                }
                else if (_chapter == 0)
                {
                    if (IsPlausibleChapter(n)) { _chapter = n; changed = true; _lastUpdate = now; }
                }
                else if (_verse == 0)
                {
                    if (IsPlausibleVerse(n)) { _verse = n; _expectRangeEnd = false; changed = true; _lastUpdate = now; }
                }
                expect = Expect.Auto;
            }
        }

        return changed;
    }

    private static (string? Book, int Consumed) TryReadBook(string[] tokens, int start)
    {
        var max = Math.Min(3, tokens.Length - start);
        for (var len = max; len >= 1; len--)
        {
            var candidate = string.Join(' ', tokens, start, len);
            var book = ScriptureReferenceParser.NormalizeBookStrict(candidate);
            if (book is not null) return (book, len);
        }
        return (null, 0);
    }

    private static bool TryReadNumber(string token, out int value)
        => int.TryParse(token, out value) && value > 0;

    private static bool IsPlausibleChapter(int n) => n is >= 1 and <= 150;
    private static bool IsPlausibleVerse(int n) => n is >= 1 and <= 176;

    private static string Normalize(string text)
    {
        var lower = text.ToLowerInvariant();
        // Ordinal book prefixes: "first john" → "1 john".
        lower = Regex.Replace(lower, @"\b(first|1st)\b", "1");
        lower = Regex.Replace(lower, @"\b(second|2nd)\b", "2");
        lower = Regex.Replace(lower, @"\b(third|3rd)\b", "3");
        lower = ScriptureReferenceParser.ReplaceSpokenNumbers(lower);
        // A hyphen between two numbers is a spoken/smart-formatted range ("verse 1-7"); turn it into
        // an explicit "to" connector so the scanner reads it as start→end.
        lower = Regex.Replace(lower, @"(\d)\s*[-\u2013\u2014]\s*(\d)", "$1 to $2");
        // Keep only words and digits as space-separated tokens; cue words ("chapter"/"verse") survive.
        lower = Regex.Replace(lower, @"[^a-z0-9]+", " ");
        return Regex.Replace(lower, @"\s+", " ").Trim();
    }
}
