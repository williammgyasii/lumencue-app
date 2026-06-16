namespace ChurchProjection.Core.Models.Content;

/// <summary>A verse returned by topical/paraphrase search, with its relevance score and how it matched.</summary>
public sealed record ScriptureSearchHit(ScripturePassage Passage, double Score, string MatchKind)
{
    public const string KindSemantic = "semantic";
    public const string KindKeyword = "keyword";
    public const string KindHybrid = "hybrid";
}
