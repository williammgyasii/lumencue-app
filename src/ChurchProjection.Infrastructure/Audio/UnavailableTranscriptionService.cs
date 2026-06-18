using System.Reactive.Linq;
using System.Reactive.Subjects;
using ChurchProjection.Core.Services;

namespace ChurchProjection.Infrastructure.Audio;

/// <summary>
/// No-op transcription engine used when the build has no cloud backend configured. Speech-to-text is
/// cloud-only (Deepgram); without a backend to mint tokens there is no engine to run, so this simply
/// reports an unavailable status and lets the operator drive cues manually. It never produces garbage
/// transcripts that would fire wrong scripture/song cues on screen.
/// </summary>
public sealed class UnavailableTranscriptionService : ITranscriptionService
{
    private readonly string _reason;

    public UnavailableTranscriptionService(
        string reason = "Speech-to-text is unavailable: this build is not connected to the LumenCue service.")
    {
        _reason = reason;
    }

    public IObservable<TranscriptionSegment> Segments => Observable.Empty<TranscriptionSegment>();
    public IObservable<string> RollingTranscript => Observable.Return("");
    public IObservable<bool> IsListening => Observable.Return(false);
    public IObservable<string> StatusMessage => Observable.Return(_reason);
    public IObservable<float> AudioLevel => Observable.Return(0f);
    public IObservable<string> EngineName => Observable.Return("");
    public bool IsRunning => false;

    public Task<List<string>> GetAvailableDevicesAsync() => Task.FromResult(new List<string>());
    public Task StartAsync(string? deviceName = null) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public void Dispose() { }
}
