namespace ChurchProjection.Core.Models.Content;

public class Song
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Artist { get; set; }
    public string? CcliNumber { get; set; }
    public string? CopyrightInfo { get; set; }
    public string? Tags { get; set; }

    /// <summary>Per-song override for how many lyric lines to show per projected slide.
    /// 0 = use the theme's automatic text-fit (default).</summary>
    public int LinesPerSlide { get; set; }

    /// <summary>Owning organization (tenant). Songs are shared across an org's branches.</summary>
    public string? OrganizationId { get; set; }

    /// <summary>Stable cloud identity (uuid) used for org-level sync. Null until first synced.</summary>
    public string? CloudId { get; set; }

    /// <summary>Soft-delete tombstone, propagated through sync so deletes reach other devices.</summary>
    public bool Deleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<SongSection> Sections { get; set; } = [];
}

public class SongSection
{
    public long Id { get; set; }
    public long SongId { get; set; }
    public string SectionType { get; set; } = "verse";
    public int SectionOrder { get; set; }
    public string Text { get; set; } = string.Empty;

    public string Label => SectionType switch
    {
        "verse" => $"Verse {SectionOrder}",
        "chorus" => "Chorus",
        "bridge" => "Bridge",
        "pre-chorus" => "Pre-Chorus",
        "tag" => "Tag",
        "outro" => "Outro",
        "intro" => "Intro",
        _ => SectionType
    };
}
