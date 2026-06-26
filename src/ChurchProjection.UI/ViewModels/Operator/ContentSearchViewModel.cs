using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Parsing;
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

    private readonly Subject<string> _invalidReference = new();

    /// <summary>Emits a ready-to-show message when a typed query parses as a real reference but returns
    /// no scripture (e.g. "Genesis 99"). The operator shell turns it into a transient toast. Plain
    /// keyword searches that simply find nothing do not fire this.</summary>
    public IObservable<string> InvalidReference => _invalidReference;

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
                // Title is just the song name; the section (Verse 5 / Chorus …) shows via the Tag badge.
                Title = song.Title,
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
    /// <param name="originVerse">
    /// The verse the operator opened this chapter from (e.g. an AI suggestion of Mark 1:1). That row
    /// is highlighted and scrolled into view so the operator can see the verse that brought them here.
    /// Pass 0 to highlight nothing.
    /// </param>
    public async Task LoadFullChapterAsync(string book, int chapter, int originVerse = 0)
    {
        // Cancel any in-flight search so it cannot clobber the chapter we are about to show.
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        try
        {
            var wholeChapter = new ScriptureReference(book, chapter, VerseStart: 1, VerseEnd: ScriptureReference.WholeChapterSentinel);
            var verses = await _contentLibrary.GetOrFetchVersesAsync(wholeChapter, SelectedTranslation);

            // Preloading a chapter that doesn't exist (e.g. auto-loaded from a mis-heard reference)
            // resolves to nothing — warn rather than silently showing an empty list.
            if (verses.Count == 0)
            {
                var notice = InvalidReferenceNotice.For($"{book} {chapter}", hadResults: false);
                if (notice is not null)
                    _invalidReference.OnNext(notice);
            }

            Results.Clear();
            ContentItem? origin = null;
            foreach (var s in verses)
            {
                var item = new ContentItem
                {
                    Type = ContentItemType.Scripture,
                    Title = s.Reference,
                    Subtitle = s.Translation,
                    Body = s.Text,
                    Tag = s.Translation,
                    Footer = $"{s.Reference} ({s.Translation})",
                    Source = s,
                    IsOrigin = originVerse > 0 && s.VerseStart == originVerse
                };
                Results.Add(item);
                if (item.IsOrigin) origin = item;
            }

            // Select the originating verse so the list auto-scrolls to it and it reads as the focus.
            if (origin is not null)
                SelectedItem = origin;

            _suppressedQuery = $"{book} {chapter}";
            SearchQuery = _suppressedQuery;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load full chapter {Book} {Chapter}", book, chapter);
        }
    }

    /// <summary>Wipes the results list (and search box) without kicking off a fresh "load all" search.
    /// Used by the operator's Clear button when a service has piled up a lot of looked-up verses.</summary>
    public void ClearResults()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        Results.Clear();
        SelectedItem = null;

        // Suppress the resulting empty-query search so we just clear rather than reload everything.
        _suppressedQuery = string.Empty;
        SearchQuery = string.Empty;
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

            // A query that reads as a concrete reference but matched no scripture is a non-existent
            // verse/chapter (e.g. "John 99"); a plain keyword search with no hits never trips this.
            var notice = InvalidReferenceNotice.For(query, scriptures.Count > 0);
            if (notice is not null)
                _invalidReference.OnNext(notice);

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
