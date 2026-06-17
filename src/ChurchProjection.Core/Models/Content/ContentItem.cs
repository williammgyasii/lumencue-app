using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChurchProjection.Core.Models.Content;

public enum ContentItemType { Scripture, Song, Announcement }

/// <summary>A searchable library entry (scripture passage or song section) shown in the operator UI.</summary>
public class ContentItem : INotifyPropertyChanged
{
    private bool _isLive;
    private bool _isOrigin;

    public ContentItemType Type { get; set; }
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Body { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Footer { get; set; } = "";
    public object? Source { get; set; }

    /// <summary>Per-song lyric lines-per-slide override (0 = theme auto-fit). Ignored for non-songs.</summary>
    public int LinesPerSlide { get; set; }

    /// <summary>True when this item is currently projected on the live output.</summary>
    public bool IsLive
    {
        get => _isLive;
        set
        {
            if (_isLive == value) return;
            _isLive = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// True for the single verse the operator opened this chapter from (via "Show Full Chapter").
    /// Highlighted in the chapter list so the operator can spot the verse that brought them here
    /// instead of hunting for it again.
    /// </summary>
    public bool IsOrigin
    {
        get => _isOrigin;
        set
        {
            if (_isOrigin == value) return;
            _isOrigin = value;
            OnPropertyChanged();
        }
    }

    public bool IsScripture => Type == ContentItemType.Scripture;

    public string Icon => Type switch
    {
        ContentItemType.Scripture => "B",
        ContentItemType.Song => "S",
        ContentItemType.Announcement => "A",
        _ => "—"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
