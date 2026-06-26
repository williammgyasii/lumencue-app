using System.Collections.ObjectModel;
using Avalonia.Media;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using ReactiveUI;

namespace ChurchProjection.UI.ViewModels.Operator;

/// <summary>
/// A single song search result. Double-clicking expands it to reveal its sections inline, so the
/// operator can project them one at a time instead of dumping the whole song into the queue.
/// </summary>
public class SongHitViewModel : ViewModelBase
{
    private bool _isExpanded;

    public Song Song { get; }
    public string Title => Song.Title;
    public string? Artist => Song.Artist;
    public bool HasArtist => !string.IsNullOrWhiteSpace(Song.Artist);
    public string Snippet { get; }
    public string? MatchedLabel { get; }
    public bool HasMatchedLabel => !string.IsNullOrWhiteSpace(MatchedLabel);

    public ObservableCollection<SongSlideItem> Slides { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }

    public SongHitViewModel(SongSearchHit hit)
    {
        Song = hit.Song;
        Snippet = hit.Snippet;
        MatchedLabel = hit.Section?.Label;
        Slides = [.. Song.Sections.Select(s => new SongSlideItem(Song, s))];
    }

    public void ToggleExpanded() => IsExpanded = !IsExpanded;
}

/// <summary>One projectable section within an expanded song result.</summary>
public class SongSlideItem : ReactiveObject
{
    private const double BaseWidth = 206;
    private const double BasePreviewHeight = 116;
    private const double BaseTextSize = 11;
    private const double BaseLabelSize = 10;

    private bool _isLive;
    private bool _isSuggested;
    private double _cardWidth = BaseWidth;
    private double _previewHeight = BasePreviewHeight;
    private double _textSize = BaseTextSize;
    private double _labelSize = BaseLabelSize;

    public string Label { get; }
    public string Preview { get; }
    public string Text { get; }
    public ContentItem Item { get; }

    /// <summary>The song this slide belongs to (for quick/full edit from the context menu).</summary>
    public Song Song { get; }

    /// <summary>The specific section this slide renders (same instance held by <see cref="Song"/>).</summary>
    public SongSection Section { get; }

    /// <summary>ProPresenter-style colour for the slide's label bar, keyed off the section type.</summary>
    public IBrush BarBrush { get; }

    /// <summary>True when this exact section is the one currently on the live output.</summary>
    public bool IsLive
    {
        get => _isLive;
        set => this.RaiseAndSetIfChanged(ref _isLive, value);
    }

    /// <summary>True when lyric-follow believes this is the slide being sung right now (Assist mode
    /// highlights it and tees it up; the operator still decides to send it).</summary>
    public bool IsSuggested
    {
        get => _isSuggested;
        set => this.RaiseAndSetIfChanged(ref _isSuggested, value);
    }

    /// <summary>Pre-tokenised lyric text used by the lyric-follow matcher (computed once).</summary>
    public IReadOnlyList<string> MatchTokens { get; }

    // Operator-adjustable card metrics (driven by the size slider in the Now Singing tab).
    public double CardWidth { get => _cardWidth; private set => this.RaiseAndSetIfChanged(ref _cardWidth, value); }
    public double PreviewHeight { get => _previewHeight; private set => this.RaiseAndSetIfChanged(ref _previewHeight, value); }
    public double TextSize { get => _textSize; private set => this.RaiseAndSetIfChanged(ref _textSize, value); }
    public double LabelSize { get => _labelSize; private set => this.RaiseAndSetIfChanged(ref _labelSize, value); }

    /// <summary>Rescales the card and its text relative to the default size.</summary>
    public void ApplyScale(double scale)
    {
        CardWidth = BaseWidth * scale;
        PreviewHeight = BasePreviewHeight * scale;
        TextSize = BaseTextSize * scale;
        LabelSize = BaseLabelSize * scale;
    }

    public SongSlideItem(Song song, SongSection section)
        : this(song, section, section.Text, section.Label) { }

    /// <summary>Creates a card for one projected page (a slice of a section), so the Now Singing tab
    /// mirrors exactly what will go on screen when lines-per-slide breaking is in effect.</summary>
    public SongSlideItem(Song song, SongSection section, string pageText, string label)
    {
        Song = song;
        Section = section;
        Label = label;
        Text = pageText;
        MatchTokens = LyricFollow.Tokenize(pageText);
        BarBrush = BrushForLabel(section.Label);
        Preview = pageText.Replace("\r", "").Replace('\n', ' ');
        Item = new ContentItem
        {
            Type = ContentItemType.Song,
            // Projected title is just the song name; the section (Chorus / Verse 5 …) stays in the
            // operator's Now Singing card (Label + colored bar) and in Tag, but never on screen.
            Title = song.Title,
            Subtitle = song.Artist ?? "",
            Body = pageText,
            Tag = label,
            Footer = song.Title,
            // The page text is already split; keep it on one slide when projected.
            LinesPerSlide = 0,
            Source = song,
        };
    }

    private static IBrush BrushForLabel(string label)
    {
        var l = (label ?? "").ToLowerInvariant();
        if (l.Contains("chorus")) return new SolidColorBrush(Color.Parse("#E5197D"));
        if (l.Contains("pre-chorus") || l.Contains("prechorus")) return new SolidColorBrush(Color.Parse("#0EA5E9"));
        if (l.Contains("verse")) return new SolidColorBrush(Color.Parse("#2563EB"));
        if (l.Contains("bridge")) return new SolidColorBrush(Color.Parse("#7C3AED"));
        if (l.Contains("intro") || l.Contains("outro") || l.Contains("ending") || l.Contains("tag")) return new SolidColorBrush(Color.Parse("#D97706"));
        return new SolidColorBrush(Color.Parse("#4B5563"));
    }
}
