using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Models.Slides;
using ChurchProjection.Core.Models.Theme;
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
    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            this.RaiseAndSetIfChanged(ref _text, value);
            PagesChanged?.Invoke(this);
        }
    }

    /// <summary>Exposed per-row so the item template binds without casting to the parent VM.</summary>
    public IReadOnlyList<string> SectionTypes => Types;

    public ObservableCollection<SongSlidePageVm> Pages { get; } = [];

    private string _slideCountLabel = "1 slide";
    public string SlideCountLabel
    {
        get => _slideCountLabel;
        private set => this.RaiseAndSetIfChanged(ref _slideCountLabel, value);
    }

    public event Action<SongSectionVm>? PagesChanged;

    public void RefreshPages(int linesPerSlide, string label)
    {
        var chunks = SongLinesPerSlide.SplitPages(Text, linesPerSlide);
        Pages.Clear();
        var multi = chunks.Count > 1;
        for (var i = 0; i < chunks.Count; i++)
            Pages.Add(new SongSlidePageVm(multi ? $"{label} ({i + 1})" : label, chunks[i]));
        SlideCountLabel = chunks.Count == 1 ? "1 slide" : $"{chunks.Count} slides";
    }
}

public sealed class SongSlidePageVm(string label, string text)
{
    public string Label { get; } = label;
    public string Text { get; } = text;
}

/// <summary>
/// Three-pane song editor: details + auto-breakdown sections + themed line-break preview.
/// </summary>
public sealed class SongEditorViewModel : ViewModelBase, IDisposable
{
    private readonly IContentLibraryService _library;
    private readonly IThemeService _themes;

    private long _songId;
    private readonly List<string> _previewPages = [];
    private int _previewIndex;
    private IDisposable? _textSub;
    private IDisposable? _lyricsSub;
    private bool _suspendParse;

    public ProjectorViewModel Preview { get; }
    public ObservableCollection<SongSectionVm> Sections { get; } = [];
    public IReadOnlyList<string> SectionTypes => SongSectionVm.Types;
    public IReadOnlyList<string> LinesPerSlideChoices => SongLinesPerSlide.Choices;
    public IReadOnlyList<string> ThemeNames { get; }

    public ReactiveCommand<Unit, Unit> ParseCommand { get; }
    public ReactiveCommand<Unit, Unit> AddSectionCommand { get; }
    public ReactiveCommand<SongSectionVm, Unit> MoveUpCommand { get; }
    public ReactiveCommand<SongSectionVm, Unit> MoveDownCommand { get; }
    public ReactiveCommand<SongSectionVm, Unit> DeleteSectionCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviewPrevCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviewNextCommand { get; }

    public event Action<Song>? Saved;

    /// <summary>Raised after a successful save so the host window can close itself.</summary>
    public event Action? CloseRequested;

    public SongEditorViewModel(IContentLibraryService library, IThemeService themes)
    {
        _library = library;
        _themes = themes;

        ThemeNames = [.. themes.Themes.Select(t => t.Name)];
        _previewThemeName = themes.GetAssignment(SlideType.Lyric);

        Preview = new ProjectorViewModel(new ProjectionService(), themes);
        Preview.PreviewTheme(CurrentPreviewTheme);

        ParseCommand = ReactiveCommand.Create(ParseFromLyrics);

        _lyricsSub = this.WhenAnyValue(x => x.RawLyrics)
            .Skip(1)
            .DistinctUntilChanged()
            .Throttle(TimeSpan.FromMilliseconds(450))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                if (_suspendParse) return;
                ParseFromLyrics();
            });

        AddSectionCommand = ReactiveCommand.Create(() =>
        {
            var s = AttachSection(new SongSectionVm("verse", ""));
            Sections.Add(s);
            SelectedSection = s;
            RefreshAllPages();
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
            this.WhenAnyValue(x => x.Title, x => x.Artist, SongEditorRules.CanSave));

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
            if (value == _linesPerSlide) return;
            this.RaiseAndSetIfChanged(ref _linesPerSlide, value);
            this.RaisePropertyChanged(nameof(LinesPerSlideChoice));
            RebuildPreviewPages();
        }
    }

    /// <summary>"Auto" or a line count — string items so the ComboBox can actually select the value.</summary>
    public string LinesPerSlideChoice
    {
        get => SongLinesPerSlide.ToChoice(_linesPerSlide);
        set => LinesPerSlide = SongLinesPerSlide.FromChoice(value);
    }

    private string _previewThemeName = "";
    public string PreviewThemeName
    {
        get => _previewThemeName;
        set
        {
            var name = value ?? "";
            if (name == _previewThemeName) return;
            this.RaiseAndSetIfChanged(ref _previewThemeName, name);
            RebuildPreviewPages();
        }
    }

    private Theme CurrentPreviewTheme =>
        _themes.GetByName(_previewThemeName) ?? _themes.ResolveFor(SlideType.Lyric);

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

    public string WindowTitle => _songId == 0 ? "New Song" : $"Edit — {SongTitle.ToSentenceCase(Title)}";

    public void NormalizeTitle() => Title = SongTitle.ToSentenceCase(Title);

    // ───────────────────────── Behavior ─────────────────────────

    private void ParseFromLyrics()
    {
        var parsed = SongImportParser.ParseSections(RawLyrics);
        var incoming = parsed.Select(s => (s.SectionType, s.Text)).ToList();
        var current = Sections.Select(s => (s.SectionType, s.Text)).ToList();
        if (SongEditorRules.SameBreakdown(incoming, current))
        {
            RebuildPreviewPages();
            return;
        }

        var keep = SelectedSection;
        Sections.Clear();
        foreach (var s in parsed)
            Sections.Add(AttachSection(new SongSectionVm(s.SectionType, s.Text)));
        SelectedSection = keep is null
            ? (Sections.Count > 0 ? Sections[0] : null)
            : Sections.FirstOrDefault(s =>
                  s.SectionType == keep.SectionType && s.Text == keep.Text)
              ?? (Sections.Count > 0 ? Sections[0] : null);
    }

    private void Move(SongSectionVm s, int direction)
    {
        var i = Sections.IndexOf(s);
        var j = i + direction;
        if (i < 0 || j < 0 || j >= Sections.Count) return;
        Sections.Move(i, j);
        SelectedSection = s;
        RefreshAllPages();
    }

    private SongSectionVm AttachSection(SongSectionVm section)
    {
        section.PagesChanged += _ => RebuildPreviewPages();
        return section;
    }

    private void RefreshAllPages()
    {
        var verse = 0;
        foreach (var section in Sections)
        {
            var label = section.SectionType switch
            {
                "verse" => $"Verse {++verse}",
                "chorus" => "Chorus",
                "pre-chorus" => "Pre-Chorus",
                "bridge" => "Bridge",
                "tag" => "Tag",
                "outro" => "Outro",
                "intro" => "Intro",
                _ => section.SectionType,
            };
            section.RefreshPages(LinesPerSlide, label);
        }
    }

    private void RebuildPreviewPages()
    {
        RefreshAllPages();
        _previewPages.Clear();
        var section = SelectedSection;
        if (section is not null)
        {
            var theme = CurrentPreviewTheme;
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
            if (!SongEditorRules.CanSave(Title, Artist)) return;

            var song = new Song
            {
                Id = _songId,
                Title = SongTitle.ToSentenceCase(Title),
                Artist = Artist.Trim(),
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
            Saved?.Invoke(saved);
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
        _suspendParse = true;
        try
        {
            _songId = song.Id;
            Title = SongTitle.ToSentenceCase(song.Title);
            Artist = song.Artist ?? "";
            LinesPerSlide = song.LinesPerSlide;

            Sections.Clear();
            foreach (var s in song.Sections)
                Sections.Add(AttachSection(new SongSectionVm(s.SectionType, s.Text)));

            RawLyrics = string.Join("\n\n", song.Sections.Select(s => s.Text));
            SelectedSection = Sections.Count > 0 ? Sections[0] : null;
            this.RaisePropertyChanged(nameof(WindowTitle));
        }
        finally
        {
            _suspendParse = false;
        }
    }

    public void Dispose()
    {
        _textSub?.Dispose();
        _lyricsSub?.Dispose();
        Preview.Dispose();
    }
}
