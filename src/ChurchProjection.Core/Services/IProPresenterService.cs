namespace ChurchProjection.Core.Services;

/// <summary>
/// Connection and mapping settings for pushing live content into ProPresenter 7.9+ via its
/// official REST API. ProPresenter renders the text on the LED wall using its own message theme
/// (animations, lower-thirds, looks); this app just supplies the text.
/// </summary>
public sealed class ProPresenterSettings
{
    /// <summary>When false, the app never contacts ProPresenter.</summary>
    public bool Enabled { get; set; }

    /// <summary>Host running ProPresenter. "localhost" when same machine; the LAN IP otherwise.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>ProPresenter network port (Preferences -> Network). Default is 1025.</summary>
    public int Port { get; set; } = 1025;

    /// <summary>Name of the ProPresenter Message used to display content.</summary>
    public string MessageName { get; set; } = string.Empty;

    /// <summary>Message token that receives the verse / song body text.</summary>
    public string TextTokenName { get; set; } = string.Empty;

    /// <summary>Optional message token that receives the reference / title (blank to skip).</summary>
    public string ReferenceTokenName { get; set; } = string.Empty;

    /// <summary>
    /// Hard cap on characters per slide pushed to ProPresenter (0 = off). Keeps long passages
    /// paging at a readable size instead of overflowing the ProPresenter message box.
    /// </summary>
    public int MaxCharsPerSlide { get; set; }

    public ProPresenterSettings Clone() => new()
    {
        Enabled = Enabled,
        Host = Host,
        Port = Port,
        MessageName = MessageName,
        TextTokenName = TextTokenName,
        ReferenceTokenName = ReferenceTokenName,
        MaxCharsPerSlide = MaxCharsPerSlide,
    };
}

/// <summary>A ProPresenter Message and the named text tokens it exposes.</summary>
public sealed record ProMessage(string Name, IReadOnlyList<string> Tokens);

/// <summary>
/// Bridges this app to ProPresenter so selected scripture / song text is shown on the LED output
/// through a configured ProPresenter Message.
/// </summary>
public interface IProPresenterService
{
    /// <summary>The current, persisted connection/mapping settings.</summary>
    ProPresenterSettings Settings { get; }

    /// <summary>Loads persisted settings from storage. Safe to call once at startup.</summary>
    Task LoadSettingsAsync();

    /// <summary>Persists and applies new settings.</summary>
    Task SaveSettingsAsync(ProPresenterSettings settings);

    /// <summary>Returns the ProPresenter version string if reachable, otherwise null.</summary>
    Task<string?> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists the Messages configured in ProPresenter, with their token names.</summary>
    Task<IReadOnlyList<ProMessage>> GetMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers the configured Message with the given reference and body. No-op (returns false)
    /// when disabled or unconfigured. Never throws; logs and returns false on failure.
    /// </summary>
    Task<bool> ShowAsync(string reference, string body, CancellationToken cancellationToken = default);

    /// <summary>Clears / hides the configured Message. No-op when disabled or unconfigured.</summary>
    Task<bool> ClearAsync(CancellationToken cancellationToken = default);
}
