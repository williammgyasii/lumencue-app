using System.Collections.ObjectModel;
using System.Reactive.Linq;
using ChurchProjection.Core.Services;
using ReactiveUI;
using Serilog;

namespace ChurchProjection.UI.ViewModels.Operator;

/// <summary>
/// Backs the "Songs" tab: smart, real-time lyric search. Typing a remembered line surfaces the
/// matching songs (typo-tolerant, word-order independent), each showing the line that matched.
/// </summary>
public class SongSearchViewModel : ViewModelBase
{
    // Tight debounce so it feels live, but still coalesces fast typing.
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(120);
    private const int MaxResults = 40;

    private readonly ISongSearchService _search;
    private CancellationTokenSource? _searchCts;

    private string _query = string.Empty;
    private string _statusText = "Type any lyric — the song will appear.";
    private bool _isSearching;
    private SongHitViewModel? _selectedHit;
    private bool _hasResults;

    public string Query
    {
        get => _query;
        set => this.RaiseAndSetIfChanged(ref _query, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set => this.RaiseAndSetIfChanged(ref _isSearching, value);
    }

    public SongHitViewModel? SelectedHit
    {
        get => _selectedHit;
        set => this.RaiseAndSetIfChanged(ref _selectedHit, value);
    }

    public bool HasResults
    {
        get => _hasResults;
        private set => this.RaiseAndSetIfChanged(ref _hasResults, value);
    }

    public ObservableCollection<SongHitViewModel> Results { get; } = [];

    public SongSearchViewModel(ISongSearchService search)
    {
        _search = search;

        this.WhenAnyValue<SongSearchViewModel, string>(x => x.Query)
            .Throttle(SearchDebounce)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(q => _ = RunSearchAsync(q));
    }

    /// <summary>Re-runs the current query (e.g. when the tab is opened) to pick up new/edited songs.</summary>
    public Task RefreshAsync() => RunSearchAsync(Query);

    private async Task RunSearchAsync(string query)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var token = cts.Token;

        try
        {
            IsSearching = true;
            var hits = await _search.SearchAsync(query, MaxResults, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            Results.Clear();
            foreach (var hit in hits)
                Results.Add(new SongHitViewModel(hit));

            HasResults = Results.Count > 0;
            StatusText = string.IsNullOrWhiteSpace(query)
                ? (Results.Count > 0 ? $"{Results.Count} song{(Results.Count == 1 ? "" : "s")} in your library" : "No songs saved yet.")
                : Results.Count > 0
                    ? $"{Results.Count} match{(Results.Count == 1 ? "" : "es")} for \"{query.Trim()}\""
                    : $"No songs match \"{query.Trim()}\".";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Song search failed for: {Query}", query);
            StatusText = "Search failed.";
        }
        finally
        {
            if (_searchCts == cts) IsSearching = false;
        }
    }
}
