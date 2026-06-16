using System.Reactive.Linq;
using System.Reactive.Subjects;
using ChurchProjection.Core.Services;
using Deepgram;
using Deepgram.Clients.Interfaces.v2;
using Deepgram.Models.Authenticate.v1;
using Deepgram.Models.Listen.v2.WebSocket;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Serilog;

namespace ChurchProjection.Infrastructure.Audio;

public class DeepgramTranscriptionService : ITranscriptionService
{
    private readonly ISttTokenProvider _tokenProvider;
    // Finals below this Deepgram confidence are treated as room noise / music bleed and dropped before
    // they reach the matcher, so a noisy sanctuary can't spawn phantom scripture/song suggestions.
    // 0 disables the gate. Tuned conservatively: clear speech scores ~0.9+, garbled noise far lower.
    private readonly double _minConfidence;
    private IListenWebSocketClient? _wsClient;
    private WasapiCapture? _capture;
    private WaveFormat? _captureFormat;
    private readonly SemaphoreSlim _startStopLock = new(1, 1);

    private readonly List<string> _transcriptHistory = [];
    private const int MaxTranscriptLines = 80;
    private const int FallbackSampleRate = 16000;

    // Sample rate we actually stream to Deepgram. Set to the capture device's native rate so we
    // never run audio through a lossy downsample before recognition.
    private int _captureSampleRate = FallbackSampleRate;

    private readonly BehaviorSubject<bool> _isListening = new(false);
    private readonly BehaviorSubject<string> _statusMessage = new("Idle");
    private readonly BehaviorSubject<float> _audioLevel = new(0f);
    private readonly Subject<TranscriptionSegment> _segments = new();
    private readonly BehaviorSubject<string> _rollingTranscript = new("");

    private string _lastPartial = "";
    private string? _currentDeviceName;
    private Timer? _watchdogTimer;
    private Timer? _keepAliveTimer;
    private DateTimeOffset _lastTranscriptTime = DateTimeOffset.UtcNow;
    private int _reconnectCount;
    private volatile bool _isReconnecting;
    private volatile bool _connectionAlive;

    private const int WatchdogIntervalSec = 15;
    private const int StaleThresholdSec = 45;
    private const int KeepAliveIntervalSec = 8;
    private const int MaxAutoReconnects = 50;

    public IObservable<TranscriptionSegment> Segments => _segments.AsObservable();
    public IObservable<string> RollingTranscript => _rollingTranscript.AsObservable();
    public IObservable<bool> IsListening => _isListening.AsObservable();
    public IObservable<string> StatusMessage => _statusMessage.AsObservable();
    public IObservable<float> AudioLevel => _audioLevel.AsObservable();
    public bool IsRunning => _isListening.Value;

    public DeepgramTranscriptionService(ISttTokenProvider tokenProvider, double minConfidence = 0.5)
    {
        _tokenProvider = tokenProvider;
        _minConfidence = Math.Clamp(minConfidence, 0.0, 1.0);
    }

    public Task<List<string>> GetAvailableDevicesAsync()
    {
        var devices = new List<string>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in endpoints)
            {
                devices.Add(device.FriendlyName);
                device.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to enumerate audio devices");
        }
        return Task.FromResult(devices);
    }

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
        _statusMessage.OnNext("Connecting to Deepgram...");
        _connectionAlive = false;

        // Fetch a fresh short-lived JWT for this connection. The token only needs to be valid at
        // connect time; the socket then stays open for the whole session. A new one is fetched on
        // every (re)connect, so we never depend on a stale credential.
        var token = await _tokenProvider.GetTokenAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            Log.Error("No Deepgram access token available");
            _statusMessage.OnNext("Speech token unavailable");
            return;
        }

        Deepgram.Library.Initialize();

        var options = new DeepgramWsClientOptions(apiKey: null, baseAddress: null, keepAlive: true, accessToken: token);
        _wsClient = ClientFactory.CreateListenWebSocketClient("", options);

        _ = _wsClient.Subscribe(new EventHandler<ResultResponse>((_, e) =>
        {
            _lastTranscriptTime = DateTimeOffset.UtcNow;
            _connectionAlive = true;

            var alternative = e.Channel?.Alternatives?.FirstOrDefault();
            var transcript = alternative?.Transcript;
            if (string.IsNullOrWhiteSpace(transcript)) return;

            if (e.IsFinal == true)
            {
                // Deepgram reports a 0..1 confidence per final; default to 1 if the SDK omits it.
                var confidence = alternative?.Confidence is { } c and > 0 ? (float)c : 1f;

                // Gate noise: drop low-confidence finals so music/crowd bleed never reaches matching.
                if (_minConfidence > 0 && confidence < _minConfidence)
                {
                    Log.Debug("Deepgram final dropped (low confidence {Confidence:P0} < {Min:P0}): {Text}",
                        confidence, _minConfidence, transcript);
                    return;
                }

                var segment = new TranscriptionSegment(transcript, DateTimeOffset.UtcNow, confidence);
                _segments.OnNext(segment);
                AppendToTranscript(transcript);
                Log.Debug("Deepgram final [{Confidence:P0}]: {Text}", confidence, transcript);
            }
            else
            {
                if (transcript != _lastPartial)
                {
                    _lastPartial = transcript;
                    UpdateLiveTranscript(transcript);
                }
            }
        }));

        _ = _wsClient.Subscribe(new EventHandler<OpenResponse>((_, e) =>
        {
            Log.Information("Deepgram WebSocket connected");
            _connectionAlive = true;
            _lastTranscriptTime = DateTimeOffset.UtcNow;
            _statusMessage.OnNext("Listening...");
        }));

        _ = _wsClient.Subscribe(new EventHandler<CloseResponse>((_, e) =>
        {
            Log.Warning("Deepgram WebSocket closed unexpectedly");
            _connectionAlive = false;

            if (_isListening.Value && !_isReconnecting)
            {
                _statusMessage.OnNext("Connection lost, reconnecting...");
                _ = Task.Run(() => AttemptReconnectAsync());
            }
        }));

        _ = _wsClient.Subscribe(new EventHandler<ErrorResponse>((_, e) =>
        {
            Log.Error("Deepgram error: {Message}", e.Message);
            _connectionAlive = false;

            if (_isListening.Value && !_isReconnecting)
            {
                _statusMessage.OnNext("Error, reconnecting...");
                _ = Task.Run(() => AttemptReconnectAsync());
            }
        }));

        // Prepare the capture device first so the schema can advertise the device's native sample
        // rate — sending native-rate audio (no resampling) gives Deepgram the cleanest signal.
        PrepareCapture(deviceName);

        var liveSchema = new LiveSchema()
        {
            Model = "nova-3",
            Encoding = "linear16",
            SampleRate = _captureSampleRate,
            Channels = 1,
            Language = "en",
            Punctuate = true,
            SmartFormat = true,
            Numerals = true,
            InterimResults = true,
            UtteranceEnd = "1500",
            VadEvents = true,
            EndPointing = "400",
            FillerWords = false,
            NoDelay = true,
            Keyterm = [
                "Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy",
                "Joshua", "Judges", "Ruth", "Samuel", "Kings", "Chronicles",
                "Ezra", "Nehemiah", "Esther", "Job", "Psalms", "Psalm",
                "Proverbs", "Ecclesiastes", "Song of Solomon", "Isaiah",
                "Jeremiah", "Lamentations", "Ezekiel", "Daniel", "Hosea",
                "Joel", "Amos", "Obadiah", "Jonah", "Micah", "Nahum",
                "Habakkuk", "Zephaniah", "Haggai", "Zechariah", "Malachi",
                "Matthew", "Mark", "Luke", "John", "Acts", "Romans",
                "Corinthians", "Galatians", "Ephesians", "Philippians",
                "Colossians", "Thessalonians", "Timothy", "Titus", "Philemon",
                "Hebrews", "James", "Peter", "Jude", "Revelation",
                "First", "Second", "Third", "chapter", "verse", "verses",
                "scripture", "passage", "gospel", "epistle", "parable",
                "amen", "hallelujah", "Jesus", "Christ", "God", "Holy Spirit",
                "Lord", "covenant", "righteousness", "salvation", "grace",
                "disciples", "apostle", "prophet", "Messiah", "Yahweh",
            ],
        };

        bool connected = await _wsClient.Connect(liveSchema);
        if (!connected)
        {
            Log.Error("Failed to connect to Deepgram");
            _statusMessage.OnNext("Connection failed");
            return;
        }

        BeginCapture();

        _lastTranscriptTime = DateTimeOffset.UtcNow;
        _isListening.OnNext(true);
        StartWatchdog();
        StartKeepAlive();

        Log.Information("Deepgram transcription started (reconnect #{Count})", _reconnectCount);
    }

    private void PrepareCapture(string? deviceName)
    {
        CleanupCapture();

        MMDevice? selectedDevice = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (deviceName is not null)
            {
                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                selectedDevice = endpoints.FirstOrDefault(d =>
                    d.FriendlyName.Contains(deviceName, StringComparison.OrdinalIgnoreCase));
            }
            selectedDevice ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

            _capture = new WasapiCapture(selectedDevice);
            _captureFormat = _capture.WaveFormat;
            _captureSampleRate = _captureFormat.SampleRate > 0 ? _captureFormat.SampleRate : FallbackSampleRate;

            Log.Information("Audio: {Name} @ {Rate}Hz {Bits}bit {Ch}ch (streaming native rate)",
                selectedDevice.FriendlyName, _captureFormat.SampleRate,
                _captureFormat.BitsPerSample, _captureFormat.Channels);
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
            Log.Warning("Deepgram connection appears stale ({Sec}s since last transcript), reconnecting...",
                (int)elapsed.TotalSeconds);
            _connectionAlive = false;
            _statusMessage.OnNext("Connection stale, reconnecting...");
            _ = Task.Run(() => AttemptReconnectAsync());
        }
    }

    private void StartKeepAlive()
    {
        _keepAliveTimer?.Dispose();
        _keepAliveTimer = new Timer(KeepAliveTick, null,
            TimeSpan.FromSeconds(KeepAliveIntervalSec),
            TimeSpan.FromSeconds(KeepAliveIntervalSec));
    }

    private void KeepAliveTick(object? state)
    {
        if (!_isListening.Value || _wsClient is null || !_connectionAlive) return;

        try
        {
            _wsClient.SendKeepAlive();
        }
        catch (Exception ex)
        {
            Log.Debug("KeepAlive send failed: {Message}", ex.Message);
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
                Log.Error("Max reconnect attempts ({Max}) reached, stopping", MaxAutoReconnects);
                _statusMessage.OnNext("Connection lost (max retries)");
                await StopInternalAsync();
                return;
            }

            _reconnectCount++;
            var delaySec = Math.Min(2 * _reconnectCount, 10);
            Log.Information("Reconnect attempt #{Count} in {Delay}s...", _reconnectCount, delaySec);
            _statusMessage.OnNext($"Reconnecting ({_reconnectCount})...");

            await Task.Delay(TimeSpan.FromSeconds(delaySec));

            if (!_isListening.Value) return;

            TeardownConnection();
            await ConnectAndStartAsync(_currentDeviceName);

            _statusMessage.OnNext("Listening...");
            Log.Information("Reconnected successfully (attempt #{Count})", _reconnectCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Reconnect attempt #{Count} failed", _reconnectCount);
            _statusMessage.OnNext("Reconnect failed, retrying...");

            _isReconnecting = false;
            if (_isListening.Value)
                _ = Task.Run(() => AttemptReconnectAsync());
        }
        finally
        {
            _isReconnecting = false;
        }
    }

    private void TeardownConnection()
    {
        _watchdogTimer?.Dispose();
        _watchdogTimer = null;
        _keepAliveTimer?.Dispose();
        _keepAliveTimer = null;
        CleanupCapture();

        if (_wsClient is not null)
        {
            try { _wsClient.Stop().GetAwaiter().GetResult(); }
            catch { }
            _wsClient = null;
        }

        try { Deepgram.Library.Terminate(); }
        catch { }
    }

    public async Task StopAsync()
    {
        await _startStopLock.WaitAsync();
        try
        {
            await StopInternalAsync();
        }
        finally
        {
            _startStopLock.Release();
        }
    }

    private async Task StopInternalAsync()
    {
        if (!_isListening.Value) return;

        _isListening.OnNext(false);

        _watchdogTimer?.Dispose();
        _watchdogTimer = null;
        _keepAliveTimer?.Dispose();
        _keepAliveTimer = null;

        CleanupCapture();

        if (_wsClient is not null)
        {
            try { await _wsClient.Stop(); }
            catch (Exception ex) { Log.Warning(ex, "Error stopping Deepgram WebSocket"); }
            _wsClient = null;
        }

        try { Deepgram.Library.Terminate(); }
        catch { }

        _statusMessage.OnNext("Stopped");
        Log.Information("Deepgram transcription stopped");
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
        if (_captureFormat is null || e.BytesRecorded == 0 || _wsClient is null) return;

        var pcmBytes = ConvertToMono16Pcm(e.Buffer, e.BytesRecorded, _captureFormat);
        if (pcmBytes.Length == 0) return;

        UpdateAudioLevel(pcmBytes);

        try
        {
            _wsClient.Send(pcmBytes);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to send audio to Deepgram");
        }
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
        var display = _transcriptHistory.Count > 0
            ? $"{history} {partialText}"
            : partialText;
        _rollingTranscript.OnNext(display);
    }

    private void AppendToTranscript(string text)
    {
        _transcriptHistory.Add(text);
        while (_transcriptHistory.Count > MaxTranscriptLines)
            _transcriptHistory.RemoveAt(0);

        _lastPartial = "";
        _rollingTranscript.OnNext(string.Join(" ", _transcriptHistory));
    }

    // Down-mixes to mono and converts to 16-bit PCM at the source's native sample rate. No rate
    // conversion is performed; the native rate is advertised to Deepgram so no signal is lost.
    private static byte[] ConvertToMono16Pcm(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
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

            var clamped = Math.Clamp(sum / sourceFormat.Channels, -1f, 1f);
            var sample16 = (short)(clamped * 32767);
            pcm[i * 2] = (byte)(sample16 & 0xFF);
            pcm[i * 2 + 1] = (byte)((sample16 >> 8) & 0xFF);
        }
        return pcm;
    }

    public void Dispose()
    {
        _watchdogTimer?.Dispose();
        _keepAliveTimer?.Dispose();
        CleanupCapture();
        try { _wsClient?.Stop().GetAwaiter().GetResult(); } catch { }
        _wsClient = null;
        try { Deepgram.Library.Terminate(); } catch { }
        _segments.Dispose();
        _startStopLock.Dispose();
    }
}
