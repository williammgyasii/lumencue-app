using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using ChurchProjection.Core.Models.Projection;

namespace ChurchProjection.UI.Services;

/// <summary>A selectable audio output device for announcement sound (id empty = system default).</summary>
public sealed record AudioOutputOption(string Id, string Name);

/// <summary>A snapshot of a target's live video transport, for driving the playback controls.</summary>
public sealed record PlaybackStatus(bool IsVideo, bool IsPaused, double Position, long TimeMs, long LengthMs, int Volume);

/// <summary>Well-known media routing targets.</summary>
public static class MediaTarget
{
    /// <summary>The "all screens" target: media sent here shows on every screen that has nothing screen-specific.</summary>
    public const string AllScreens = "*all*";
}

/// <summary>
/// Owns the operator's announcement media: a managed library of full-screen / lower-third graphics and
/// video clips. Media is routed PER TARGET, where a target is either an individual screen (an output's
/// stable key) or <see cref="MediaTarget.AllScreens"/>. A screen shows its own screen-specific media when
/// set, otherwise whatever is sent to All — so clicking a tile plays everywhere, while a right-click can
/// pin a clip to one screen. Video announcements play WITH AUDIO, routed to a chosen output device.
/// </summary>
public interface IAnnouncementService
{
    /// <summary>All announcements in the library.</summary>
    IReadOnlyList<AnnouncementMedia> Items { get; }

    /// <summary>Emits whenever the library list changes (add/remove).</summary>
    IObservable<IReadOnlyList<AnnouncementMedia>> ItemsChanged { get; }

    /// <summary>All collections (folders) media can be grouped into.</summary>
    IReadOnlyList<MediaCollection> Collections { get; }

    /// <summary>Emits whenever the set of collections changes (created/removed).</summary>
    IObservable<IReadOnlyList<MediaCollection>> CollectionsChanged { get; }

    /// <summary>Creates a new (empty) collection with the given name and returns it.</summary>
    Task<MediaCollection> CreateCollectionAsync(string name);

    /// <summary>Moves a media item into a collection (or to no folder / "All media" when
    /// <paramref name="collectionId"/> is null). No-op if the item isn't found.</summary>
    Task MoveToCollectionAsync(string mediaId, string? collectionId);

    /// <summary>Emits whenever a target's live media changes (target key + item, item null = cleared).</summary>
    IObservable<(string Target, AnnouncementMedia? Item)> LiveChanged { get; }

    /// <summary>Available audio output devices for video sound.</summary>
    IReadOnlyList<AudioOutputOption> AudioDevices { get; }

    /// <summary>The chosen audio output device id (empty = system default).</summary>
    string AudioDeviceId { get; }

    Task LoadAsync();

    /// <summary>Adds a media file to the library (image or video, inferred from extension), optionally
    /// into a collection. If the same file is already in the library it is not duplicated — the existing
    /// item is returned instead.</summary>
    Task<AnnouncementMedia?> AddAsync(string path, string? collectionId = null);

    /// <summary>Removes an announcement; clears it from any target where it is live.</summary>
    Task RemoveAsync(AnnouncementMedia item);

    /// <summary>The media currently live on a target, or null when none is set there.</summary>
    AnnouncementMedia? GetLive(string target);

    /// <summary>The frame stream a screen should paint: its own screen-specific media if set, else the
    /// All-screens media (null when nothing applies). For <see cref="MediaTarget.AllScreens"/> it is just
    /// the All-screens stream.</summary>
    IObservable<Bitmap?> FrameFor(string screenKey);

    /// <summary>Sends media live on a target (null clears it). Videos start playing with sound.</summary>
    void Select(string target, AnnouncementMedia? item);

    /// <summary>Routes announcement audio to the given device (empty = system default), live for any playing clips.</summary>
    void SetAudioDevice(string deviceId);

    // ----- Transport for a target's live video -----

    /// <summary>Pause or resume the target's live video.</summary>
    void SetPaused(string target, bool paused);

    /// <summary>Scrub the target's live video to a normalized position (0..1).</summary>
    void Seek(string target, double fraction);

    /// <summary>Jump the target's live video by a relative number of seconds (negative = back).</summary>
    void SkipSeconds(string target, double seconds);

    /// <summary>Sets the target's playback volume (0..100); remembered for the next clip on that target.</summary>
    void SetVolume(string target, int volume);

    /// <summary>Reads the target's current transport state, or null when nothing is live there.</summary>
    PlaybackStatus? ReadPlayback(string target);
}
