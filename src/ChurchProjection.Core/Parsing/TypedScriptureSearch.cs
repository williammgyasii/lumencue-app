using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Core.Parsing;

/// <summary>
/// Fetches every slice of a typed Scripture-tab query in order. Phrase search is the caller's
/// fallback when <see cref="ScriptureReferenceParser.TryParseTypedQuery"/> returns nothing.
/// </summary>
public static class TypedScriptureSearch
{
    public static List<T> FetchSlices<T>(
        IReadOnlyList<ScriptureReference> slices,
        Func<ScriptureReference, IEnumerable<T>> fetch)
    {
        var all = new List<T>();
        foreach (var slice in slices)
            all.AddRange(fetch(slice));
        return all;
    }
}
