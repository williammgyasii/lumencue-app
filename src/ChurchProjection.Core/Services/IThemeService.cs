using ChurchProjection.Core.Models.Slides;
using ChurchProjection.Core.Models.Theme;

namespace ChurchProjection.Core.Services;

/// <summary>
/// Stores named <see cref="Theme"/>s and the mapping of which theme applies to each
/// <see cref="SlideType"/>. Emits <see cref="Changed"/> whenever themes or assignments change
/// so the projector can re-resolve its look live.
/// </summary>
public interface IThemeService
{
    IReadOnlyList<Theme> Themes { get; }

    /// <summary>Notifies subscribers (e.g. the projector / studio preview) that themes or assignments changed.</summary>
    IObservable<System.Reactive.Unit> Changed { get; }

    Theme ResolveFor(SlideType slideType);
    Theme? GetByName(string name);

    string GetAssignment(SlideType slideType);
    Task SetAssignmentAsync(SlideType slideType, string themeName);

    Task AddOrUpdateAsync(Theme theme, string? originalName = null);
    Task DeleteAsync(string themeName);

    Task LoadAsync();
}
