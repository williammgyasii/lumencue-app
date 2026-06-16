using System.Collections.ObjectModel;
using System.Reactive;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Models.Slides;
using ChurchProjection.Core.Services;
using ChurchProjection.UI.Services;
using ReactiveUI;
using Serilog;

namespace ChurchProjection.UI.ViewModels.Operator;

public class ServiceQueueViewModel : ViewModelBase
{
    private readonly IProjectionService _projection;
    private readonly IThemeService _themes;
    private int _currentIndex = -1;
    private QueueSlide? _selectedItem;

    public ObservableCollection<QueueSlide> Items { get; } = [];

    public int CurrentIndex
    {
        get => _currentIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentIndex, value);
            this.RaisePropertyChanged(nameof(PositionText));
            this.RaisePropertyChanged(nameof(HasItems));

            if (value >= 0 && value < Items.Count)
                SelectedItem = Items[value];
        }
    }

    public QueueSlide? SelectedItem
    {
        get => _selectedItem;
        set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
    }

    public string PositionText => Items.Count == 0
        ? "Empty"
        : $"{CurrentIndex + 1} / {Items.Count}";

    public bool HasItems => Items.Count > 0;

    public ReactiveCommand<Unit, Unit> NextCommand { get; }
    public ReactiveCommand<Unit, Unit> PrevCommand { get; }
    public ReactiveCommand<Unit, Unit> GoLiveCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearCommand { get; }

    public ServiceQueueViewModel(IProjectionService projection, IThemeService themes)
    {
        _projection = projection;
        _themes = themes;

        NextCommand = ReactiveCommand.Create(GoNext);
        PrevCommand = ReactiveCommand.Create(GoPrev);
        GoLiveCommand = ReactiveCommand.Create(ProjectCurrent);
        ClearCommand = ReactiveCommand.Create(ClearQueue);
    }

    /// <summary>True when there is a next queue item to advance to.</summary>
    public bool CanGoNext => Items.Count > 0 && CurrentIndex < Items.Count - 1;

    /// <summary>True when there is a previous queue item to step back to.</summary>
    public bool CanGoPrev => Items.Count > 0 && CurrentIndex > 0;

    public void AddItem(ContentItem item)
    {
        Items.Add(ToQueueSlide(item));

        this.RaisePropertyChanged(nameof(PositionText));
        this.RaisePropertyChanged(nameof(HasItems));

        if (Items.Count == 1)
            CurrentIndex = 0;

        Log.Information("Added to queue: {Title} ({Count} total)", item.Title, Items.Count);
    }

    /// <summary>Replaces the queue with a saved set of slides (used when loading a playlist).</summary>
    public void LoadSlides(IEnumerable<QueueSlide> slides)
    {
        Items.Clear();
        foreach (var s in slides)
            Items.Add(s);

        this.RaisePropertyChanged(nameof(PositionText));
        this.RaisePropertyChanged(nameof(HasItems));

        CurrentIndex = Items.Count > 0 ? 0 : -1;
        Log.Information("Loaded playlist into queue: {Count} items", Items.Count);
    }

    /// <summary>A snapshot copy of the current queue, suitable for saving as a playlist.</summary>
    public List<QueueSlide> Snapshot() => Items.Select(s => new QueueSlide
    {
        Title = s.Title,
        Body = s.Body,
        Footer = s.Footer,
        Tag = s.Tag,
        Icon = s.Icon,
        SlideType = s.SlideType,
    }).ToList();

    public void AddAllItems(IEnumerable<ContentItem> items)
    {
        var startEmpty = Items.Count == 0;
        var added = 0;
        foreach (var item in items)
        {
            Items.Add(ToQueueSlide(item));
            added++;
        }

        this.RaisePropertyChanged(nameof(PositionText));
        this.RaisePropertyChanged(nameof(HasItems));

        if (startEmpty && Items.Count > 0)
            CurrentIndex = 0;

        Log.Information("Added {Count} items to queue", added);
    }

    private static QueueSlide ToQueueSlide(ContentItem item) => new()
    {
        Title = item.Title,
        Body = item.Body,
        Footer = item.Footer,
        Tag = item.Tag,
        Icon = item.Icon,
        SlideType = item.Type.ToSlideType(),
        LinesPerSlide = item.LinesPerSlide,
    };

    private void GoNext()
    {
        if (Items.Count == 0) return;
        CurrentIndex = Math.Min(CurrentIndex + 1, Items.Count - 1);
        ProjectCurrent();
    }

    private void GoPrev()
    {
        if (Items.Count == 0) return;
        CurrentIndex = Math.Max(CurrentIndex - 1, 0);
        ProjectCurrent();
    }

    private void ProjectCurrent()
    {
        if (CurrentIndex < 0 || CurrentIndex >= Items.Count) return;
        var item = Items[CurrentIndex];
        var theme = _themes.ResolveFor(item.SlideType);
        _projection.ProjectDeck(DeckBuilder.Build(item.SlideType, item.Title, item.Body, item.Footer, theme, item.LinesPerSlide));
    }

    private void ClearQueue()
    {
        Items.Clear();
        CurrentIndex = -1;
        this.RaisePropertyChanged(nameof(PositionText));
        this.RaisePropertyChanged(nameof(HasItems));
    }
}
