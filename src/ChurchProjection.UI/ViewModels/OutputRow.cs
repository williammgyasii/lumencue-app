using System.Collections.ObjectModel;
using ReactiveUI;

namespace ChurchProjection.UI.ViewModels;

public enum OutputKind
{
    Display,
    Windowed,
    ProPresenter,
    Ndi,
}

/// <summary>
/// A screen the operator can switch on/off, give a custom name, and assign a view (theme) to: a physical
/// display, a windowed preview, or ProPresenter. Every active screen renders the single program feed; the
/// window/output coordinator reacts to changes.
/// </summary>
public sealed class OutputRow : ReactiveObject
{
    /// <summary>Sentinel option meaning "use the global per-content theme assignment".</summary>
    public const string FollowContent = "Follow content";

    private string _name;
    private bool _isActive;
    private string _selectedThemeOption = FollowContent;

    public OutputRow(string key, OutputKind kind, string name, DisplayOption? display,
        ObservableCollection<string> themeOptions)
    {
        Key = key;
        Kind = kind;
        _name = name;
        Display = display;
        ThemeOptions = themeOptions;
    }

    /// <summary>Stable identity: display key, "windowed", or "propresenter".</summary>
    public string Key { get; }

    public OutputKind Kind { get; }

    /// <summary>Geometry for Display/Windowed outputs; null for ProPresenter.</summary>
    public DisplayOption? Display { get; }

    /// <summary>The shared theme-name list, with <see cref="FollowContent"/> first.</summary>
    public ObservableCollection<string> ThemeOptions { get; }

    /// <summary>The operator-editable screen name (e.g. "LED Wall", "Lobby").</summary>
    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    /// <summary>Whether this screen is currently live.</summary>
    public bool IsActive
    {
        get => _isActive;
        set => this.RaiseAndSetIfChanged(ref _isActive, value);
    }

    /// <summary>The theme this screen forces (e.g. a green-screen lower-third), or "Follow content".</summary>
    public string SelectedThemeOption
    {
        get => _selectedThemeOption;
        set => this.RaiseAndSetIfChanged(ref _selectedThemeOption, value);
    }

    /// <summary>Resolved override theme name, or null to follow the global per-content assignment.</summary>
    public string? ThemeOverride =>
        string.IsNullOrEmpty(_selectedThemeOption) || _selectedThemeOption == FollowContent
            ? null
            : _selectedThemeOption;
}
