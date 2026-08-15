using ChurchProjection.Core.Parsing;
using ChurchProjection.Core.Services;
using ChurchProjection.Infrastructure.Matching;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class SuggestionEngineInterimTests
{
    [Fact]
    public async Task PushInterim_FeedsTheSpokenReferenceBuilder()
    {
        var matcher = new RecordingMatcher();
        using var engine = new SuggestionEngine(matcher);

        engine.PushInterim("let's go to John");
        await Task.Delay(50);

        Assert.Contains("let's go to John", matcher.Accumulated);
    }

    private sealed class RecordingMatcher : IAiMatcherService
    {
        public List<string> Accumulated { get; } = [];

        public string CurrentTranslation { get; set; } = "BSB";
        public bool IncludeContentMatches { get; set; }
        public IObservable<ReferenceNotFound> ReferenceNotFound =>
            System.Reactive.Linq.Observable.Empty<ReferenceNotFound>();

        public Task<List<AiSuggestion>> MatchAsync(string transcriptChunk, bool scriptureOnly = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new List<AiSuggestion>());

        public void UpdateContentLibrary(IEnumerable<string> contentTexts, IEnumerable<string> contentIds) { }
        public void NoteSpokenSegment(string finalSegmentText) { }

        public Task<List<AiSuggestion>> NavigateAsync(NavCommand command, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<AiSuggestion>());

        public Task<List<AiSuggestion>> AccumulateSpokenAsync(string finalSegmentText,
            CancellationToken cancellationToken = default)
        {
            Accumulated.Add(finalSegmentText);
            return Task.FromResult(new List<AiSuggestion>());
        }
    }
}
