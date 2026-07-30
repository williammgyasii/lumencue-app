using NAudio.Wave;

namespace ChurchProjection.Infrastructure.Audio;

/// <summary>
/// Cross-platform microphone input. Windows uses WASAPI; macOS/Linux use PortAudio.
/// </summary>
public interface IMicrophoneCapture : IDisposable
{
    WaveFormat WaveFormat { get; }

    event EventHandler<WaveInEventArgs>? DataAvailable;

    void StartRecording();

    void StopRecording();
}
