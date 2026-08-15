using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ChurchProjection.Core.Bible;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Parsing;
using ChurchProjection.Core.Services;
using ReactiveUI;
using Serilog;

namespace ChurchProjection.UI.ViewModels.Operator;

public class ContentSearchViewModel : ViewModelBase
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(400);

    private readonly IContentLibraryService _contentLibrary;

    private string _searchQuery = string.Empty;
    private ContentItem? _selectedItem;
    private string _selectedTranslation = "BSB";
    private CancellationTokenSource? _searchCts;

    // When a full chapter is loaded we set the search box for context; this records that query so
    // the debounced search subscription skips it once instead of clobbering the chapter verses.
    private string? _suppressedQuery;

    // When a full chapter/book is on the verse grid, remember it so a translation switch
    // reloads those cards instead of running a text search that wipes them.
    private string? _browseBook;
    private int? _browseChapter;
    private int _browseOriginChapter;
    private int _browseOriginVerse;

    // The scripture grid is shared with song-library loads. Remember the workspace mode so a
    // Bible search (or a stale empty query after switching back) cannot append song cards.
    private bool _songsMode;

    private ContentItem? _rangeAnchor;
    private bool _hasRangeSelection;

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

    /// <summary>True when Shift+click has painted two or more cards as a range.</summary>
    public bool HasRangeSelection
    {
        get => _hasRangeSelection;
        private set => this.RaiseAndSetIfChanged(ref _hasRangeSelection, value);
    }

    public IReadOnlyList<ContentItem> SelectedRangeItems()
        => Results.Where(r => r.IsRangeSelected).ToList();

    /// <summary>Plain click: remember this card as the range start and clear any previous span.</summary>
    public void SetRangeAnchor(ContentItem item)
    {
        ClearRangeFlags();
        _rangeAnchor = item;
        HasRangeSelection = false;
    }

    /// <summary>Shift+click: paint the inclusive span from the anchor to this card.</summary>
    public void ExtendRangeTo(ContentItem item)
    {
        if (_rangeAnchor is null)
        {
            SetRangeAnchor(item);
            return;
        }

        var from = Results.IndexOf(_rangeAnchor);
        var to = Results.IndexOf(item);
        if (from < 0 || to < 0)
        {
            SetRangeAnchor(item);
            return;
        }

        SlideRange.Apply(Results, from, to);
        var (start, end) = SlideRange.Inclusive(from, to);
        HasRangeSelection = end > start;
    }

    public void ClearRange()
    {
        ClearRangeFlags();
        _rangeAnchor = null;
        HasRangeSelection = false;
    }

    private void ClearRangeFlags()
    {
        foreach (var item in Results)
            item.IsRangeSelected = false;
    }

    private void ResetResults()
    {
        ClearRange();
        Results.Clear();
    }

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
    }

    /// <summary>
    /// Reloads the open chapter/book in the new translation so verse cards stay on screen.
    /// If a verse is live but the grid is not in chapter-browse mode, that live verse's chapter
    /// is loaded instead of re-running a text search that would wipe the cards.
    /// A plain search is only re-run when neither browse nor a live verse is available.
    /// </summary>
    public Task HandleTranslationChangeAsync(int originVerse = 0, ScriptureReference? liveRef = null)
    {
        if (originVerse > 0)
            _browseOriginVerse = originVerse;
        else if (liveRef is not null)
            _browseOriginVerse = liveRef.VerseStart;
        else if (SelectedItem?.Source is ScripturePassage p)
            _browseOriginVerse = p.VerseStart;

        if (!string.IsNullOrEmpty(_browseBook) && _browseChapter is > 0)
            return LoadFullChapterAsync(_browseBook, _browseChapter.Value, _browseOriginVerse);

        if (!string.IsNullOrEmpty(_browseBook))
            return LoadFullBookAsync(_browseBook, _browseOriginChapter, _browseOriginVerse);

        if (liveRef is not null)
            return LoadFullChapterAsync(liveRef.Book, liveRef.Chapter, liveRef.VerseStart);

        return RunSearchAsync(SearchQuery);
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
        ClearBrowse();
        ResetResults();
        var songs = await _contentLibrary.GetAllSongsAsync();
        foreach (var song in songs)
            AddSongSections(song);
    }

    /// <summary>
    /// Resets the shared content list when the workspace mode changes so results don't bleed across
    /// modes (e.g. a scripture search in Bible mode appearing in Songs mode). Songs mode shows the
    /// song library; Bible mode opens on Genesis 1 so the verse grid is never empty.
    /// </summary>
    public async Task ResetForModeAsync(bool songsMode)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        _songsMode = songsMode;

        // Suppress the debounced search that the cleared query would otherwise trigger.
        _suppressedQuery = string.Empty;
        SearchQuery = string.Empty;
        SelectedItem = null;
        ClearBrowse();

        if (songsMode)
            await LoadAllContentAsync();
        else
            await LoadFullChapterAsync("Genesis", 1);
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
    /// <summary>
    /// Stages a heard scripture reference on the verse grid without going live. Reloads the chapter
    /// when the operator is on a different passage; if that chapter is already showing, only the
    /// origin highlight and selection move so the grid does not flicker.
    /// </summary>
    public Task StageReferenceAsync(string book, int chapter, int originVerse)
    {
        if (!string.IsNullOrEmpty(_browseBook)
            && string.Equals(_browseBook, book, StringComparison.OrdinalIgnoreCase)
            && _browseChapter == chapter
            && Results.Count > 0)
        {
            FocusOriginVerse(originVerse);
            return Task.CompletedTask;
        }

        return LoadFullChapterAsync(book, chapter, originVerse);
    }

    private void FocusOriginVerse(int originVerse)
    {
        ClearRange();
        ContentItem? origin = null;
        foreach (var item in Results)
        {
            var isOrigin = originVerse > 0
                && item.Source is ScripturePassage p
                && p.VerseStart == originVerse;
            item.IsOrigin = isOrigin;
            if (isOrigin) origin = item;
        }

        if (origin is not null)
            SelectedItem = origin;
        _browseOriginVerse = originVerse;
    }

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
                // A translation switch that finds nothing must not wipe cards already on screen.
                if (Results.Count > 0)
                    return;
            }

            ResetResults();
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
            _browseBook = book;
            _browseChapter = chapter;
            _browseOriginChapter = chapter;
            _browseOriginVerse = originVerse;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load full chapter {Book} {Chapter}", book, chapter);
        }
    }

    /// <summary>
    /// Loads every verse of a book into the results list so the operator can browse the whole book.
    /// The bookmarked verse (when provided) is highlighted and scrolled into view.
    /// </summary>
    public async Task LoadFullBookAsync(string book, int originChapter = 0, int originVerse = 0)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        var chapterCount = BibleBooks.ChapterCountFor(book);
        if (chapterCount <= 0)
        {
            _invalidReference.OnNext($"Could not load {book} — book not recognized.");
            return;
        }

        try
        {
            ResetResults();
            ContentItem? origin = null;

            for (var chapter = 1; chapter <= chapterCount; chapter++)
            {
                var wholeChapter = new ScriptureReference(book, chapter, VerseStart: 1, VerseEnd: ScriptureReference.WholeChapterSentinel);
                var verses = await _contentLibrary.GetOrFetchVersesAsync(wholeChapter, SelectedTranslation);
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
                        IsOrigin = originChapter == chapter && originVerse > 0 && s.VerseStart == originVerse,
                    };
                    Results.Add(item);
                    if (item.IsOrigin) origin = item;
                }
            }

            if (Results.Count == 0)
            {
                var notice = InvalidReferenceNotice.For(book, hadResults: false);
                if (notice is not null)
                    _invalidReference.OnNext(notice);
            }

            if (origin is not null)
                SelectedItem = origin;

            _suppressedQuery = book;
            SearchQuery = book;
            _browseBook = book;
            _browseChapter = null;
            _browseOriginChapter = originChapter;
            _browseOriginVerse = originVerse;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load full book {Book}", book);
        }
    }

    /// <summary>Wipes the results list (and search box) without kicking off a fresh "load all" search.
    /// Used by the operator's Clear button when a service has piled up a lot of looked-up verses.</summary>
    public void ClearResults()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        ResetResults();
        SelectedItem = null;
        ClearBrowse();

        // Suppress the resulting empty-query search so we just clear rather than reload everything.
        _suppressedQuery = string.Empty;
        SearchQuery = string.Empty;
    }

    private void ClearBrowse()
    {
        _browseBook = null;
        _browseChapter = null;
        _browseOriginChapter = 0;
        _browseOriginVerse = 0;
    }

    private async Task RunSearchAsync(string query)
    {
        // Latest-wins: cancel any search still running for a previous query.
        _searchCts?.Cancel();
        _searchCts?.Dispose();

        if (string.IsNullOrWhiteSpace(query))
        {
            _searchCts = null;
            // Bible mode's empty box must not dump the song library onto the verse grid.
            // Songs mode still uses this path to show every saved song.
            if (_songsMode)
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
            var songs = _songsMode
                ? await _contentLibrary.SearchSongsAsync(query, token)
                : [];
            if (token.IsCancellationRequested) return;

            // A query that reads as a concrete reference but matched no scripture is a non-existent
            // verse/chapter (e.g. "John 99"); a plain keyword search with no hits never trips this.
            var notice = InvalidReferenceNotice.For(query, scriptures.Count > 0);
            if (notice is not null)
                _invalidReference.OnNext(notice);

            ResetResults();

            ClearBrowse();

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
