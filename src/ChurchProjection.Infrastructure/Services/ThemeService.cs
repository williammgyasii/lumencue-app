using System.Reactive;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChurchProjection.Core.Models.Slides;
using ChurchProjection.Core.Models.Theme;
using ChurchProjection.Core.Services;
using ChurchProjection.Infrastructure.Data;
using Serilog;

namespace ChurchProjection.Infrastructure.Services;

public sealed class ThemeService : IThemeService
{
    private const string ThemesKey = "themes_json";
    private const string AssignmentsKey = "theme_assignments_json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SettingsRepository _settings;
    private readonly Subject<Unit> _changed = new();

    private List<Theme> _themes = [];
    private Dictionary<SlideType, string> _assignments = [];

    public ThemeService(SettingsRepository settings) => _settings = settings;

    public IReadOnlyList<Theme> Themes => _themes;
    public IObservable<Unit> Changed => _changed;

    public async Task LoadAsync()
    {
        var themesJson = await _settings.GetAsync(ThemesKey);
        var assignmentsJson = await _settings.GetAsync(AssignmentsKey);

        _themes = TryDeserialize<List<Theme>>(themesJson) ?? [];
        if (_themes.Count == 0)
            _themes = BuildDefaultThemes();

        _assignments = TryDeserialize<Dictionary<SlideType, string>>(assignmentsJson) ?? [];
        EnsureAssignmentDefaults();

        if (string.IsNullOrEmpty(themesJson) || string.IsNullOrEmpty(assignmentsJson))
            await PersistAsync();

        _changed.OnNext(Unit.Default);
    }

    public Theme ResolveFor(SlideType slideType)
    {
        var name = GetAssignment(slideType);
        return GetByName(name) ?? _themes.FirstOrDefault() ?? BuildDefaultThemes()[0];
    }

    public Theme? GetByName(string name)
        => _themes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    public string GetAssignment(SlideType slideType)
    {
        if (_assignments.TryGetValue(slideType, out var name) && GetByName(name) is not null)
            return name;
        return _themes.FirstOrDefault()?.Name ?? "House - Dark";
    }

    public async Task SetAssignmentAsync(SlideType slideType, string themeName)
    {
        _assignments[slideType] = themeName;
        await PersistAsync();
        _changed.OnNext(Unit.Default);
    }

    public async Task AddOrUpdateAsync(Theme theme, string? originalName = null)
    {
        var key = originalName ?? theme.Name;
        var idx = _themes.FindIndex(t => string.Equals(t.Name, key, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            _themes[idx] = theme;
            // Re-point assignments if the theme was renamed.
            if (!string.Equals(key, theme.Name, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var st in _assignments.Keys.ToList())
                    if (string.Equals(_assignments[st], key, StringComparison.OrdinalIgnoreCase))
                        _assignments[st] = theme.Name;
            }
        }
        else
        {
            _themes.Add(theme);
        }

        await PersistAsync();
        _changed.OnNext(Unit.Default);
    }

    public async Task DeleteAsync(string themeName)
    {
        if (_themes.Count <= 1) return; // never leave zero themes
        _themes.RemoveAll(t => string.Equals(t.Name, themeName, StringComparison.OrdinalIgnoreCase));

        var fallback = _themes[0].Name;
        foreach (var st in _assignments.Keys.ToList())
            if (string.Equals(_assignments[st], themeName, StringComparison.OrdinalIgnoreCase))
                _assignments[st] = fallback;

        await PersistAsync();
        _changed.OnNext(Unit.Default);
    }

    private void EnsureAssignmentDefaults()
    {
        var defaultName = _themes.FirstOrDefault()?.Name ?? "House - Dark";
        foreach (SlideType st in Enum.GetValues<SlideType>())
            if (!_assignments.ContainsKey(st) || GetByName(_assignments[st]) is null)
                _assignments[st] = defaultName;
    }

    private async Task PersistAsync()
    {
        try
        {
            await _settings.SetAsync(ThemesKey, JsonSerializer.Serialize(_themes, JsonOptions));
            await _settings.SetAsync(AssignmentsKey, JsonSerializer.Serialize(_assignments, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist themes");
        }
    }

    private static T? TryDeserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to deserialize theme data; using defaults");
            return default;
        }
    }

    private static List<Theme> BuildDefaultThemes() =>
    [
        new Theme
        {
            Name = "House - Dark",
            BackgroundKind = ThemeBackgroundKind.Solid,
            BackgroundColor = "#FF0F172A",
            ShadowEnabled = true,
        },
        new Theme
        {
            Name = "ATEM Green Key",
            BackgroundKind = ThemeBackgroundKind.KeyColorGreen,
            ShadowEnabled = false,
            OutlineEnabled = true,
            OutlineColor = "#FF000000",
            OutlineWidth = 3,
        },
        new Theme
        {
            Name = "ATEM Luma Black",
            BackgroundKind = ThemeBackgroundKind.KeyColorBlack,
            ShadowEnabled = false,
            OutlineEnabled = false,
        },
        new Theme
        {
            Name = "Lower Third",
            Layout = ThemeLayout.LowerThird,
            BackgroundKind = ThemeBackgroundKind.Solid,
            BackgroundColor = "#CC000000",
            ShadowEnabled = true,
            BodyFontSize = 54,
        },
    ];
}
