namespace ChurchProjection.Core.Services;

/// <summary>Operator-visible speech-to-text copy. Vendor names stay in logs, not on the desk.</summary>
public static class SttOperatorCopy
{
    public const string EngineLabel = "Transcription agent";
    public const string Connecting = "Connecting to transcription agent...";
    public const string TranscriptionError = "Transcription error";
}
