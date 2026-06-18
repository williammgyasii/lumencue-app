using ChurchProjection.Core.Services;

namespace ChurchProjection.Infrastructure.Services;

/// <summary>Thread-safe in-memory holder for the active seat token.</summary>
public sealed class SeatTokenProvider : ISeatTokenProvider
{
    private volatile string? _token;
    private volatile string _hardwareId = "";

    public string? CurrentToken => _token;

    public void Set(string? token) => _token = string.IsNullOrWhiteSpace(token) ? null : token;

    public event Action? Unauthorized;

    // Only signal while we still believe we hold a token; once cleared, in-flight 401s are ignored
    // so we don't fire repeatedly after already dropping the session.
    public void NotifyUnauthorized()
    {
        if (_token is null) return;
        Unauthorized?.Invoke();
    }

    public string HardwareId => _hardwareId;

    public void SetHardware(string hardwareId) => _hardwareId = hardwareId ?? "";
}
