using System.Text.RegularExpressions;

namespace ChurchProjection.Core.Parsing;

/// <summary>
/// Detects when a speaker is asking the team to find a passage by description rather than by
/// reference ("find me the scripture that talks about going into the nations") and extracts the
/// topic phrase to search for.
/// </summary>
public static partial class TopicalRequestParser
{
    private const int MaxTopicWords = 12;

    // Each pattern captures the topic that follows a request cue.
    [GeneratedRegex(
        @"\b(?:find|get|pull up|bring up|show|give)\s+(?:me\s+|us\s+)?(?:the\s+)?(?:scripture|verse|passage|part|portion|place|chapter)\s+(?:that\s+)?(?:talks?\s+about|says?|where\s+(?:it|the\s+bible)\s+says?|about)\s+(?<topic>.+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FindRequest();

    [GeneratedRegex(
        @"\b(?:the\s+)?(?:scripture|verse|passage)\s+(?:that\s+)?(?:talks?\s+about|says?)\s+(?<topic>.+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ScriptureThat();

    [GeneratedRegex(
        @"\bwhere\s+(?:it|the\s+bible)\s+says?\s+(?<topic>.+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex WhereItSays();

    [GeneratedRegex(
        @"\bthe\s+(?:part|portion|place)\s+(?:where|that)\s+(?:it\s+)?(?:says?|talks?\s+about)\s+(?<topic>.+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ThePartWhere();

    private static readonly Regex[] Patterns =
        [FindRequest(), ThePartWhere(), ScriptureThat(), WhereItSays()];

    /// <summary>
    /// Returns the topic the speaker asked to look up, or null if the utterance is not a request.
    /// </summary>
    public static string? ExtractTopic(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        foreach (var pattern in Patterns)
        {
            var match = pattern.Match(text);
            if (match.Success)
            {
                var topic = CleanTopic(match.Groups["topic"].Value);
                if (!string.IsNullOrWhiteSpace(topic))
                    return topic;
            }
        }
        return null;
    }

    private static string CleanTopic(string raw)
    {
        var topic = raw.Trim().TrimEnd('.', ',', ';', '?', '!');

        // Drop a leading filler conjunction the cue sometimes leaves behind.
        topic = Regex.Replace(topic, @"^(?:that\s+|where\s+|about\s+|how\s+)", "", RegexOptions.IgnoreCase).Trim();

        // Cap length so a whole sentence does not become the query.
        var words = topic.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > MaxTopicWords)
            topic = string.Join(' ', words[..MaxTopicWords]);

        return topic;
    }
}
