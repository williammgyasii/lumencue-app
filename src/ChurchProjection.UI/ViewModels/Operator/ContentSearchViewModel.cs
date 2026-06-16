using System.Collections.ObjectModel;
using System.Reactive.Linq;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using ReactiveUI;
using Serilog;

namespace ChurchProjection.UI.ViewModels.Operator;

public class ContentSearchViewModel : ViewModelBase
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan TranslationDebounce = TimeSpan.FromMilliseconds(200);

    private readonly IContentLibraryService _contentLibrary;

    private string _searchQuery = string.Empty;
    private ContentItem? _selectedItem;
    private string _selectedTranslation = "BSB";
    private CancellationTokenSource? _searchCts;

    // When a full chapter is loaded we set the search box for context; this records that query so
    // the debounced search subscription skips it once instead of clobbering the chapter verses.
    private string? _suppressedQuery;

    public string SearchQuery
    {
        get => _searchQuery;
        set => this.RaiseAndSetIfChanged(ref _searchQuery, value);
    }

    public ContentItem? SelectedItem
    {
        get => _selectedItem;
        set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
    }

    public string SelectedTranslation
    {
        get => _selectedTranslation;
        set => this.RaiseAndSetIfChanged(ref _selectedTranslation, value);
    }

    public ObservableCollection<string> AvailableTranslations { get; } =
        ["BSB", "KJV", "NIV", "NKJV", "NLT", "ASV", "LSV", "WEB", "FBV", "DRA", "GNV", "RV", "T4T"];

    public ObservableCollection<ContentItem> Results { get; } = [];

    public ContentSearchViewModel(IContentLibraryService contentLibrary)
    {
        _contentLibrary = contentLibrary;

        this.WhenAnyValue<ContentSearchViewModel, string>(x => x.SearchQuery)
            .Throttle(SearchDebounce)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(query => QueueSearch(query));

        this.WhenAnyValue<ContentSearchViewModel, string>(x => x.SelectedTranslation)
            .Skip(1)
            .Throttle(TranslationDebounce)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => QueueSearch(SearchQuery));
    }

    private void QueueSearch(string query)
    {
        if (_suppressedQuery is not null && query == _suppressedQuery)
        {
            _suppressedQuery = null;
            return;
        }
        _ = RunSearchAsync(query);
    }

    public async Task LoadAllContentAsync()
    {
        Results.Clear();
        var songs = await _contentLibrary.GetAllSongsAsync();
        foreach (var song in songs)
            AddSongSections(song);
    }

    /// <summary>
    /// Resets the shared content list when the workspace mode changes so results don't bleed across
    /// modes (e.g. a scripture search in Bible mode appearing in Songs mode). Songs mode shows the
    /// song library; Bible mode starts empty until a verse is looked up.
    /// </summary>
    public async Task ResetForModeAsync(bool songsMode)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        // Suppress the debounced search that the cleared query would otherwise trigger.
        _suppressedQuery = string.Empty;
        SearchQuery = string.Empty;
        SelectedItem = null;

        if (songsMode)
            await LoadAllContentAsync();
        else
            Results.Clear();
    }

    private void AddSongSections(Song song)
    {
        foreach (var section in song.Sections)
        {
            Results.Add(new ContentItem
            {
                Type = ContentItemType.Song,
                Title = $"{song.Title} — {section.Label}",
                Subtitle = song.Artist ?? "",
                Body = section.Text,
                Tag = section.Label,
                Footer = song.Title,
                LinesPerSlide = song.LinesPerSlide,
                Source = song
            });
        }
    }

    /// <summary>
    /// Loads every verse of a chapter into the results list (one selectable row per verse) so the
    /// operator can browse and project verses manually, independent of the live AI suggestions.
    /// </summary>
    public async Task LoadFullChapterAsync(string book, int chapter)
    {
        // Cancel any in-flight search so it cannot clobber the chapter we are about to show.
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        try
        {
            var wholeChapter = new ScriptureReference(book, chapter, VerseStart: 1, VerseEnd: ScriptureReference.WholeChapterSentinel);
            var verses = await _contentLibrary.GetOrFetchVersesAsync(wholeChapter, SelectedTranslation);

            Results.Clear();
            foreach (var s in verses)
            {
                Results.Add(new ContentItem
                {
                    Type = ContentItemType.Scripture,
                    Title = s.Reference,
                    Subtitle = s.Translation,
                    Body = s.Text,
                    Tag = s.Translation,
                    Footer = $"{s.Reference} ({s.Translation})",
                    Source = s
                });
            }

            _suppressedQuery = $"{book} {chapter}";
            SearchQuery = _suppressedQuery;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load full chapter {Book} {Chapter}", book, chapter);
        }
    }

    private async Task RunSearchAsync(string query)
    {
        // Latest-wins: cancel any search still running for a previous query.
        _searchCts?.Cancel();
        _searchCts?.Dispose();

        if (string.IsNullOrWhiteSpace(query))
        {
            _searchCts = null;
            await LoadAllContentAsync();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var token = cts.Token;

        try
        {
            Log.Information("Searching for: {Query} (translation: {Translation})", query, SelectedTranslation);

            var scriptures = await _contentLibrary.SearchScripturesAsync(query, SelectedTranslation, token);
            var songs = await _contentLibrary.SearchSongsAsync(query, token);
            if (token.IsCancellationRequested) return;

            Results.Clear();

            foreach (var s in scriptures)
            {
                Results.Add(new ContentItem
                {
                    Type = ContentItemType.Scripture,
                    Title = s.Reference,
                    Subtitle = s.Translation,
                    Body = s.Text,
                    Tag = s.Translation,
                    Footer = $"{s.Reference} ({s.Translation})",
                    Source = s
                });
            }

            foreach (var song in songs)
                AddSongSections(song);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Search failed for query: {Query}", query);
        }
    }
}
