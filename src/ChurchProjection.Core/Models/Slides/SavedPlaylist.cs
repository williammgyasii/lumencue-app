using System.Text.Json.Serialization;

namespace ChurchProjection.Core.Models.Slides;

/// <summary>A named, saved set list the operator can reload for fast referencing
/// (e.g. a song set, a sermon's scriptures, a recurring liturgy).</summary>
public sealed class SavedPlaylist
{
    public string Name { get; set; } = "";
    public List<QueueSlide> Items { get; set; } = [];

    [JsonIgnore]
    public int ItemCount => Items.Count;

    [JsonIgnore]
    public string CountLabel => $"{Items.Count} item{(Items.Count == 1 ? "" : "s")}";
}
