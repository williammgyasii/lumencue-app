using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using ChurchProjection.UI.ViewModels.Operator;
using ReactiveUI;
using System.Reactive.Concurrency;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class ContentSearchRangeTests
{
    public ContentSearchRangeTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    [Fact]
    public void ShiftClick_HighlightsTheInclusiveSpan()
    {
        var search = NewSearch(out var v3, out var v4, out var v5);

        search.SetRangeAnchor(v3);
        search.ExtendRangeTo(v5);

        Assert.True(v3.IsRangeSelected);
        Assert.True(v4.IsRangeSelected);
        Assert.True(v5.IsRangeSelected);
        Assert.True(search.HasRangeSelection);
        Assert.Equal(3, search.SelectedRangeItems().Count);
    }

    [Fact]
    public void ShiftClick_WorksBackwardThroughTheGrid()
    {
        var search = NewSearch(out var v3, out var v4, out var v5);

        search.SetRangeAnchor(v5);
        search.ExtendRangeTo(v3);

        Assert.True(v3.IsRangeSelected);
        Assert.True(v4.IsRangeSelected);
        Assert.True(v5.IsRangeSelected);
    }

    [Fact]
    public void PlainClick_ClearsAPreviousRange()
    {
        var search = NewSearch(out var v3, out var v4, out var v5);

        search.SetRangeAnchor(v3);
        search.ExtendRangeTo(v5);
        search.SetRangeAnchor(v4);

        Assert.False(v3.IsRangeSelected);
        Assert.False(v4.IsRangeSelected);
        Assert.False(v5.IsRangeSelected);
        Assert.False(search.HasRangeSelection);
    }

    [Fact]
    public void ShiftClick_WithoutAnAnchor_DoesNotCreateARange()
    {
        var search = NewSearch(out _, out var v4, out _);

        search.ExtendRangeTo(v4);

        Assert.False(v4.IsRangeSelected);
        Assert.False(search.HasRangeSelection);
    }

    [Fact]
    public async Task ReloadingTheChapter_ClearsTheRange()
    {
        var search = new ContentSearchViewModel(new ChapterLibrary());
        await search.LoadFullChapterAsync("John", 3, originVerse: 1);

        search.SetRangeAnchor(search.Results[0]);
        search.ExtendRangeTo(search.Results[2]);
        Assert.True(search.HasRangeSelection);

        await search.LoadFullChapterAsync("John", 3, originVerse: 1);

        Assert.False(search.HasRangeSelection);
        Assert.DoesNotContain(search.Results, r => r.IsRangeSelected);
    }

    private static ContentSearchViewModel NewSearch(
        out ContentItem v3, out ContentItem v4, out ContentItem v5)
    {
        var search = new ContentSearchViewModel(new ChapterLibrary());
        v3 = Verse(3);
        v4 = Verse(4);
        v5 = Verse(5);
        search.Results.Add(v3);
        search.Results.Add(v4);
        search.Results.Add(v5);
        return search;
    }

    private static ContentItem Verse(int n) => new()
    {
        Type = ContentItemType.Scripture,
        Title = $"Genesis 1:{n}",
        Body = $"verse {n}",
        Source = new ScripturePassage { Book = "Genesis", Chapter = 1, VerseStart = n, Text = $"verse {n}" },
    };

    private sealed class ChapterLibrary : IContentLibraryService
    {
        public Task<List<ScripturePassage>> GetOrFetchVersesAsync(
            ScriptureReference reference, string translation = "BSB",
            bool localOnly = false, CancellationToken cancellationToken = default)
        {
            var verses = new List<ScripturePassage>();
            for (var v = 1; v <= 5; v++)
            {
                verses.Add(new ScripturePassage
                {
                    Book = reference.Book,
                    Chapter = reference.Chapter,
                    VerseStart = v,
                    Translation = translation,
                    Text = $"{reference.Book} {reference.Chapter}:{v}",
                });
            }
            return Task.FromResult(verses);
        }

        public Task<List<ScripturePassage>> SearchScripturesAsync(
            string query, string translation = "BSB", CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ScripturePassage>());

        public Task<ScripturePassage?> GetOrFetchScriptureAsync(
            ScriptureReference reference, string translation = "BSB", CancellationToken cancellationToken = default)
            => Task.FromResult<ScripturePassage?>(null);

        public Task<List<Song>> GetAllSongsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<Song>());

        public Task<List<Song>> SearchSongsAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<Song>());

        public Task<Song> ImportSongAsync(string title, string rawLyrics, string? artist = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<Song> SaveSongAsync(Song song, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task DeleteSongAsync(long songId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
