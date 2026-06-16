using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Models.Slides;
using ChurchProjection.Core.Services;
using ChurchProjection.Infrastructure.Parsing;
using ChurchProjection.Infrastructure.Services;
using ChurchProjection.UI.Services;
using ReactiveUI;
using Serilog;

namespace ChurchProjection.UI.ViewModels.Planning;

/// <summary>An editable song section (type + lyric text) used inside the song editor.</summary>
public sealed class SongSectionVm : ReactiveObject
{
    /// <summary>Section types offered in the editor's per-row dropdown.</summary>
    public static readonly IReadOnlyList<string> Types =
        ["verse", "chorus", "pre-chorus", "bridge", "tag", "intro", "outro"];

    private string _sectionType;
    private string _text;

    public SongSectionVm(string sectionType, string text)
    {
        _sectionType = sectionType;
        _text = text;
    }

    public string SectionType { get => _sectionType; set => this.RaiseAndSetIfChanged(ref _sectionType, value); }
    public string Text { get => _text; set => this.RaiseAndSetIfChanged(ref _text, value); }

    /// <summary>Exposed per-row so the item template binds without casting to the parent VM.</summary>
    public IReadOnlyList<string> SectionTypes => Types;
}

/// <summary>
/// Backs the two-pane Songs editor: paste/type lyrics on the left, get an auto-detected, editable
/// breakdown (verses + chorus) with a live themed preview on the right, then save to the library.
/// </summary>
public sealed class SongEditorViewModel : ViewModelBase, IDisposable
{
    private readonly IContentLibraryService _library;
    private readonly IThemeService _themes;

    private long _songId;
    private readonly List<string> _previewPages = [];
    private int _previewIndex;
    private IDisposable? _textSub;

    public ProjectorViewModel Preview { get; }
    public ObservableCollection<SongSectionVm> Sections { get; } = [];
    public IReadOnlyList<string> SectionTypes => SongSectionVm.Types;
    public IReadOnlyList<int> LinesPerSlideOptions { get; } = [0, 1, 2, 3, 4, 5, 6, 8];

    public ReactiveCommand<Unit, Unit> ParseCommand { get; }
    public ReactiveCommand<Unit, Unit> AddSectionCommand { get; }
    public ReactiveCommand<SongSectionVm, Unit> MoveUpCommand { get; }
    public ReactiveCommand<SongSectionVm, Unit> MoveDownCommand { get; }
    public ReactiveCommand<SongSectionVm, Unit> DeleteSectionCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviewPrevCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviewNextCommand { get; }

    public event Action? Saved;

    /// <summary>Raised after a successful save so the host window can close itself.</summary>
    public event Action? CloseRequested;

    public SongEditorViewModel(IContentLibraryService library, IThemeService themes)
    {
        _library = library;
        _themes = themes;

        Preview = new ProjectorViewModel(new ProjectionService(), themes);
        Preview.PreviewTheme(_themes.ResolveFor(SlideType.Lyric));

        ParseCommand = ReactiveCommand.Create(ParseFromLyrics);

        AddSectionCommand = ReactiveCommand.Create(() =>
        {
            var s = new SongSectionVm("verse", "");
            Sections.Add(s);
            SelectedSection = s;
        });

        MoveUpCommand = ReactiveCommand.Create<SongSectionVm>(s => Move(s, -1));
        MoveDownCommand = ReactiveCommand.Create<SongSectionVm>(s => Move(s, +1));

        DeleteSectionCommand = ReactiveCommand.Create<SongSectionVm>(s =>
        {
            var i = Sections.IndexOf(s);
            if (i < 0) return;
            Sections.RemoveAt(i);
            SelectedSection = Sections.Count == 0 ? null : Sections[Math.Min(i, Sections.Count - 1)];
        });

        SaveCommand = ReactiveCommand.CreateFromTask(
            SaveAsync,
            this.WhenAnyValue(x => x.Title, t => !string.IsNullOrWhiteSpace(t)));

        PreviewPrevCommand = ReactiveCommand.Create(() =>
        {
            if (_previewIndex <= 0) return;
            _previewIndex--;
            RefreshPreview();
        });

        PreviewNextCommand = ReactiveCommand.Create(() =>
        {
            if (_previewIndex >= _previewPages.Count - 1) return;
            _previewIndex++;
            RefreshPreview();
        });
    }

    // ───────────────────────── Editable fields ─────────────────────────

    private string _title = "";
    public string Title
    {
        get => _title;
        set
        {
            this.RaiseAndSetIfChanged(ref _title, value);
            this.RaisePropertyChanged(nameof(WindowTitle));
        }
    }

    private string _artist = "";
    public string Artist { get => _artist; set => this.RaiseAndSetIfChanged(ref _artist, value); }

    private string _rawLyrics = "";
    public string RawLyrics { get => _rawLyrics; set => this.RaiseAndSetIfChanged(ref _rawLyrics, value); }

    private int _linesPerSlide;
    public int LinesPerSlide
    {
        get => _linesPerSlide;
        set
        {
            this.RaiseAndSetIfChanged(ref _linesPerSlide, value);
            RebuildPreviewPages();
        }
    }

    private SongSectionVm? _selectedSection;
    public SongSectionVm? SelectedSection
    {
        get => _selectedSection;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSection, value);
            _textSub?.Dispose();
            _previewIndex = 0;
            _textSub = value?.WhenAnyValue(s => s.Text).Skip(1).Subscribe(_ => RebuildPreviewPages());
            RebuildPreviewPages();
        }
    }

    private string _slideCountText = "";
    public string SlideCountText { get => _slideCountText; set => this.RaiseAndSetIfChanged(ref _slideCountText, value); }

    public string PreviewPositionText => _previewPages.Count == 0 ? "" : $"{_previewIndex + 1} / {_previewPages.Count}";

    public string WindowTitle => _songId == 0 ? "New Song" : $"Edit — {Title}";

    // ───────────────────────── Behavior ─────────────────────────

    private void ParseFromLyrics()
    {
        var parsed = SongImportParser.ParseSections(RawLyrics);
        Sections.Clear();
        foreach (var s in parsed)
            Sections.Add(new SongSectionVm(s.SectionType, s.Text));
        SelectedSection = Sections.Count > 0 ? Sections[0] : null;
    }

    private void Move(SongSectionVm s, int direction)
    {
        var i = Sections.IndexOf(s);
        var j = i + direction;
        if (i < 0 || j < 0 || j >= Sections.Count) return;
        Sections.Move(i, j);
        SelectedSection = s;
    }

    private void RebuildPreviewPages()
    {
        _previewPages.Clear();
        var section = SelectedSection;
        if (section is not null)
        {
            var theme = _themes.ResolveFor(SlideType.Lyric);
            Preview.PreviewTheme(theme);
            var deck = DeckBuilder.Build(SlideType.Lyric, Title, section.Text, "", theme, LinesPerSlide);
            foreach (var slide in deck.Slides)
                _previewPages.Add(slide.Body);
        }

        if (_previewIndex >= _previewPages.Count)
            _previewIndex = Math.Max(0, _previewPages.Count - 1);

        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_previewPages.Count == 0)
        {
            Preview.SetSampleContent("", "", "");
            Preview.IsBlank = true;
            SlideCountText = "No content";
        }
        else
        {
            Preview.SetSampleContent("", _previewPages[_previewIndex], "");
            SlideCountText = $"{_previewPages.Count} slide{(_previewPages.Count == 1 ? "" : "s")}";
        }
        this.RaisePropertyChanged(nameof(PreviewPositionText));
    }

    private async Task SaveAsync()
    {
        try
        {
            var song = new Song
            {
                Id = _songId,
                Title = Title.Trim(),
                Artist = string.IsNullOrWhiteSpace(Artist) ? null : Artist.Trim(),
                LinesPerSlide = LinesPerSlide,
            };

            int verse = 0, order = 0;
            foreach (var s in Sections)
            {
                if (string.IsNullOrWhiteSpace(s.Text)) continue;
                var type = string.IsNullOrWhiteSpace(s.SectionType) ? "verse" : s.SectionType;
                if (type == "verse") verse++;
                song.Sections.Add(new SongSection
                {
                    SectionType = type,
                    SectionOrder = type == "verse" ? verse : ++order,
                    Text = s.Text.Trim(),
                });
            }

            var saved = await _library.SaveSongAsync(song);
            _songId = saved.Id;
            this.RaisePropertyChanged(nameof(WindowTitle));
            Saved?.Invoke();
            CloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save song {Title}", Title);
        }
    }

    /// <summary>Loads an existing song for editing.</summary>
    public void SetSong(Song song)
    {
        _songId = song.Id;
        Title = song.Title;
        Artist = song.Artist ?? "";
        LinesPerSlide = song.LinesPerSlide;

        Sections.Clear();
        foreach (var s in song.Sections.OrderBy(x => x.SectionOrder))
            Sections.Add(new SongSectionVm(s.SectionType, s.Text));

        RawLyrics = string.Join("\n\n", song.Sections.OrderBy(x => x.SectionOrder).Select(s => s.Text));
        SelectedSection = Sections.Count > 0 ? Sections[0] : null;
        this.RaisePropertyChanged(nameof(WindowTitle));
    }

    public void Dispose()
    {
        _textSub?.Dispose();
        Preview.Dispose();
    }
}
