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
    /// Raised when an authenticated cloud request comes back 401 Unauthorized while a token was
    /// attached — i.e. the seat token has been revoked/expired or the seat was released elsewhere.
    /// The app uses this to drop the dead session and return to sign-in instead of silently running
    /// on a session the server no longer accepts.
    /// </summary>
    event Action? Unauthorized;

    /// <summary>Called by the auth handler when a tokened request was rejected with 401.</summary>
    void NotifyUnauthorized();

    /// <summary>
    /// The stable hardware fingerprint for this machine, attached to authenticated requests so the
    /// server can verify the seat is still being used from the device it was bound to.
    /// </summary>
    string HardwareId { get; }
    void SetHardware(string hardwareId);
}
