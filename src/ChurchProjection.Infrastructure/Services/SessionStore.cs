using System.Text.Json;
using ChurchProjection.Core.Models.Tenancy;
using ChurchProjection.Core.Services;
using ChurchProjection.Infrastructure.Data;
using Serilog;

namespace ChurchProjection.Infrastructure.Services;

/// <summary>Stores the session and device id in the local <c>settings</c> table.</summary>
public sealed class SessionStore : ISessionStore
{
    private const string SessionKey = "auth_session";
    private const string DeviceKey = "device_id";

    private readonly SettingsRepository _settings;

    public SessionStore(SettingsRepository settings) => _settings = settings;

    public async Task<AuthSession?> LoadAsync()
    {
        try
        {
            var json = await _settings.GetAsync(SessionKey);
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<AuthSession>(json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load saved session");
            return null;
        }
    }

    public Task SaveAsync(AuthSession session) =>
        _settings.SetAsync(SessionKey, JsonSerializer.Serialize(session));

    public Task ClearAsync() => _settings.SetAsync(SessionKey, "");

    public async Task<string> GetOrCreateDeviceIdAsync()
    {
        var existing = await _settings.GetAsync(DeviceKey);
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;

        var id = Guid.NewGuid().ToString("N");
        await _settings.SetAsync(DeviceKey, id);
        return id;
    }
}
