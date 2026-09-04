using System.Reactive.Subjects;

namespace ChurchProjection.Core.Services;

public interface ITranscriptionService : IDisposable
{
    IObservable<TranscriptionSegment> Segments { get; }
    IObservable<string> RollingTranscript { get; }

    /// <summary>
    /// Live, not-yet-finalised partial of the current utterance (the running recognition hypothesis).
    /// Emitted continuously as the speaker talks, well before Deepgram finalises the utterance on an
    /// end-of-speech pause. Fed into the matcher so spoken scripture references surface as they are
    /// said rather than lagging a full utterance behind the speaker.
    /// </summary>
    IObservable<string> InterimTranscript { get; }
    IObservable<bool> IsListening { get; }
    IObservable<string> StatusMessage { get; }
    IObservable<float> AudioLevel { get; }
    /// <summary>Human-readable name of the active engine (e.g. "Transcription agent").</summary>
    IObservable<string> EngineName { get; }

    Task<List<string>> GetAvailableDevicesAsync();
    Task StartAsync(string? deviceName = null);
    Task StopAsync();
    bool IsRunning { get; }

    /// <summary>Software capture gain (mic sensitivity) applied to incoming audio before clipping.
    /// 1.0 = no boost. Settable live so the operator can tune a quiet/loud mic mid-service without
    /// restarting capture; implementations clamp to a safe range.</summary>
    float InputGain { get; set; }
}

public record TranscriptionSegment(
    string Text,
    DateTimeOffset Timestamp,
    float Confidence);
