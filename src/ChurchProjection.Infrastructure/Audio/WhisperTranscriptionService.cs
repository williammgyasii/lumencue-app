using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ChurchProjection.Core.Services;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Serilog;
using Whisper.net;
using Whisper.net.Ggml;

namespace ChurchProjection.Infrastructure.Audio;

public class WhisperTranscriptionService : ITranscriptionService
{
    private WhisperProcessor? _processor;
    private WasapiCapture? _capture;
    private WaveFormat? _captureFormat;
    private CancellationTokenSource? _cts;
    private Task? _processingTask;

    private readonly ConcurrentQueue<float[]> _audioQueue = new();
    private readonly List<float> _audioBuffer = [];
    private readonly List<string> _transcriptHistory = [];
    private readonly object _bufferLock = new();
    private readonly SemaphoreSlim _startStopLock = new(1, 1);

    private const int WhisperSampleRate = 16000;
    private const int ChunkSeconds = 10;
    private const int OverlapSeconds = 2;
    private const int MaxQueueDepth = 6;
    private const int MaxTranscriptLines = 50;
    private const float SilenceThreshold = 0.008f;

    private string _lastTranscription = "";

    private readonly BehaviorSubject<bool> _isListening = new(false);
    private readonly BehaviorSubject<string> _statusMessage = new("Idle");
    private readonly BehaviorSubject<float> _audioLevel = new(0f);
    private readonly Subject<TranscriptionSegment> _segments = new();
    private readonly BehaviorSubject<string> _rollingTranscript = new("");

    public IObservable<TranscriptionSegment> Segments => _segments.AsObservable();
    public IObservable<string> RollingTranscript => _rollingTranscript.AsObservable();
    public IObservable<bool> IsListening => _isListening.AsObservable();
    public IObservable<string> StatusMessage => _statusMessage.AsObservable();
    public IObservable<float> AudioLevel => _audioLevel.AsObservable();
    public bool IsRunning => _isListening.Value;

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

            await EnsureModelAsync();
            _statusMessage.OnNext("Initializing audio...");

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
                _capture.DataAvailable += OnDataAvailable;

                Log.Information("Audio: {Name} @ {Rate}Hz {Bits}bit {Ch}ch",
                    selectedDevice.FriendlyName, _captureFormat.SampleRate,
                    _captureFormat.BitsPerSample, _captureFormat.Channels);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize audio capture");
                _statusMessage.OnNext("Audio device error");
                CleanupCapture();
                return;
            }

            ClearBuffers();
            _cts = new CancellationTokenSource();
            _processingTask = Task.Run(() => ProcessAudioLoop(_cts.Token));

            _capture.StartRecording();
            _isListening.OnNext(true);
            _statusMessage.OnNext("Listening...");
            Log.Information("Transcription started");
        }
        finally
        {
            _startStopLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _startStopLock.WaitAsync();
        try
        {
            if (!_isListening.Value) return;

            CleanupCapture();

            _cts?.Cancel();
            if (_processingTask is not null)
            {
                try { await _processingTask; }
                catch (OperationCanceledException) { }
            }
            _cts?.Dispose();
            _cts = null;
            _processingTask = null;

            _isListening.OnNext(false);
            _statusMessage.OnNext("Stopped");
            Log.Information("Transcription stopped");
        }
        finally
        {
            _startStopLock.Release();
        }
    }

    private void CleanupCapture()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            try { _capture.StopRecording(); } catch { /* device may already be gone */ }
            _capture.Dispose();
            _capture = null;
        }
    }

    private void ClearBuffers()
    {
        lock (_bufferLock) { _audioBuffer.Clear(); }
        while (_audioQueue.TryDequeue(out _)) { }
        _lastTranscription = "";
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_captureFormat is null || e.BytesRecorded == 0) return;

        var monoSamples = ConvertToMono16k(e.Buffer, e.BytesRecorded, _captureFormat);

        float peak = 0;
        for (int i = 0; i < monoSamples.Length; i++)
            peak = Math.Max(peak, Math.Abs(monoSamples[i]));
        _audioLevel.OnNext(peak);

        lock (_bufferLock)
        {
            _audioBuffer.AddRange(monoSamples);

            int chunkSize = WhisperSampleRate * ChunkSeconds;
            if (_audioBuffer.Count >= chunkSize)
            {
                var chunk = _audioBuffer.GetRange(0, chunkSize).ToArray();
                int keepFrom = chunkSize - WhisperSampleRate * OverlapSeconds;
                _audioBuffer.RemoveRange(0, keepFrom);

                if (_audioQueue.Count < MaxQueueDepth)
                {
                    _audioQueue.Enqueue(chunk);
                }
                else
                {
                    Log.Warning("Audio queue full ({Max}), dropping chunk", MaxQueueDepth);
                }
            }
        }
    }

    private static float[] ConvertToMono16k(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
    {
        int bytesPerSample = sourceFormat.BitsPerSample / 8;
        int sampleCount = bytesRecorded / (bytesPerSample * sourceFormat.Channels);

        var mono = new float[sampleCount];
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
            mono[i] = sum / sourceFormat.Channels;
        }

        if (sourceFormat.SampleRate == WhisperSampleRate) return mono;

        double ratio = (double)WhisperSampleRate / sourceFormat.SampleRate;
        int newLength = (int)(mono.Length * ratio);
        var resampled = new float[newLength];
        for (int i = 0; i < newLength; i++)
        {
            double srcIndex = i / ratio;
            int idx = (int)srcIndex;
            double frac = srcIndex - idx;
            resampled[i] = idx + 1 < mono.Length
                ? (float)(mono[idx] * (1 - frac) + mono[idx + 1] * frac)
                : mono[Math.Min(idx, mono.Length - 1)];
        }
        return resampled;
    }

    private async Task ProcessAudioLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_audioQueue.TryDequeue(out var chunk) && _processor is not null)
            {
                if (IsSilent(chunk))
                {
                    Log.Debug("Skipping silent chunk");
                    continue;
                }

                try
                {
                    using var memStream = new MemoryStream();
                    WriteWav(memStream, chunk, WhisperSampleRate);
                    memStream.Seek(0, SeekOrigin.Begin);

                    await foreach (var result in _processor.ProcessAsync(memStream, ct))
                    {
                        var text = result.Text?.Trim();
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        if (IsNoiseOrHallucination(text)) continue;
                        if (IsDuplicateOfLast(text)) continue;

                        _lastTranscription = text;
                        var segment = new TranscriptionSegment(
                            text, DateTimeOffset.UtcNow, result.Probability);

                        _segments.OnNext(segment);
                        AppendToTranscript(text);
                        Log.Debug("STT [{Confidence:P0}]: {Text}", result.Probability, text);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Error(ex, "Whisper processing error");
                }
            }
            else
            {
                try { await Task.Delay(200, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private static bool IsSilent(float[] samples)
    {
        float rms = 0;
        for (int i = 0; i < samples.Length; i++)
            rms += samples[i] * samples[i];
        rms = MathF.Sqrt(rms / samples.Length);
        return rms < SilenceThreshold;
    }

    private bool IsDuplicateOfLast(string text)
    {
        if (string.IsNullOrEmpty(_lastTranscription)) return false;

        var a = NormalizeForComparison(text);
        var b = NormalizeForComparison(_lastTranscription);

        if (a == b) return true;

        if (a.Length > 0 && b.Length > 0)
        {
            int common = LongestCommonSubstringLength(a, b);
            double overlap = (double)common / Math.Max(a.Length, b.Length);
            return overlap > 0.75;
        }

        return false;
    }

    private static string NormalizeForComparison(string text) =>
        text.ToLowerInvariant().Trim()
            .Replace(",", "").Replace(".", "").Replace("!", "").Replace("?", "");

    private static int LongestCommonSubstringLength(string a, string b)
    {
        if (a.Length > 200 || b.Length > 200)
        {
            var wordsA = a.Split(' ');
            var wordsB = new HashSet<string>(b.Split(' '));
            return wordsA.Count(w => wordsB.Contains(w));
        }

        int maxLen = 0;
        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                if (a[i - 1] == b[j - 1])
                {
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                    maxLen = Math.Max(maxLen, dp[i, j]);
                }
            }
        }
        return maxLen;
    }

    private static void WriteWav(Stream stream, float[] samples, int sampleRate)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        int bitsPerSample = 16;
        short channels = 1;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);
        int dataSize = samples.Length * blockAlign;

        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)bitsPerSample);

        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        foreach (var sample in samples)
        {
            var clamped = Math.Clamp(sample, -1f, 1f);
            writer.Write((short)(clamped * 32767));
        }
    }

    private static bool IsNoiseOrHallucination(string text)
    {
        var lower = text.ToLowerInvariant().Trim();

        if (lower.Length < 3) return true;
        if (lower.StartsWith('[') || lower.StartsWith('(')) return true;

        var hallucinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "you", "the", "uh", "um", "ah", "oh",
            "thank you", "thanks for watching",
            "subscribe", "like and subscribe",
            "thank you for watching",
            "thanks for listening",
            "please subscribe",
            "...", "..", "so", "and"
        };

        return hallucinations.Contains(lower);
    }

    private void AppendToTranscript(string text)
    {
        _transcriptHistory.Add(text);
        while (_transcriptHistory.Count > MaxTranscriptLines)
            _transcriptHistory.RemoveAt(0);

        _rollingTranscript.OnNext(string.Join(" ", _transcriptHistory));
    }

    private async Task EnsureModelAsync()
    {
        if (_processor is not null) return;

        _statusMessage.OnNext("Loading Whisper model...");

        var modelsDir = Path.Combine(AppContext.BaseDirectory, "models");
        Directory.CreateDirectory(modelsDir);
        var modelPath = Path.Combine(modelsDir, "ggml-small.bin");

        if (!File.Exists(modelPath))
        {
            Log.Information("Downloading Whisper small model (~466 MB)...");
            _statusMessage.OnNext("Downloading Whisper model (~466 MB, first run only)...");

            using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(GgmlType.Small);
            using var fs = File.Create(modelPath);
            await modelStream.CopyToAsync(fs);

            Log.Information("Whisper model downloaded to {Path}", modelPath);
        }

        var factory = WhisperFactory.FromPath(modelPath);
        int threads = Math.Max(2, Environment.ProcessorCount / 2);
        _processor = factory.CreateBuilder()
            .WithLanguage("en")
            .WithThreads(threads)
            .Build();

        Log.Information("Whisper small model ready (threads: {T})", threads);
    }

    public void Dispose()
    {
        CleanupCapture();
        _cts?.Cancel();
        _cts?.Dispose();
        _processor?.Dispose();
        _processor = null;
        _segments.Dispose();
        _startStopLock.Dispose();
    }
}
