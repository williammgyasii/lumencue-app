using System.Reactive.Linq;
using System.Reactive.Subjects;
using ChurchProjection.Core.Services;
using Serilog;

namespace ChurchProjection.Infrastructure.Audio;

/// <summary>
/// Picks the speech-to-text engine at start time: cloud Deepgram when a token can be minted,
/// otherwise the offline Vosk engine. Observables transparently follow whichever engine is active.
/// </summary>
public sealed class ResilientTranscriptionService : ITranscriptionService
{
    private readonly DeepgramTranscriptionService _cloud;
    private readonly VoskTranscriptionService _offline;
    private readonly ISttTokenProvider _tokens;

    private readonly BehaviorSubject<ITranscriptionService?> _active = new(null);
    private ITranscriptionService? _current;

    public ResilientTranscriptionService(
        DeepgramTranscriptionService cloud, VoskTranscriptionService offline, ISttTokenProvider tokens)
    {
        _cloud = cloud;
        _offline = offline;
        _tokens = tokens;
    }

    private IObservable<T> FromActive<T>(Func<ITranscriptionService, IObservable<T>> selector) =>
        _active.Where(s => s is not null).Select(s => selector(s!)).Switch();

    public IObservable<TranscriptionSegment> Segments => FromActive(s => s.Segments);
    public IObservable<string> RollingTranscript => FromActive(s => s.RollingTranscript);
    public IObservable<bool> IsListening => FromActive(s => s.IsListening);
    public IObservable<string> StatusMessage => FromActive(s => s.StatusMessage);
    public IObservable<float> AudioLevel => FromActive(s => s.AudioLevel);

    public bool IsRunning => _current?.IsRunning ?? false;

    // Device enumeration is identical across engines (both use WASAPI), so either works.
    public Task<List<string>> GetAvailableDevicesAsync() => _cloud.GetAvailableDevicesAsync();

    public async Task StartAsync(string? deviceName = null)
    {
        string? token = null;
        try { token = await _tokens.GetTokenAsync().ConfigureAwait(false); }
        catch (Exception ex) { Log.Warning(ex, "STT token probe failed; using offline engine"); }

        _current = !string.IsNullOrWhiteSpace(token) ? _cloud : _offline;
        Log.Information("STT engine selected: {Engine}",
            _current == _cloud ? "Deepgram (cloud)" : "Vosk (offline)");

        _active.OnNext(_current);
        await _current.StartAsync(deviceName).ConfigureAwait(false);
    }

    public Task StopAsync() => _current?.StopAsync() ?? Task.CompletedTask;

    public void Dispose()
    {
        _cloud.Dispose();
        _offline.Dispose();
        _active.Dispose();
    }
}
