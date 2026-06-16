using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using ChurchProjection.Core.Models.Projection;

namespace ChurchProjection.UI.Services;

/// <summary>
/// Owns the operator's swappable background media layer: a managed library of still images and
/// motion loops, the currently selected item, and a stream of <see cref="Bitmap"/> frames the
/// projector outputs paint behind the text. Selecting a clip swaps the look live without ever
/// touching the active theme.
/// </summary>
public interface ILiveBackgroundService
{
    /// <summary>All backgrounds in the library.</summary>
    IReadOnlyList<LiveBackground> Items { get; }

    /// <summary>Emits whenever the library list changes (add/remove).</summary>
    IObservable<IReadOnlyList<LiveBackground>> ItemsChanged { get; }

    /// <summary>The currently live background, or null when none is selected.</summary>
    LiveBackground? Selected { get; }

    /// <summary>Emits the selected background (or null) whenever it changes.</summary>
    IObservable<LiveBackground?> SelectedChanged { get; }

    /// <summary>
    /// The current frame to paint: a still bitmap for an image, live frames for a motion clip, or
    /// null when nothing is selected (outputs then fall back to their theme background).
    /// </summary>
    IObservable<Bitmap?> Frame { get; }

    Task LoadAsync();

    /// <summary>Adds a media file to the library (image or video, inferred from extension).</summary>
    Task<LiveBackground?> AddAsync(string path);

    /// <summary>Removes a background; clears the live layer if it was selected.</summary>
    Task RemoveAsync(LiveBackground item);

    /// <summary>Makes a background live (null clears the layer back to the theme background).</summary>
    void Select(LiveBackground? item);
}
