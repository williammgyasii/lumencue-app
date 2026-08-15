using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Core.Services;

/// <summary>
/// Inserts a section into a song and resequences <see cref="SongSection.SectionOrder"/>
/// so the new slide stays where the operator placed it after save/reload.
/// </summary>
public static class SongSlideInsert
{
    public static SongSection After(IList<SongSection> sections, SongSection? after, string? sectionType, string? text)
    {
        var created = new SongSection
        {
            SectionType = string.IsNullOrWhiteSpace(sectionType) ? "verse" : sectionType.Trim(),
            Text = text ?? "",
        };

        var index = after is null ? sections.Count : sections.IndexOf(after) + 1;
        if (index <= 0) index = sections.Count;
        sections.Insert(index, created);

        for (var i = 0; i < sections.Count; i++)
            sections[i].SectionOrder = i + 1;

        return created;
    }
}
