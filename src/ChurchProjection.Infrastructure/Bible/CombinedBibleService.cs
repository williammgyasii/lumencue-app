using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using Serilog;

namespace ChurchProjection.Infrastructure.Bible;

public class CombinedBibleService : IBibleApiService
{
    private readonly FreeBibleApiClient _freeApi;
    private readonly ApiBibleClient? _apiBible;

    public CombinedBibleService(FreeBibleApiClient freeApi, ApiBibleClient? apiBible = null)
    {
        _freeApi = freeApi;
        _apiBible = apiBible;
    }

    public async Task<ScripturePassage?> FetchPassageAsync(ScriptureReference reference, string translation = "BSB", CancellationToken cancellationToken = default)
    {
        Log.Information("Fetching {Ref} ({Translation})", reference, translation);

        var result = await _freeApi.FetchPassageAsync(reference, translation, cancellationToken).ConfigureAwait(false);
        if (result is not null) return result;

        if (_apiBible is not null)
        {
            result = await _apiBible.FetchPassageAsync(reference, translation, cancellationToken).ConfigureAwait(false);
            if (result is not null) return result;
        }

        Log.Warning("Could not fetch {Ref} from any Bible API", reference);
        return null;
    }

    public async Task<List<ScripturePassage>> FetchVersesAsync(ScriptureReference reference, string translation = "BSB", CancellationToken cancellationToken = default)
    {
        Log.Information("Fetching verses for {Ref} ({Translation})", reference, translation);

        var verses = await _freeApi.FetchVersesAsync(reference, translation, cancellationToken).ConfigureAwait(false);
        if (verses.Count > 0) return verses;

        if (_apiBible is not null)
        {
            verses = await _apiBible.FetchVersesAsync(reference, translation, cancellationToken).ConfigureAwait(false);
            if (verses.Count > 0) return verses;
        }

        return [];
    }

    public async Task<List<string>> GetAvailableTranslationsAsync()
    {
        var translations = await _freeApi.GetAvailableTranslationsAsync();

        if (_apiBible is not null)
        {
            var apiBibleTranslations = await _apiBible.GetAvailableTranslationsAsync();
            foreach (var t in apiBibleTranslations)
            {
                if (!translations.Contains(t, StringComparer.OrdinalIgnoreCase))
                    translations.Add(t);
            }
        }

        return translations;
    }
}
