using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Models.Slides;
using ChurchProjection.Core.Services;
using ChurchProjection.Infrastructure.Services;
using ChurchProjection.UI.Services;
using ReactiveUI;

namespace ChurchProjection.UI.ViewModels.Operator;

/// <summary>Display row for the split-mode picker.</summary>
public sealed record NoteSplitModeChoice(NoteSplitMode Mode, string Label);

/// <summary>One projected slide row in the note editor.</summary>
public sealed class NoteSlidePreviewVm : ReactiveObject
{
    public NoteSlidePreviewVm(int index, string body)
    {
        Index = index;
        Body = body;
        Label = $"Slide {index + 1}";
        Preview = body.Replace("\r", "").Replace('\n', ' ');
        if (Preview.Length > 120)
            Preview = Preview[..117] + "...";
    }

    public int Index { get; }
    public string Label { get; }
    public string Body { get; }
    public string Preview { get; }
}

/// <summary>
/// Song-editor-style note composer: paste long-form text, pick a split mode, preview slides, save.
/// </summary>
public sealed class NoteEditorViewModel : ViewModelBase, IDisposable
{
    private readonly IThemeService _themes;
    private readonly List<string> _previewBodies = [];
    private int _previewIndex;
    private IDisposable? _refreshSub;

    public NoteEditorViewModel(IThemeService themes, string heading, string title, string body, NoteSplitMode splitMode)
    {
        _themes = themes;
        Heading = heading;
        _noteTitle = title ?? "";
        _body = body ?? "";
        _splitMode = splitMode;

        Preview = new ProjectorViewModel(new ProjectionService(), themes);
        Preview.PreviewTheme(_themes.ResolveFor(SlideType.Note));

        Slides = [];
        SplitModeChoices =
        [
            new(NoteSplitMode.OneParagraphPerSlide, "One slide per paragraph"),
            new(NoteSplitMode.AutoFit, "Auto-fit to theme"),
            new(NoteSplitMode.BySection, "By section, then paragraph"),
        ];
        _selectedSplitModeChoice = SplitModeChoices.First(c => c.Mode == splitMode);

        SaveCommand = ReactiveCommand.Create(() =>
        {
            Confirmed = true;
            CloseRequested?.Invoke();
        });
        CancelCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke());

        RefreshSlidesCommand = ReactiveCommand.Create(RefreshSlides);

        PreviewPrevCommand = ReactiveCommand.Create(() =>
        {
            if (_previewIndex <= 0) return;
            _previewIndex--;
            RefreshPreview();
        });

        PreviewNextCommand = ReactiveCommand.Create(() =>
        {
            if (_previewIndex >= _previewBodies.Count - 1) return;
            _previewIndex++;
            RefreshPreview();
        });

        _refreshSub = this.WhenAnyValue(x => x.Body, x => x.SplitMode)
            .Throttle(TimeSpan.FromMilliseconds(250))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshSlides());
    }

    public string Heading { get; }
    public ProjectorViewModel Preview { get; }
    public ObservableCollection<NoteSlidePreviewVm> Slides { get; }
    public IReadOnlyList<NoteSplitModeChoice> SplitModeChoices { get; }

    private NoteSplitModeChoice _selectedSplitModeChoice;
    public NoteSplitModeChoice SelectedSplitModeChoice
    {
        get => _selectedSplitModeChoice;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSplitModeChoice, value);
            SplitMode = value.Mode;
        }
    }

    private string _noteTitle;
    public string NoteTitle { get => _noteTitle; set => this.RaiseAndSetIfChanged(ref _noteTitle, value); }

    private string _body;
    public string Body { get => _body; set => this.RaiseAndSetIfChanged(ref _body, value); }

    private NoteSplitMode _splitMode;
    public NoteSplitMode SplitMode
    {
        get => _splitMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _splitMode, value);
            var choice = SplitModeChoices.FirstOrDefault(c => c.Mode == value);
            if (choice is not null && _selectedSplitModeChoice != choice)
            {
                _selectedSplitModeChoice = choice;
                this.RaisePropertyChanged(nameof(SelectedSplitModeChoice));
            }
        }
    }

    private NoteSlidePreviewVm? _selectedSlide;
    public NoteSlidePreviewVm? SelectedSlide
    {
        get => _selectedSlide;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSlide, value);
            if (value is not null)
            {
                _previewIndex = value.Index;
                RefreshPreview();
            }
        }
    }

    private string _slideCountText = "0 slides";
    public string SlideCountText
    {
        get => _slideCountText;
        private set => this.RaiseAndSetIfChanged(ref _slideCountText, value);
    }

    private string _previewPositionText = "";
    public string PreviewPositionText
    {
        get => _previewPositionText;
        private set => this.RaiseAndSetIfChanged(ref _previewPositionText, value);
    }

    private string _splitModeHint = "";
    public string SplitModeHint
    {
        get => _splitModeHint;
        private set => this.RaiseAndSetIfChanged(ref _splitModeHint, value);
    }

    public bool Confirmed { get; private set; }

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshSlidesCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviewPrevCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviewNextCommand { get; }

    public event Action? CloseRequested;

    public void RefreshSlides()
    {
        var theme = _themes.ResolveFor(SlideType.Note);
        _previewBodies.Clear();

        if (SplitMode == NoteSplitMode.AutoFit)
        {
            var deck = DeckBuilder.BuildNote(NoteTitle, Body, string.Empty, theme, SplitMode);
            _previewBodies.AddRange(deck.Slides.Select(s => s.Body));
        }
        else
        {
            _previewBodies.AddRange(NoteSlidePlanner.PlanBodies(Body, SplitMode));
        }

        Slides.Clear();
        for (var i = 0; i < _previewBodies.Count; i++)
            Slides.Add(new NoteSlidePreviewVm(i, _previewBodies[i]));

        SlideCountText = _previewBodies.Count switch
        {
            0 => "No slides yet — paste your note text",
            1 => "1 slide",
            _ => $"{_previewBodies.Count} slides",
        };

        SplitModeHint = SplitMode switch
        {
            NoteSplitMode.OneParagraphPerSlide => "One slide per blank-line paragraph.",
            NoteSplitMode.BySection => "New section on 📖 / ✍️ / 🙏 / ALL CAPS headers, then one slide per paragraph.",
            _ => "Paragraphs are packed to fit your theme.",
        };

        _previewIndex = Math.Clamp(_previewIndex, 0, Math.Max(0, _previewBodies.Count - 1));
        SelectedSlide = Slides.Count > 0 ? Slides[_previewIndex] : null;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_previewBodies.Count == 0)
        {
            Preview.SetSampleContent(NoteTitle, "", "");
            PreviewPositionText = "";
            return;
        }

        var body = _previewBodies[_previewIndex];
        Preview.SetSampleContent(NoteTitle, body, "");
        PreviewPositionText = $"Slide {_previewIndex + 1} of {_previewBodies.Count}";
    }

    public static string SplitModeLabel(NoteSplitMode mode) => mode switch
    {
        NoteSplitMode.OneParagraphPerSlide => "One slide per paragraph",
        NoteSplitMode.BySection => "By section, then paragraph",
        _ => "Auto-fit to theme",
    };

    public void Dispose()
    {
        _refreshSub?.Dispose();
        Preview.Dispose();
    }
}
