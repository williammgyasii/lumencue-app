using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using ChurchProjection.Core.Services;
using NAudio.Wave;
using Serilog;

namespace ChurchProjection.Infrastructure.Audio;

public sealed class ElevenLabsTranscriptionService : ITranscriptionService
{
    private readonly ISttTokenProvider? _tokenProvider;
    private readonly string? _apiKey;
    private readonly double _minConfidence;
    private volatile float _inputGain;
    private readonly bool _vadGate;
    private readonly double _vadRmsThreshold;
    private const int PreRollMs = 300;
    private const int HangoverMs = 1500;
    private readonly Queue<(byte[] Pcm, int DurMs)> _preRoll = new();
    private int _preRollMs;
    private bool _sending;
    private DateTimeOffset _lastSpeechUtc = DateTimeOffset.MinValue;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _runCts;
    private IMicrophoneCapture? _capture;
    private WaveFormat? _captureFormat;
    private readonly SemaphoreSlim _startStopLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private readonly List<string> _transcriptHistory = [];
    private const int MaxTranscriptLines = 80;
    private const int FallbackSampleRate = 16000;
    private int _captureSampleRate = FallbackSampleRate;
    private int _streamSampleRate = FallbackSampleRate;

    private readonly BehaviorSubject<bool> _isListening = new(false);
    private readonly BehaviorSubject<string> _statusMessage = new("Idle");
    private readonly BehaviorSubject<float> _audioLevel = new(0f);
    private readonly Subject<TranscriptionSegment> _segments = new();
    private readonly BehaviorSubject<string> _rollingTranscript = new("");
    private readonly Subject<string> _interim = new();

    private string _lastPartial = "";
    private string? _currentDeviceName;
    private Timer? _watchdogTimer;
    private DateTimeOffset _lastTranscriptTime = DateTimeOffset.UtcNow;
    private int _reconnectCount;
    private volatile bool _isReconnecting;
    private volatile bool _connectionAlive;

    private const int WatchdogIntervalSec = 15;
    private const int StaleThresholdSec = 45;
    private const int MaxAutoReconnects = 50;

    public IObservable<TranscriptionSegment> Segments => _segments.AsObservable();
    public IObservable<string> RollingTranscript => _rollingTranscript.AsObservable();
    public IObservable<string> InterimTranscript => _interim.AsObservable();
    public IObservable<bool> IsListening => _isListening.AsObservable();
    public IObservable<string> StatusMessage => _statusMessage.AsObservable();
    public IObservable<float> AudioLevel => _audioLevel.AsObservable();
    public IObservable<string> EngineName => Observable.Return(SttOperatorCopy.EngineLabel);
    public bool IsRunning => _isListening.Value;

    public float InputGain
    {
        get => _inputGain;
        set => _inputGain = (float)Math.Clamp(value, 1.0, 20.0);
    }

    public ElevenLabsTranscriptionService(ISttTokenProvider tokenProvider, double minConfidence = 0.5,
        double inputGain = 1.0, bool vadGate = true, double vadThreshold = 0.01, string? apiKey = null)
        : this(tokenProvider, apiKey, minConfidence, inputGain, vadGate, vadThreshold)
    {
    }

    public ElevenLabsTranscriptionService(string apiKey, double minConfidence = 0.5,
        double inputGain = 1.0, bool vadGate = true, double vadThreshold = 0.01)
        : this(tokenProvider: null, apiKey, minConfidence, inputGain, vadGate, vadThreshold)
    {
    }

    private ElevenLabsTranscriptionService(ISttTokenProvider? tokenProvider, string? apiKey,
        double minConfidence, double inputGain, bool vadGate, double vadThreshold)
    {
        _tokenProvider = tokenProvider;
        _apiKey = apiKey;
        _minConfidence = Math.Clamp(minConfidence, 0.0, 1.0);
        InputGain = (float)inputGain;
        _vadGate = vadGate;
        _vadRmsThreshold = Math.Clamp(vadThreshold, 0.0, 1.0);
    }

    public Task<List<string>> GetAvailableDevicesAsync()
        => Task.FromResult(MicrophoneCaptureFactory.ListInputDevices());

    public async Task StartAsync(string? deviceName = null)
    {
        await _startStopLock.WaitAsync();
        try
        {
            if (_isListening.Value) return;
            _currentDeviceName = deviceName;
            _reconnectCount = 0;
            await ConnectAndStartAsync(deviceName);
        }
        finally
        {
            _startStopLock.Release();
        }
    }

    private async Task ConnectAndStartAsync(string? deviceName)
    {
        _statusMessage.OnNext(SttOperatorCopy.Connecting);
        _connectionAlive = false;

        PrepareCapture(deviceName);
        var options = ElevenLabsScribeOptions.Create(_captureSampleRate);
        _streamSampleRate = options.SampleRate;

        string? singleUseToken = null;
        if (_tokenProvider is not null)
            singleUseToken = await _tokenProvider.GetTokenAsync().ConfigureAwait(false);

        var auth = SttAuthPolicy.Resolve(singleUseToken, _apiKey);
        if (auth == SttAuthMode.Unavailable)
        {
            Log.Error("No ElevenLabs Scribe token or local key available");
            _statusMessage.OnNext("Speech token unavailable");
            return;
        }

        if (auth == SttAuthMode.LocalKey)
        {
            Log.Warning("STT: cloud token mint failed; using local ElevenLabs key");
            singleUseToken = null;
        }

        var ws = new ClientWebSocket();
        if (singleUseToken is null)
            ws.Options.SetRequestHeader("xi-api-key", _apiKey);
        var cts = new CancellationTokenSource();
        _runCts = cts;
        _ws = ws;

        try
        {
            await ws.ConnectAsync(options.BuildWebSocketUri(singleUseToken), cts.Token);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to connect to ElevenLabs Scribe");
            _statusMessage.OnNext("Connection failed");
            await TeardownSocketAsync();
            return;
        }

        _connectionAlive = true;
        _lastTranscriptTime = DateTimeOffset.UtcNow;
        _ = Task.Run(() => ReceiveLoopAsync(ws, cts.Token));
        BeginCapture();
        _isListening.OnNext(true);
        StartWatchdog();
        _statusMessage.OnNext("Listening...");
        Log.Information("ElevenLabs Scribe started (reconnect #{Count}, {Format})",
            _reconnectCount, options.AudioFormat);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var text = new StringBuilder();
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                HandleMessage(text.ToString());
                text.Clear();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "ElevenLabs receive loop ended");
        }

        _connectionAlive = false;
        if (_isListening.Value && !ct.IsCancellationRequested && !_isReconnecting)
        {
            _statusMessage.OnNext("Disconnected, reconnecting...");
            _ = Task.Run(AttemptReconnectAsync);
        }
    }

    private void HandleMessage(string json)
    {
        ElevenLabsScribeMessage parsed;
        try { parsed = ElevenLabsScribeMessage.Parse(json); }
        catch (Exception ex)
        {
            Log.Debug(ex, "ElevenLabs message parse failed");
            return;
        }

        _lastTranscriptTime = DateTimeOffset.UtcNow;
        _connectionAlive = true;

        switch (parsed.Kind)
        {
            case ElevenLabsScribeKind.Interim when !string.IsNullOrWhiteSpace(parsed.Text):
                if (parsed.Text != _lastPartial)
                {
                    _lastPartial = parsed.Text;
                    UpdateLiveTranscript(parsed.Text);
                    _interim.OnNext(parsed.Text);
                }
                break;
            case ElevenLabsScribeKind.Final when !string.IsNullOrWhiteSpace(parsed.Text):
                // Scribe committed text has no 0..1 confidence; treat as 1 so the noise gate
                // does not drop real speech. Room noise is still blocked by local VAD.
                const float confidence = 1f;
                if (_minConfidence > 0 && confidence < _minConfidence) return;
                _segments.OnNext(new TranscriptionSegment(parsed.Text, DateTimeOffset.UtcNow, confidence));
                AppendToTranscript(parsed.Text);
                Log.Debug("ElevenLabs final: {Text}", parsed.Text);
                break;
            case ElevenLabsScribeKind.Error:
                Log.Error("ElevenLabs Scribe error: {Error}", parsed.Error);
                _statusMessage.OnNext(SttOperatorCopy.TranscriptionError);
                if (string.Equals(parsed.Error, "invalid key", StringComparison.OrdinalIgnoreCase)
                    || json.Contains("auth_error", StringComparison.Ordinal))
                {
                    _ = StopAsync();
                }
                break;
        }
    }

    private void PrepareCapture(string? deviceName)
    {
        CleanupCapture();
        try
        {
            _capture = MicrophoneCaptureFactory.Open(deviceName);
            if (_capture is null)
            {
                _statusMessage.OnNext("Audio device error");
                _captureSampleRate = FallbackSampleRate;
                return;
            }

            _captureFormat = _capture.WaveFormat;
            _captureSampleRate = _captureFormat.SampleRate > 0 ? _captureFormat.SampleRate : FallbackSampleRate;
            Log.Information("Audio: @ {Rate}Hz {Bits}bit {Ch}ch (ElevenLabs)",
                _captureFormat.SampleRate, _captureFormat.BitsPerSample, _captureFormat.Channels);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize audio capture");
            _statusMessage.OnNext("Audio device error");
            CleanupCapture();
            _captureSampleRate = FallbackSampleRate;
        }
    }

    private void BeginCapture()
    {
        if (_capture is null) return;
        try
        {
            _preRoll.Clear();
            _preRollMs = 0;
            _sending = false;
            _lastSpeechUtc = DateTimeOffset.MinValue;
            _capture.DataAvailable += OnDataAvailable;
            _capture.StartRecording();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start audio capture");
            _statusMessage.OnNext("Audio device error");
            CleanupCapture();
        }
    }

    private void StartWatchdog()
    {
        _watchdogTimer?.Dispose();
        _watchdogTimer = new Timer(WatchdogTick, null,
            TimeSpan.FromSeconds(WatchdogIntervalSec),
            TimeSpan.FromSeconds(WatchdogIntervalSec));
    }

    private void WatchdogTick(object? state)
    {
        if (!_isListening.Value || _isReconnecting) return;
        var elapsed = DateTimeOffset.UtcNow - _lastTranscriptTime;
        if (elapsed.TotalSeconds > StaleThresholdSec && _connectionAlive)
        {
            Log.Warning("ElevenLabs connection stale ({Sec}s), reconnecting...", (int)elapsed.TotalSeconds);
            _connectionAlive = false;
            _statusMessage.OnNext("Connection stale, reconnecting...");
            _ = Task.Run(AttemptReconnectAsync);
        }
    }

    private async Task AttemptReconnectAsync()
    {
        if (_isReconnecting) return;
        _isReconnecting = true;
        try
        {
            if (_reconnectCount >= MaxAutoReconnects)
            {
                _statusMessage.OnNext("Connection lost (max retries)");
                await StopInternalAsync();
                return;
            }

            _reconnectCount++;
            var delaySec = Math.Min(2 * _reconnectCount, 10);
            _statusMessage.OnNext($"Reconnecting ({_reconnectCount})...");
            await Task.Delay(TimeSpan.FromSeconds(delaySec));
            if (!_isListening.Value) return;

            await TeardownSocketAsync();
            CleanupCapture();
            await ConnectAndStartAsync(_currentDeviceName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ElevenLabs reconnect failed");
            _isReconnecting = false;
            if (_isListening.Value)
                _ = Task.Run(AttemptReconnectAsync);
        }
        finally
        {
            _isReconnecting = false;
        }
    }

    public async Task StopAsync()
    {
        await _startStopLock.WaitAsync();
        try { await StopInternalAsync(); }
        finally { _startStopLock.Release(); }
    }

    private async Task StopInternalAsync()
    {
        if (!_isListening.Value) return;
        _isListening.OnNext(false);
        _watchdogTimer?.Dispose();
        _watchdogTimer = null;
        CleanupCapture();
        await TeardownSocketAsync();
        _statusMessage.OnNext("Stopped");
        Log.Information("ElevenLabs Scribe stopped");
    }

    private async Task TeardownSocketAsync()
    {
        _runCts?.Cancel();
        var ws = _ws;
        _ws = null;
        if (ws is not null)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "stop", CancellationToken.None);
            }
            catch { }
            ws.Dispose();
        }
        _runCts?.Dispose();
        _runCts = null;
    }

    private void CleanupCapture()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            try { _capture.StopRecording(); } catch { }
            _capture.Dispose();
            _capture = null;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_captureFormat is null || e.BytesRecorded == 0) return;

        var pcmBytes = ConvertToMono16Pcm(e.Buffer, e.BytesRecorded, _captureFormat);
        if (pcmBytes.Length == 0) return;
        UpdateAudioLevel(pcmBytes);
        if (_ws is null || _ws.State != WebSocketState.Open) return;

        if (!_vadGate || _vadRmsThreshold <= 0)
        {
            SendAudio(pcmBytes);
            return;
        }

        double rms = RawRms(e.Buffer, e.BytesRecorded, _captureFormat);
        int durMs = _captureSampleRate > 0 ? (int)(1000L * (pcmBytes.Length / 2) / _captureSampleRate) : 0;
        var now = DateTimeOffset.UtcNow;
        if (rms >= _vadRmsThreshold) _lastSpeechUtc = now;
        bool active = (now - _lastSpeechUtc).TotalMilliseconds <= HangoverMs;

        if (active)
        {
            if (!_sending)
            {
                while (_preRoll.Count > 0)
                    SendAudio(_preRoll.Dequeue().Pcm);
                _preRollMs = 0;
                _sending = true;
            }
            SendAudio(pcmBytes);
        }
        else
        {
            _sending = false;
            _preRoll.Enqueue((pcmBytes, durMs));
            _preRollMs += durMs;
            while (_preRollMs > PreRollMs && _preRoll.Count > 0)
                _preRollMs -= _preRoll.Dequeue().DurMs;
        }
    }

    private void SendAudio(byte[] pcmBytes)
    {
        var ws = _ws;
        if (ws is null || ws.State != WebSocketState.Open) return;
        var json = ElevenLabsScribeMessage.EncodeAudioChunk(pcmBytes, _streamSampleRate);
        var bytes = Encoding.UTF8.GetBytes(json);
        _ = SendAsync(ws, bytes);
    }

    private async Task SendAsync(ClientWebSocket ws, byte[] bytes)
    {
        await _sendLock.WaitAsync();
        try
        {
            if (ws.State == WebSocketState.Open)
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to send audio to ElevenLabs");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static double RawRms(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
    {
        int bytesPerSample = sourceFormat.BitsPerSample / 8;
        if (bytesPerSample == 0 || sourceFormat.Channels == 0) return 0;
        int frameStride = bytesPerSample * sourceFormat.Channels;
        int n = bytesRecorded / frameStride;
        if (n == 0) return 0;
        double sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            int offset = i * frameStride;
            if (offset + bytesPerSample > bytesRecorded) break;
            float s = bytesPerSample == 4
                ? BitConverter.ToSingle(buffer, offset)
                : bytesPerSample == 2 ? BitConverter.ToInt16(buffer, offset) / 32768f : 0f;
            sumSq += s * (double)s;
        }
        return Math.Sqrt(sumSq / n);
    }

    private void UpdateAudioLevel(byte[] pcmBytes)
    {
        float peak = 0;
        for (int i = 0; i + 1 < pcmBytes.Length; i += 2)
        {
            var sample = Math.Abs(BitConverter.ToInt16(pcmBytes, i) / 32768f);
            if (sample > peak) peak = sample;
        }
        _audioLevel.OnNext(peak);
    }

    private void UpdateLiveTranscript(string partialText)
    {
        var history = string.Join(" ", _transcriptHistory);
        _rollingTranscript.OnNext(_transcriptHistory.Count > 0 ? $"{history} {partialText}" : partialText);
    }

    private void AppendToTranscript(string text)
    {
        _transcriptHistory.Add(text);
        while (_transcriptHistory.Count > MaxTranscriptLines)
            _transcriptHistory.RemoveAt(0);
        _lastPartial = "";
        _rollingTranscript.OnNext(string.Join(" ", _transcriptHistory));
    }

    private byte[] ConvertToMono16Pcm(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
    {
        int bytesPerSample = sourceFormat.BitsPerSample / 8;
        int sampleCount = bytesRecorded / (bytesPerSample * sourceFormat.Channels);
        if (sampleCount == 0) return [];

        var pcm = new byte[sampleCount * 2];
        for (int i = 0; i < sampleCount; i++)
        {
            int byteIndex = i * bytesPerSample * sourceFormat.Channels;
            float sum = 0;
            for (int ch = 0; ch < sourceFormat.Channels; ch++)
            {
                int offset = byteIndex + ch * bytesPerSample;
                if (offset + bytesPerSample > bytesRecorded) break;
                if (bytesPerSample == 4)
                    sum += BitConverter.ToSingle(buffer, offset);
                else if (bytesPerSample == 2)
                    sum += BitConverter.ToInt16(buffer, offset) / 32768f;
            }

            var limited = SoftLimit((sum / sourceFormat.Channels) * _inputGain);
            var sample16 = (short)(limited * 32767);
            pcm[i * 2] = (byte)(sample16 & 0xFF);
            pcm[i * 2 + 1] = (byte)((sample16 >> 8) & 0xFF);
        }
        return pcm;
    }

    private const float LimiterKnee = 0.8f;

    private static float SoftLimit(float x)
    {
        float a = MathF.Abs(x);
        if (a <= LimiterKnee) return x;
        float over = (a - LimiterKnee) / (1f - LimiterKnee);
        float shaped = LimiterKnee + (1f - LimiterKnee) * MathF.Tanh(over);
        return MathF.Sign(x) * shaped;
    }

    public void Dispose()
    {
        _watchdogTimer?.Dispose();
        CleanupCapture();
        try { TeardownSocketAsync().GetAwaiter().GetResult(); } catch { }
        _segments.Dispose();
        _interim.Dispose();
        _startStopLock.Dispose();
        _sendLock.Dispose();
    }
}
