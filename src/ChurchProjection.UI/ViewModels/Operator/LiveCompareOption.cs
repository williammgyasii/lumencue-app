using ReactiveUI;

namespace ChurchProjection.UI.ViewModels.Operator;

/// <summary>A checkbox row in the Now Live cog. At most two can be on.</summary>
public sealed class LiveCompareOption : ReactiveObject
{
    private readonly Func<string, bool, bool> _trySet;
    private bool _isSelected;
    private bool _isEnabled = true;

    public LiveCompareOption(string code, bool selected, Func<string, bool, bool> trySet)
    {
        Code = code;
        _isSelected = selected;
        _trySet = trySet;
    }

    public string Code { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            if (!_trySet(Code, value))
            {
                this.RaisePropertyChanged();
                return;
            }

            this.RaiseAndSetIfChanged(ref _isSelected, value);
        }
    }

    /// <summary>Unchecked rows lock when two translations are already picked.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
    }
}
