using System.Collections.ObjectModel;
using System.Reactive.Linq;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using ReactiveUI;
using Serilog;

namespace ChurchProjection.UI.ViewModels.Operator;

/// <summary>
/// Backs the "Find Scripture" tab: topical/paraphrase lookup driven either by the operator typing a
/// description or by a spoken request the AI detected ("the scripture that talks about ...").
/// </summary>
public class TopicalSearchViewModel : ViewModelBase
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(350);
    private const int MaxResults = 12;
    // The detected lane is a running history of passages the preacher has echoed; cap it so a long
    // sermon doesn't grow it without bound. Newest sits on top.
    private const int MaxDetected = 15;

    private readonly IScriptureSearchService _search;
    private CancellationTokenSource? _searchCts;

    private string _query = string.Empty;
    private string _statusText = "Describe a passage, or let the AI catch a request.";
    private bool _isSearching;
    private ContentItem? _selectedItem;
    private ContentItem? _selectedDetectedItem;
    private bool _hasResults;
    private bool _hasDetected;
    private double _cardWidth = OperatorWorkspaceChrome.ScriptureCardMinWidth;

    public string Translation { get; set; } = "BSB";

    public string Query
    {
        get => _query;
        set => this.RaiseAndSetIfChanged(ref _query, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        set => this.RaiseAndSetIfChanged(ref _isSearching, value);
    }

    public ContentItem? SelectedItem
    {
        get => _selectedItem;
        set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
    }

    public bool HasResults
    {
        get => _hasResults;
        private set => this.RaiseAndSetIfChanged(ref _hasResults, value);
    }

    public ContentItem? SelectedDetectedItem
    {
        get => _selectedDetectedItem;
        set => this.RaiseAndSetIfChanged(ref _selectedDetectedItem, value);
    }

    /// <summary>True once the preacher has paraphrased at least one passage this session.</summary>
    public bool HasDetected
    {
        get => _hasDetected;
        private set => this.RaiseAndSetIfChanged(ref _hasDetected, value);
    }

    public ObservableCollection<ContentItem> Results { get; } = [];

    /// <summary>One third of the Find Scripture list, same formula as the Scripture tab.</summary>
    public double CardWidth
    {
        get => _cardWidth;
        private set => this.RaiseAndSetIfChanged(ref _cardWidth, value);
    }

    public void SetCardPaneWidth(double paneWidth)
    {
        CardWidth = OperatorWorkspaceChrome.ScriptureCardWidth(paneWidth);
        foreach (var item in Results)
            item.CardWidth = CardWidth;
    }

    /// <summary>Verses auto-detected from the preacher paraphrasing scripture. Separate from the
    /// operator's manual <see cref="Results"/> so live detection never clobbers a manual search.</summary>
    public ObservableCollection<ContentItem> DetectedResults { get; } = [];

    public TopicalSearchViewModel(IScriptureSearchService search)
    {
        _search = search;

        this.WhenAnyValue<TopicalSearchViewModel, string>(x => x.Query)
            .Throttle(SearchDebounce)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(q => _ = RunSearchAsync(q, isAuto: false));
    }

    /// <summary>Adds auto-detected paraphrase matches to the detected lane (newest first), skipping any
    /// verse already shown and trimming to the cap. Must be called on the UI thread.</summary>
    public void AddDetections(IReadOnlyList<ParaphraseDetection> detections)
    {
        foreach (var d in detections)
        {
            var p = d.Passage;
            var key = $"{p.Book}|{p.Chapter}|{p.VerseStart}";
            if (DetectedResults.Any(i => i.Source is ScripturePassage sp && $"{sp.Book}|{sp.Chapter}|{sp.VerseStart}" == key))
                continue;

            DetectedResults.Insert(0, new ContentItem
            {
                Type = ContentItemType.Scripture,
                Title = p.Reference,
                Subtitle = p.Translation,
                Body = p.Text,
                Tag = p.Translation,
                Footer = $"{p.Reference} ({p.Translation})",
                Source = p,
            });

            while (DetectedResults.Count > MaxDetected)
                DetectedResults.RemoveAt(DetectedResults.Count - 1);
        }

        HasDetected = DetectedResults.Count > 0;
    }

    /// <summary>Runs a search for a topic the AI detected in speech, surfacing it in the tab.</summary>
    public Task RunAutoSearchAsync(string topic)
    {
        Query = topic;
        return RunSearchAsync(topic, isAuto: true);
    }

    private async Task RunSearchAsync(string query, bool isAuto)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();

        if (string.IsNullOrWhiteSpace(query))
        {
            _searchCts = null;
            Results.Clear();
            HasResults = false;
            StatusText = "Describe a passage, or let the AI catch a request.";
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var token = cts.Token;

        try
        {
            IsSearching = true;
            StatusText = isAuto ? $"Heard a request — finding \"{query}\"..." : "Searching...";

            var hits = await _search.SearchAsync(query, Translation, MaxResults, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            Results.Clear();
            foreach (var hit in hits)
            {
                var p = hit.Passage;
                Results.Add(new ContentItem
                {
                    Type = ContentItemType.Scripture,
                    Title = p.Reference,
                    Subtitle = p.Translation,
                    Body = p.Text,
                    Tag = p.Translation,
                    Footer = $"{p.Reference} ({p.Translation})",
                    Source = p,
                    CardWidth = CardWidth,
                });
            }

            HasResults = Results.Count > 0;
            StatusText = Results.Count > 0
                ? $"{Results.Count} match{(Results.Count == 1 ? "" : "es")} for \"{query}\""
                : $"No matches for \"{query}\".";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Topical search failed for: {Query}", query);
            StatusText = "Search failed.";
        }
        finally
        {
            if (_searchCts == cts) IsSearching = false;
        }
    }
}
