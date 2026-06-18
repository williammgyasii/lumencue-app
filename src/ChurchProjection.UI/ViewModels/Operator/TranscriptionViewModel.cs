using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using ReactiveUI;
using Serilog;

namespace ChurchProjection.UI.ViewModels.Operator;

public class TranscriptionViewModel : ViewModelBase
{
    private const int MaxSuggestions = 20;
    private static readonly TimeSpan WindowDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AudioLevelThrottle = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan SegmentThrottle = TimeSpan.FromMilliseconds(500);
    private const float SignalThreshold = 0.005f;
    private const double AudioLevelGain = 300;

    private readonly ITranscriptionService _transcription;
    private readonly ISuggestionEngine _engine;

    private readonly List<(string Text, DateTimeOffset Time)> _slidingWindow = [];

    private string _transcript = "";
    private bool _isListening;
    private string _statusText = "Idle";
    private float _audioLevel;
    private string _engineName = "";
    private string? _selectedDevice;
    private SuggestionItem? _selectedSuggestion;

    public string Transcript
    {
        get => _transcript;
        set
        {
            this.RaiseAndSetIfChanged(ref _transcript, value);
            this.RaisePropertyChanged(nameof(LastHeard));
            this.RaisePropertyChanged(nameof(RecentTranscript));
        }
    }

    /// <summary>The tail end of the rolling transcript, for the compact listening bar.</summary>
    public string LastHeard
    {
        get
        {
            var t = (_transcript ?? string.Empty).Replace('\n', ' ').Trim();
            const int max = 160;
            return t.Length <= max ? t : "…" + t[^max..];
        }
    }

    /// <summary>A longer tail of the rolling transcript for the expanded live panel.
    /// The newest words are always at the end so the panel reads in real time.</summary>
    public string RecentTranscript
    {
        get
        {
            var t = (_transcript ?? string.Empty).Replace('\n', ' ').Trim();
            const int max = 700;
            return t.Length == 0
                ? "Listening for speech…"
                : t.Length <= max ? t : "…" + t[^max..];
        }
    }

    private bool _showTranscript = true;
    /// <summary>Whether the expanded live transcript panel is shown above the mic bar.</summary>
    public bool ShowTranscript
    {
        get => _showTranscript;
        set => this.RaiseAndSetIfChanged(ref _showTranscript, value);
    }

    public bool IsListening
    {
        get => _isListening;
        set
        {
            this.RaiseAndSetIfChanged(ref _isListening, value);
            this.RaisePropertyChanged(nameof(ShowEngineName));
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public float AudioLevel
    {
        get => _audioLevel;
        set
        {
            this.RaiseAndSetIfChanged(ref _audioLevel, value);
            this.RaisePropertyChanged(nameof(AudioLevelPercent));
            this.RaisePropertyChanged(nameof(AudioLevelDisplay));
            this.RaisePropertyChanged(nameof(HasSignal));
        }
    }

    public double AudioLevelPercent => Math.Min(AudioLevel * AudioLevelGain, 100);
    public string AudioLevelDisplay => $"{AudioLevelPercent:F0}%";
    public bool HasSignal => AudioLevel > SignalThreshold;

    /// <summary>Name of the active speech engine, shown only while listening.</summary>
    public string EngineName
    {
        get => _engineName;
        set
        {
            this.RaiseAndSetIfChanged(ref _engineName, value);
            this.RaisePropertyChanged(nameof(ShowEngineName));
        }
    }

    public bool ShowEngineName => _isListening && !string.IsNullOrWhiteSpace(_engineName);

    public string? SelectedDevice
    {
        get => _selectedDevice;
        set => this.RaiseAndSetIfChanged(ref _selectedDevice, value);
    }

    public SuggestionItem? SelectedSuggestion
    {
        get => _selectedSuggestion;
        set => this.RaiseAndSetIfChanged(ref _selectedSuggestion, value);
    }

    public ObservableCollection<string> AudioDevices { get; } = [];
    public ObservableCollection<SuggestionItem> Suggestions { get; } = [];

    /// <summary>Verses the operator pinned to revisit later in the service.</summary>
    public ObservableCollection<SuggestionItem> Bookmarks { get; } = [];

    private bool _hasBookmarks;
    public bool HasBookmarks
    {
        get => _hasBookmarks;
        private set => this.RaiseAndSetIfChanged(ref _hasBookmarks, value);
    }

    /// <summary>Pins (or unpins) a suggestion so it stays available even as live suggestions churn.</summary>
    public void ToggleBookmark(SuggestionItem? item)
    {
        if (item is null) return;

        var existing = Bookmarks.FirstOrDefault(b => b.ContentId == item.ContentId);
        if (existing is not null)
        {
            RemoveBookmark(existing);
            return;
        }

        // Store an independent copy so trimming/hydration of the live list never mutates a pin.
        Bookmarks.Insert(0, new SuggestionItem
        {
            ContentId = item.ContentId,
            Title = item.Title,
            Body = item.Body,
            Footer = item.Footer,
            Score = item.Score,
            MatchType = item.MatchType,
            IsBookmarked = true,
        });

        SyncBookmarkFlag(item.ContentId, true);
        HasBookmarks = Bookmarks.Count > 0;
    }

    /// <summary>Pins a verse without toggling — used when bookmarking from the scripture list, where
    /// the source item doesn't track its own pinned state. A duplicate id is ignored.</summary>
    public void AddBookmark(SuggestionItem? item)
    {
        if (item is null) return;
        if (Bookmarks.Any(b => b.ContentId == item.ContentId))
        {
            SyncBookmarkFlag(item.ContentId, true);
            return;
        }

        Bookmarks.Insert(0, item);
        SyncBookmarkFlag(item.ContentId, true);
        HasBookmarks = Bookmarks.Count > 0;
    }

    public void RemoveBookmark(SuggestionItem? item)
    {
        if (item is null) return;

        var existing = Bookmarks.FirstOrDefault(b => b.ContentId == item.ContentId);
        if (existing is not null)
            Bookmarks.Remove(existing);

        SyncBookmarkFlag(item.ContentId, false);
        HasBookmarks = Bookmarks.Count > 0;
    }

    // Keep any live suggestion sharing this id in sync so the star reflects the pinned state.
    private void SyncBookmarkFlag(string contentId, bool bookmarked)
    {
        foreach (var s in Suggestions.Where(s => s.ContentId == contentId))
            s.IsBookmarked = bookmarked;
    }

    public ReactiveCommand<Unit, Unit> ToggleListeningCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleTranscriptCommand { get; }

    public TranscriptionViewModel(
        ITranscriptionService transcription,
        ISuggestionEngine engine,
        IProjectionService projection)
    {
        _transcription = transcription;
        _engine = engine;

        ToggleListeningCommand = ReactiveCommand.CreateFromTask(ToggleListening);
        ToggleTranscriptCommand = ReactiveCommand.Create(() => { ShowTranscript = !ShowTranscript; });

        _transcription.RollingTranscript
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(t => Transcript = t);

        _transcription.IsListening
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(b => IsListening = b);

        _transcription.StatusMessage
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(s => StatusText = s);

        _transcription.AudioLevel
            .Throttle(AudioLevelThrottle)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(l => AudioLevel = l);

        _transcription.EngineName
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(n => EngineName = n);

        // Feed transcript windows to the background engine; matching never runs on this thread.
        _transcription.Segments
            .Throttle(SegmentThrottle)
            .Subscribe(PushSegment);

        // Handle each final utterance once for spoken commands ("next verse") and to keep the
        // navigation anchor current. Un-throttled so no command utterance is collapsed away.
        _transcription.Segments
            .Subscribe(s => _engine.HandleSegment(s.Text));

        // Results arrive off-thread; marshal only the final UI update.
        _engine.Suggestions
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(MergeSuggestions);
    }

    public async Task InitializeAsync()
    {
        var devices = await _transcription.GetAvailableDevicesAsync();
        AudioDevices.Clear();
        foreach (var d in devices)
            AudioDevices.Add(d);
        if (devices.Count > 0)
            SelectedDevice = devices[0];
    }

    private async Task ToggleListening()
    {
        try
        {
            if (_transcription.IsRunning)
                await _transcription.StopAsync();
            else
                await _transcription.StartAsync(SelectedDevice);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Transcription toggle failed");
            StatusText = $"Error: {ex.Message}";
        }
    }

    private void PushSegment(TranscriptionSegment segment)
    {
        if (string.IsNullOrWhiteSpace(segment.Text)) return;

        var now = DateTimeOffset.UtcNow;
        _slidingWindow.Add((segment.Text, now));
        _slidingWindow.RemoveAll(e => now - e.Time > WindowDuration);

        _engine.Push(string.Join(" ", _slidingWindow.Select(e => e.Text)));
    }

    private void MergeSuggestions(IReadOnlyList<AiSuggestion> matches)
    {
        foreach (var match in matches)
        {
            var existing = Suggestions.FirstOrDefault(s => s.ContentId == match.ContentId);
            if (existing is not null)
            {
                // Reflect hydrated scripture text / improved scores in place.
                existing.Title = match.Title;
                existing.Body = match.Body;
                existing.Footer = match.Footer;
                existing.Score = match.Score;
                existing.MatchType = match.MatchType;
                continue;
            }

            // Newest suggestions go to the top so the operator never scrolls to find them.
            Suggestions.Insert(0, new SuggestionItem
            {
                ContentId = match.ContentId,
                Title = match.Title,
                Body = match.Body,
                Footer = match.Footer,
                Score = match.Score,
                MatchType = match.MatchType,
                IsBookmarked = Bookmarks.Any(b => b.ContentId == match.ContentId),
            });

            // Trim the oldest (bottom) entries when over the cap.
            while (Suggestions.Count > MaxSuggestions)
                Suggestions.RemoveAt(Suggestions.Count - 1);
        }
    }
}
