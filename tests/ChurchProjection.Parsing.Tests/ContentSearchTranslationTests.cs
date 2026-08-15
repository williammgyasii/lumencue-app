using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using ChurchProjection.UI.ViewModels.Operator;
using ReactiveUI;
using System.Reactive.Concurrency;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class ContentSearchTranslationTests
{
    public ContentSearchTranslationTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    [Fact]
    public async Task ChangingTranslation_KeepsTheChapterVerseCards()
    {
        var library = new FakeLibrary();
        var search = new ContentSearchViewModel(library);

        await search.LoadFullChapterAsync("John", 3, originVerse: 3);
        Assert.Equal(5, search.Results.Count);
        Assert.Contains(search.Results, r => r.Title.Contains("3:3") && r.Tag == "BSB");

        search.SelectedTranslation = "NIV";
        await search.HandleTranslationChangeAsync(originVerse: 3);

        Assert.Equal(5, search.Results.Count);
        Assert.Contains(search.Results, r => r.Title.Contains("3:3") && r.Tag == "NIV");
        Assert.DoesNotContain(search.Results, r => r.Tag == "search-hit");
    }

    [Fact]
    public async Task ChangingTranslation_WithLiveVerse_LoadsChapterInsteadOfSearch()
    {
        var library = new FakeLibrary();
        var search = new ContentSearchViewModel(library);

        search.SelectedTranslation = "NIV";
        await search.HandleTranslationChangeAsync(
            originVerse: 3,
            liveRef: new ScriptureReference("John", 3, 3));

        Assert.Equal(5, search.Results.Count);
        Assert.Contains(search.Results, r => r.Title.Contains("3:3") && r.Tag == "NIV");
        Assert.DoesNotContain(search.Results, r => r.Tag == "search-hit");
    }

    [Fact]
    public async Task ResetForMode_Bible_PreloadsGenesis1()
    {
        var search = new ContentSearchViewModel(new FakeLibrary());

        await search.ResetForModeAsync(songsMode: false);

        Assert.Equal("Genesis 1", search.SearchQuery);
        Assert.Equal(5, search.Results.Count);
        Assert.All(search.Results, r => Assert.Equal(ContentItemType.Scripture, r.Type));
    }

    [Fact]
    public async Task ClearingSearchInBibleMode_DoesNotLoadSongs()
    {
        var search = new ContentSearchViewModel(new FakeLibrary());
        await search.ResetForModeAsync(songsMode: false);

        search.SearchQuery = "";
        await Task.Delay(500);

        Assert.NotEmpty(search.Results);
        Assert.All(search.Results, r => Assert.Equal(ContentItemType.Scripture, r.Type));
        Assert.DoesNotContain(search.Results, r => r.Type == ContentItemType.Song);
    }

    [Fact]
    public async Task StageReference_LoadsADifferentChapterAndHighlightsTheVerse()
    {
        var search = new ContentSearchViewModel(new FakeLibrary());
        await search.ResetForModeAsync(songsMode: false);

        await search.StageReferenceAsync("John", 3, 3);

        Assert.Equal("John 3", search.SearchQuery);
        Assert.All(search.Results, r => Assert.Equal(ContentItemType.Scripture, r.Type));
        Assert.Contains(search.Results, r => r.IsOrigin && r.Title.Contains("3:3"));
        Assert.Contains("3:3", search.SelectedItem?.Title ?? "");
    }

    [Fact]
    public async Task StageReference_SameChapter_OnlyMovesTheHighlight()
    {
        var library = new FakeLibrary();
        var search = new ContentSearchViewModel(library);
        await search.LoadFullChapterAsync("John", 3, originVerse: 1);
        var fetches = library.VerseFetches;

        await search.StageReferenceAsync("John", 3, 3);

        Assert.Equal(fetches, library.VerseFetches);
        Assert.Contains(search.Results, r => r.IsOrigin && r.Title.Contains("3:3"));
        Assert.DoesNotContain(search.Results, r => r.IsOrigin && r.Title.Contains("3:1"));
    }

    [Fact]
    public async Task BibleKeywordSearch_DoesNotIncludeMatchingSongs()
    {
        var search = new ContentSearchViewModel(new FakeLibrary());
        await search.ResetForModeAsync(songsMode: false);

        search.SearchQuery = "love";
        await Task.Delay(500);

        Assert.Contains(search.Results, r => r.Type == ContentItemType.Scripture);
        Assert.DoesNotContain(search.Results, r => r.Type == ContentItemType.Song);
    }

    private sealed class FakeLibrary : IContentLibraryService
    {
        public int VerseFetches { get; private set; }

        public Task<List<ScripturePassage>> GetOrFetchVersesAsync(
            ScriptureReference reference, string translation = "BSB",
            bool localOnly = false, CancellationToken cancellationToken = default)
        {
            VerseFetches++;
            var verses = new List<ScripturePassage>();
            for (var v = 1; v <= 5; v++)
            {
                verses.Add(new ScripturePassage
                {
                    Book = reference.Book,
                    Chapter = reference.Chapter,
                    VerseStart = v,
                    Translation = translation,
                    Text = $"{translation} John {reference.Chapter}:{v}",
                });
            }
            return Task.FromResult(verses);
        }

        public Task<List<ScripturePassage>> SearchScripturesAsync(
            string query, string translation = "BSB", CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ScripturePassage>
            {
                new()
                {
                    Book = "John", Chapter = 3, VerseStart = 1, Translation = "search-hit",
                    Text = "this search result must not replace the chapter grid",
                },
            });

        public Task<ScripturePassage?> GetOrFetchScriptureAsync(
            ScriptureReference reference, string translation = "BSB", CancellationToken cancellationToken = default)
            => Task.FromResult<ScripturePassage?>(null);

        public Task<List<Song>> GetAllSongsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<Song> { SampleSong() });

        public Task<List<Song>> SearchSongsAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<Song> { SampleSong() });

        private static Song SampleSong() => new()
        {
            Title = "Amazing Grace",
            Sections = [new SongSection { SectionType = "verse", Text = "Amazing grace how sweet the sound" }],
        };

        public Task<Song> ImportSongAsync(string title, string rawLyrics, string? artist = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<Song> SaveSongAsync(Song song, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task DeleteSongAsync(long songId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
