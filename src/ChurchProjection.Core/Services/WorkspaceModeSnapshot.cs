namespace ChurchProjection.Core.Services;

public readonly record struct WorkspaceModePlace(
    string SearchQuery,
    string? BrowseBook,
    int? BrowseChapter);

/// <summary>
/// Session-only place for each workspace mode. First Bible visit is Genesis 1.
/// Media folder is remembered but not wiped by Bible/Songs restores.
/// </summary>
public sealed class WorkspaceModeSnapshot
{
    public static WorkspaceModePlace FirstBiblePlace { get; } = new("", "Genesis", 1);

    public WorkspaceModePlace? Bible { get; private set; }
    public WorkspaceModePlace? Songs { get; private set; }
    public string? MediaFolderId { get; private set; }

    public void RememberBible(WorkspaceModePlace place) => Bible = place;

    public void RememberSongs(WorkspaceModePlace place) => Songs = place;

    public void RememberMediaFolder(string? folderId) => MediaFolderId = folderId;

    public WorkspaceModePlace RestoreBible() => Bible ?? FirstBiblePlace;

    public WorkspaceModePlace? RestoreSongs() => Songs;
}
