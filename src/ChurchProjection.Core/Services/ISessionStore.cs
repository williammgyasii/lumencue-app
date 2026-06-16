using ChurchProjection.Core.Models.Tenancy;

namespace ChurchProjection.Core.Services;

/// <summary>Persists the signed-in session and this install's stable device id locally.</summary>
public interface ISessionStore
{
    Task<AuthSession?> LoadAsync();
    Task SaveAsync(AuthSession session);
    Task ClearAsync();

    /// <summary>Returns a stable per-install device id, generating and persisting one on first use.</summary>
    Task<string> GetOrCreateDeviceIdAsync();
}
