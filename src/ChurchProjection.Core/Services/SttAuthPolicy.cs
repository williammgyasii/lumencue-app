namespace ChurchProjection.Core.Services;

public enum SttAuthMode
{
    CloudToken,
    LocalKey,
    Unavailable
}

/// <summary>
/// How Scribe should authenticate. Cloud single-use tokens win; a local
/// ElevenLabs workspace key is the fallback when the mint fails.
/// </summary>
public static class SttAuthPolicy
{
    public static SttAuthMode Resolve(string? cloudToken, string? localKey)
    {
        if (!string.IsNullOrWhiteSpace(cloudToken))
            return SttAuthMode.CloudToken;
        if (!string.IsNullOrWhiteSpace(localKey))
            return SttAuthMode.LocalKey;
        return SttAuthMode.Unavailable;
    }
}
