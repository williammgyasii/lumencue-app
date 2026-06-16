using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChurchProjection.Core.Models.Content;

/// <summary>
/// Mutable, bindable view of an <see cref="ChurchProjection.Core.Services.AiSuggestion"/> shown in the
/// live suggestions list. Body/footer can be updated in place when a scripture reference finishes
/// hydrating, so it raises change notifications.
/// </summary>
public sealed class SuggestionItem : INotifyPropertyChanged
{
    private string _title = "";
    private string _body = "";
    private string _footer = "";
    private double _score;
    private string _matchType = "";
    private bool _isBookmarked;
    private bool _isLive;

    public string ContentId { get; init; } = "";

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string Body
    {
        get => _body;
        set => SetField(ref _body, value);
    }

    public string Footer
    {
        get => _footer;
        set => SetField(ref _footer, value);
    }

    public double Score
    {
        get => _score;
        set
        {
            if (SetField(ref _score, value))
                OnPropertyChanged(nameof(ScoreDisplay));
        }
    }

    public string MatchType
    {
        get => _matchType;
        set
        {
            if (SetField(ref _matchType, value))
            {
                OnPropertyChanged(nameof(Icon));
                OnPropertyChanged(nameof(IsScripture));
            }
        }
    }

    public bool IsBookmarked
    {
        get => _isBookmarked;
        set => SetField(ref _isBookmarked, value);
    }

    /// <summary>True when this suggestion is currently projected on the live output.</summary>
    public bool IsLive
    {
        get => _isLive;
        set => SetField(ref _isLive, value);
    }

    public bool IsScripture => MatchType == "scripture_reference";

    public string ScoreDisplay => $"{Score:P0}";

    public string Icon => MatchType switch
    {
        "scripture_reference" => "B",
        "semantic" => "S",
        _ => "M",
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
