using System.Reactive.Subjects;

namespace ChurchProjection.Core.Services;

public interface ITranscriptionService : IDisposable
{
    IObservable<TranscriptionSegment> Segments { get; }
    IObservable<string> RollingTranscript { get; }
    IObservable<bool> IsListening { get; }
    IObservable<string> StatusMessage { get; }
    IObservable<float> AudioLevel { get; }

    Task<List<string>> GetAvailableDevicesAsync();
    Task StartAsync(string? deviceName = null);
    Task StopAsync();
    bool IsRunning { get; }
}

public record TranscriptionSegment(
    string Text,
    DateTimeOffset Timestamp,
    float Confidence);
