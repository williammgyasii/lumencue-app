using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Core.Services;

public interface IBibleApiService
{
    Task<ScripturePassage?> FetchPassageAsync(ScriptureReference reference, string translation = "BSB", CancellationToken cancellationToken = default);
    Task<List<ScripturePassage>> FetchVersesAsync(ScriptureReference reference, string translation = "BSB", CancellationToken cancellationToken = default);
    Task<List<string>> GetAvailableTranslationsAsync();
}
