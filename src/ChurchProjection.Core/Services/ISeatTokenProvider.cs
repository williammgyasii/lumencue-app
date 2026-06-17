namespace ChurchProjection.Core.Services;

/// <summary>
/// Holds the current signed-in seat token in memory so client-side services (Bible proxy,
/// STT token fetch) can authenticate to the cloud API without re-reading persisted state.
/// </summary>
public interface ISeatTokenProvider
{
    string? CurrentToken { get; }
    void Set(string? token);

    /// <summary>
    /// The stable hardware fingerprint for this machine, attached to authenticated requests so the
    /// server can verify the seat is still being used from the device it was bound to.
    /// </summary>
    string HardwareId { get; }
    void SetHardware(string hardwareId);
}
