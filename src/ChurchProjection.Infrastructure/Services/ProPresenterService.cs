using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using ChurchProjection.Core.Services;
using ChurchProjection.Infrastructure.Data;
using Serilog;

namespace ChurchProjection.Infrastructure.Services;

/// <summary>
/// Talks to ProPresenter 7.9+ over its REST API to push live text onto the LED output via a
/// configured Message. All operations are best-effort: failures are logged and never bubble up so
/// a missing or offline ProPresenter never disrupts local projection.
/// </summary>
public sealed class ProPresenterService : IProPresenterService
{
    private const string SettingsKey = "propresenter_settings";

    private readonly SettingsRepository _settings;
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ProPresenterSettings Settings { get; private set; } = new();

    public ProPresenterService(SettingsRepository settings)
    {
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    }

    public async Task LoadSettingsAsync()
    {
        try
        {
            var json = await _settings.GetAsync(SettingsKey).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var loaded = JsonSerializer.Deserialize<ProPresenterSettings>(json, JsonOptions);
                if (loaded is not null) Settings = loaded;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load ProPresenter settings; using defaults");
        }
    }

    public async Task SaveSettingsAsync(ProPresenterSettings settings)
    {
        Settings = settings.Clone();
        try
        {
            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            await _settings.SetAsync(SettingsKey, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist ProPresenter settings");
        }
    }

    public async Task<string?> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // /version is unversioned and the lightest reachability probe.
            using var resp = await _http.GetAsync(BaseUrl() + "/version", cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;

            var name = TryGetString(root, "name") ?? "ProPresenter";
            var version = TryGetString(root, "api_version") ?? TryGetString(root, "host_description") ?? string.Empty;
            return string.IsNullOrEmpty(version) ? name : $"{name} ({version})";
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "ProPresenter connection test failed");
            return null;
        }
    }

    public async Task<IReadOnlyList<ProMessage>> GetMessagesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var resp = await _http.GetAsync(BaseUrl() + "/v1/messages", cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return [];

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            var result = new List<ProMessage>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var name = MessageName(item);
                if (string.IsNullOrEmpty(name)) continue;
                result.Add(new ProMessage(name, TokenNames(item)));
            }
            return result;
        }
        catch (Exception ex)
        {
            // Unreachable ProPresenter is an expected, benign state; keep this quiet.
            Log.Debug(ex, "Failed to fetch ProPresenter messages from {Host}:{Port}", Settings.Host, Settings.Port);
            return [];
        }
    }

    public async Task<bool> ShowAsync(string reference, string body, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured()) return false;

        var tokens = new List<object>();
        if (!string.IsNullOrWhiteSpace(Settings.TextTokenName))
            tokens.Add(TextToken(Settings.TextTokenName, body ?? string.Empty));
        if (!string.IsNullOrWhiteSpace(Settings.ReferenceTokenName))
            tokens.Add(TextToken(Settings.ReferenceTokenName, reference ?? string.Empty));

        if (tokens.Count == 0) return false;

        try
        {
            var url = $"{BaseUrl()}/v1/message/{Uri.EscapeDataString(Settings.MessageName)}/trigger";
            using var resp = await _http.PostAsJsonAsync(url, tokens, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Log.Warning("ProPresenter trigger returned {Status} for message '{Message}'", (int)resp.StatusCode, Settings.MessageName);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to trigger ProPresenter message '{Message}'", Settings.MessageName);
            return false;
        }
    }

    public async Task<bool> ClearAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured()) return false;
        try
        {
            var url = $"{BaseUrl()}/v1/message/{Uri.EscapeDataString(Settings.MessageName)}/clear";
            using var resp = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to clear ProPresenter message '{Message}'", Settings.MessageName);
            return false;
        }
    }

    // On/off is governed by the ProPresenter *output* (Outputs list); here we only require enough
    // connection detail to actually send.
    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(Settings.Host)
        && !string.IsNullOrWhiteSpace(Settings.MessageName);

    private string BaseUrl() => $"http://{Settings.Host}:{Settings.Port}";

    private static object TextToken(string name, string text) => new
    {
        name,
        text = new { text }
    };

    // Message identity in PP responses may live under id.name, a "title" field, or a bare "name".
    private static string MessageName(JsonElement message)
    {
        if (message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Object)
        {
            var fromId = TryGetString(id, "name");
            if (!string.IsNullOrEmpty(fromId)) return fromId;
        }
        return TryGetString(message, "title") ?? TryGetString(message, "name") ?? string.Empty;
    }

    private static IReadOnlyList<string> TokenNames(JsonElement message)
    {
        if (!message.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Array)
            return [];

        var names = new List<string>();
        foreach (var token in tokens.EnumerateArray())
        {
            var name = TryGetString(token, "name");
            if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name))
                names.Add(name);
        }
        return names;
    }

    private static string? TryGetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
