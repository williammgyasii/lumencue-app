using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using PortAudioSharp;
using Serilog;

namespace ChurchProjection.Infrastructure.Audio;

public static class MicrophoneCaptureFactory
{
    private static readonly object InitLock = new();
    private static bool _portAudioInitialized;

    public static List<string> ListInputDevices()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ListWasapiInputDevices();

        try
        {
            EnsurePortAudioInitialized();
            return ListPortAudioInputDevices();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to enumerate PortAudio input devices");
            return [];
        }
    }

    public static IMicrophoneCapture? Open(string? deviceName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return OpenWasapi(deviceName);

        try
        {
            EnsurePortAudioInitialized();
            return OpenPortAudio(deviceName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open PortAudio capture device");
            return null;
        }
    }

    private static List<string> ListWasapiInputDevices()
    {
        var devices = new List<string>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                devices.Add(device.FriendlyName);
                device.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to enumerate WASAPI input devices");
        }
        return devices;
    }

    private static IMicrophoneCapture? OpenWasapi(string? deviceName)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            MMDevice? selected = null;
            if (deviceName is not null)
            {
                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                selected = endpoints.FirstOrDefault(d =>
                    d.FriendlyName.Contains(deviceName, StringComparison.OrdinalIgnoreCase));
            }
            selected ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return new WasapiMicrophoneCapture(selected);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open WASAPI capture device");
            return null;
        }
    }

    private static void EnsurePortAudioInitialized()
    {
        lock (InitLock)
        {
            if (_portAudioInitialized) return;
            PortAudio.Initialize();
            _portAudioInitialized = true;
        }
    }

    private static List<string> ListPortAudioInputDevices()
    {
        var devices = new List<string>();
        try
        {
            for (int i = 0; i < PortAudio.DeviceCount; i++)
            {
                var info = PortAudio.GetDeviceInfo(i);
                if (info.maxInputChannels > 0)
                    devices.Add(info.name);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to enumerate PortAudio input devices");
        }
        return devices;
    }

    private static IMicrophoneCapture? OpenPortAudio(string? deviceName)
    {
        try
        {
            var deviceIndex = ResolvePortAudioDeviceIndex(deviceName);
            if (deviceIndex == PortAudio.NoDevice)
            {
                Log.Error("No PortAudio input device available");
                return null;
            }

            var info = PortAudio.GetDeviceInfo(deviceIndex);
            var sampleRate = info.defaultSampleRate > 0 ? info.defaultSampleRate : 48_000;
            Log.Information("PortAudio input: {Name} @ {Rate}Hz", info.name, sampleRate);
            return new PortAudioMicrophoneCapture(deviceIndex, sampleRate);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open PortAudio capture device");
            return null;
        }
    }

    private static int ResolvePortAudioDeviceIndex(string? deviceName)
    {
        if (deviceName is not null)
        {
            for (int i = 0; i < PortAudio.DeviceCount; i++)
            {
                var info = PortAudio.GetDeviceInfo(i);
                if (info.maxInputChannels > 0 &&
                    info.name.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        var defaultDevice = PortAudio.DefaultInputDevice;
        if (defaultDevice != PortAudio.NoDevice)
            return defaultDevice;

        for (int i = 0; i < PortAudio.DeviceCount; i++)
        {
            if (PortAudio.GetDeviceInfo(i).maxInputChannels > 0)
                return i;
        }

        return PortAudio.NoDevice;
    }

    private sealed class WasapiMicrophoneCapture : IMicrophoneCapture
    {
        private readonly WasapiCapture _capture;

        public WasapiMicrophoneCapture(MMDevice device) => _capture = new WasapiCapture(device);

        public WaveFormat WaveFormat => _capture.WaveFormat;

        public event EventHandler<WaveInEventArgs>? DataAvailable
        {
            add => _capture.DataAvailable += value;
            remove => _capture.DataAvailable -= value;
        }

        public void StartRecording() => _capture.StartRecording();

        public void StopRecording() => _capture.StopRecording();

        public void Dispose() => _capture.Dispose();
    }

    private sealed class PortAudioMicrophoneCapture : IMicrophoneCapture
    {
        private readonly PortAudioSharp.Stream _stream;
        // Must stay alive for the lifetime of the stream — GC collecting the delegate crashes native code.
        private readonly PortAudioSharp.Stream.Callback _callback;
        private readonly int _sampleRate;

        public PortAudioMicrophoneCapture(int deviceIndex, double sampleRate)
        {
            _sampleRate = (int)Math.Round(sampleRate);
            _callback = OnCallback;

            var info = PortAudio.GetDeviceInfo(deviceIndex);
            var param = new StreamParameters
            {
                device = deviceIndex,
                channelCount = 1,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = info.defaultLowInputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero,
            };

            _stream = new PortAudioSharp.Stream(
                inParams: param,
                outParams: null,
                sampleRate: sampleRate,
                framesPerBuffer: 512,
                streamFlags: StreamFlags.ClipOff,
                callback: _callback,
                userData: IntPtr.Zero);
        }

        public WaveFormat WaveFormat => WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, 1);

        public event EventHandler<WaveInEventArgs>? DataAvailable;

        public void StartRecording() => _stream.Start();

        public void StopRecording() => _stream.Stop();

        public void Dispose() => _stream.Dispose();

        private StreamCallbackResult OnCallback(
            IntPtr input,
            IntPtr output,
            uint frameCount,
            ref StreamCallbackTimeInfo timeInfo,
            StreamCallbackFlags statusFlags,
            IntPtr userData)
        {
            if (input == IntPtr.Zero || frameCount == 0)
                return StreamCallbackResult.Continue;

            var byteCount = (int)frameCount * sizeof(float);
            var buffer = new byte[byteCount];
            Marshal.Copy(input, buffer, 0, byteCount);
            DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, byteCount));
            return StreamCallbackResult.Continue;
        }
    }
}
