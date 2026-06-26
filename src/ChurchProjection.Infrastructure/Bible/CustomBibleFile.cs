using System.Text.Json;

namespace ChurchProjection.Infrastructure.Bible;

/// <summary>One verse in a hosted custom-translation file.</summary>
public record CustomBibleVerse(string Book, int Chapter, int Verse, string Text);

/// <summary>
/// The on-the-wire shape of a hosted custom Bible (e.g. the Passion Translation): a flat list of
/// verses with a short code and display name. The importer writes this file and the desktop app's
/// <see cref="BibleCacheService"/> reads it, so the two stay in lock-step through this single type.
/// </summary>
public record CustomBibleFile(string Code, string Name, IReadOnlyList<CustomBibleVerse> Verses)
{
    /// <summary>Shared JSON options so the writer and reader always agree on casing.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static CustomBibleFile FromJson(string json) =>
        JsonSerializer.Deserialize<CustomBibleFile>(json, JsonOptions)
        ?? throw new InvalidOperationException("Custom Bible file was empty or malformed.");
}
