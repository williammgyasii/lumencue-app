using System.IO.Compression;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using ChurchProjection.Core.Services;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Serilog;
using Vosk;

namespace ChurchProjection.Infrastructure.Audio;

public class VoskTranscriptionService : ITranscriptionService
{
    private Model? _model;
    private VoskRecognizer? _recognizer;
    private WasapiCapture? _capture;
    private WaveFormat? _captureFormat;
    private WaveFormat? _targetFormat;
    private readonly SemaphoreSlim _startStopLock = new(1, 1);
    private readonly object _recognizerLock = new();

    private readonly List<string> _transcriptHistory = [];
    private const int MaxTranscriptLines = 80;
    private const int TargetSampleRate = 16000;

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

            try
            {
                await EnsureModelAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load speech model");
                _statusMessage.OnNext("Model load failed");
                return;
            }

            _statusMessage.OnNext("Initializing audio...");
            _targetFormat = new WaveFormat(TargetSampleRate, 16, 1);

            lock (_recognizerLock)
            {
                _recognizer?.Dispose();
                _recognizer = new VoskRecognizer(_model!, TargetSampleRate);
                _recognizer.SetMaxAlternatives(0);
                _recognizer.SetWords(true);
            }

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

            _capture.StartRecording();
            _isListening.OnNext(true);
            _statusMessage.OnNext("Listening...");
            Log.Information("Vosk transcription started");
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

            lock (_recognizerLock)
            {
                if (_recognizer is not null)
                {
                    var final = _recognizer.FinalResult();
                    EmitFinalText(final);
                    _recognizer.Dispose();
                    _recognizer = null;
                }
            }

            _isListening.OnNext(false);
            _statusMessage.OnNext("Stopped");
            Log.Information("Vosk transcription stopped");
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
            try { _capture.StopRecording(); } catch { }
            _capture.Dispose();
            _capture = null;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_captureFormat is null || e.BytesRecorded == 0) return;

        var pcmBytes = ConvertToMono16kPcm(e.Buffer, e.BytesRecorded, _captureFormat);
        if (pcmBytes.Length == 0) return;

        UpdateAudioLevel(pcmBytes);

        lock (_recognizerLock)
        {
            if (_recognizer is null) return;

            if (_recognizer.AcceptWaveform(pcmBytes, pcmBytes.Length))
            {
                var json = _recognizer.Result();
                EmitFinalText(json);
            }
            else
            {
                var json = _recognizer.PartialResult();
                EmitPartialText(json);
            }
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

    private void EmitFinalText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("text", out var textProp))
            {
                var text = textProp.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(text)) return;

                var segment = new TranscriptionSegment(text, DateTimeOffset.UtcNow, 0.9f);
                _segments.OnNext(segment);
                AppendToTranscript(text);
                Log.Debug("Vosk final: {Text}", text);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse Vosk result");
        }
    }

    private string _lastPartial = "";

    private void EmitPartialText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("partial", out var partialProp))
            {
                var partial = partialProp.GetString()?.Trim() ?? "";
                if (partial == _lastPartial || string.IsNullOrWhiteSpace(partial)) return;
                _lastPartial = partial;

                UpdateLiveTranscript(partial);
            }
        }
        catch { }
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

    private static byte[] ConvertToMono16kPcm(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
    {
        int bytesPerSample = sourceFormat.BitsPerSample / 8;
        int sampleCount = bytesRecorded / (bytesPerSample * sourceFormat.Channels);
        if (sampleCount == 0) return [];

        var monoFloat = new float[sampleCount];
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
            monoFloat[i] = sum / sourceFormat.Channels;
        }

        float[] resampled;
        if (sourceFormat.SampleRate != TargetSampleRate)
        {
            double ratio = (double)TargetSampleRate / sourceFormat.SampleRate;
            int newLength = (int)(monoFloat.Length * ratio);
            resampled = new float[newLength];
            for (int i = 0; i < newLength; i++)
            {
                double srcIndex = i / ratio;
                int idx = (int)srcIndex;
                double frac = srcIndex - idx;
                resampled[i] = idx + 1 < monoFloat.Length
                    ? (float)(monoFloat[idx] * (1 - frac) + monoFloat[idx + 1] * frac)
                    : monoFloat[Math.Min(idx, monoFloat.Length - 1)];
            }
        }
        else
        {
            resampled = monoFloat;
        }

        var pcm = new byte[resampled.Length * 2];
        for (int i = 0; i < resampled.Length; i++)
        {
            var clamped = Math.Clamp(resampled[i], -1f, 1f);
            var sample16 = (short)(clamped * 32767);
            pcm[i * 2] = (byte)(sample16 & 0xFF);
            pcm[i * 2 + 1] = (byte)((sample16 >> 8) & 0xFF);
        }
        return pcm;
    }

    private async Task EnsureModelAsync()
    {
        if (_model is not null) return;

        _statusMessage.OnNext("Loading speech model...");

        var modelsDir = Path.Combine(AppContext.BaseDirectory, "models");
        Directory.CreateDirectory(modelsDir);
        var modelDir = Path.Combine(modelsDir, "vosk-model-en-us-0.22-lgraph");

        if (!Directory.Exists(modelDir) || !File.Exists(Path.Combine(modelDir, "README")))
        {
            Log.Information("Downloading Vosk English model (~128 MB)...");
            _statusMessage.OnNext("Downloading speech model (~128 MB, first run only)...");

            var zipPath = Path.Combine(modelsDir, "vosk-model-en-us-0.22-lgraph.zip");
            try
            {
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromMinutes(10);
                var url = "https://alphacephei.com/vosk/models/vosk-model-en-us-0.22-lgraph.zip";

                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                {
                    await using var netStream = await response.Content.ReadAsStreamAsync();
                    await using var fs = File.Create(zipPath);
                    await netStream.CopyToAsync(fs);
                }

                Log.Information("Model downloaded, extracting...");
                _statusMessage.OnNext("Extracting model...");

                ZipFile.ExtractToDirectory(zipPath, modelsDir, overwriteFiles: true);

                try { File.Delete(zipPath); } catch { }

                Log.Information("Vosk model ready at {Path}", modelDir);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to download Vosk model");
                _statusMessage.OnNext("Model download failed");
                throw;
            }
        }

        Vosk.Vosk.SetLogLevel(-1);
        _model = new Model(modelDir);
        Log.Information("Vosk model loaded from {Path}", modelDir);
    }

    public void Dispose()
    {
        CleanupCapture();
        lock (_recognizerLock)
        {
            _recognizer?.Dispose();
            _recognizer = null;
        }
        _model?.Dispose();
        _model = null;
        _segments.Dispose();
        _startStopLock.Dispose();
    }
}
