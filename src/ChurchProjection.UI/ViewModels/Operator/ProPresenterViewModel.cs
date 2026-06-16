using System.Collections.ObjectModel;
using System.Reactive;
using ChurchProjection.Core.Services;
using ChurchProjection.UI.Services;
using ReactiveUI;
using Serilog;

namespace ChurchProjection.UI.ViewModels.Operator;

/// <summary>
/// Settings UI for the ProPresenter bridge: connection details, a connection test, message
/// discovery, and token mapping. Reads/writes through <see cref="IProPresenterService"/>.
/// </summary>
public class ProPresenterViewModel : ViewModelBase
{
    private readonly IProPresenterService _service;

    private bool _enabled;
    private string _host = "localhost";
    private string _port = "1025";
    private string _maxCharsPerSlide = "0";
    private string _statusText = "Not connected";
    private bool _isBusy;
    private ProMessage? _selectedMessage;
    private string? _selectedTextToken;
    private string? _selectedReferenceToken;

    public ObservableCollection<ProMessage> Messages { get; } = [];
    public ObservableCollection<string> AvailableTokens { get; } = [];

    public ReactiveCommand<Unit, Unit> TestConnectionCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshMessagesCommand { get; }

    public ProPresenterViewModel(IProPresenterService service)
    {
        _service = service;

        var s = service.Settings;
        _enabled = s.Enabled;
        _host = s.Host;
        _port = s.Port.ToString();
        _maxCharsPerSlide = s.MaxCharsPerSlide.ToString();

        TestConnectionCommand = ReactiveCommand.CreateFromTask(TestConnectionAsync);
        RefreshMessagesCommand = ReactiveCommand.CreateFromTask(LoadMessagesAsync);

        ApplyDeckCap();
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _enabled, value);
            Persist();
        }
    }

    public string Host
    {
        get => _host;
        set
        {
            this.RaiseAndSetIfChanged(ref _host, value);
            Persist();
        }
    }

    public string Port
    {
        get => _port;
        set
        {
            this.RaiseAndSetIfChanged(ref _port, value);
            Persist();
        }
    }

    public string MaxCharsPerSlide
    {
        get => _maxCharsPerSlide;
        set
        {
            this.RaiseAndSetIfChanged(ref _maxCharsPerSlide, value);
            Persist();
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public ProMessage? SelectedMessage
    {
        get => _selectedMessage;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedMessage, value);
            UpdateTokenOptions();
            Persist();
        }
    }

    public string? SelectedTextToken
    {
        get => _selectedTextToken;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTextToken, value);
            Persist();
        }
    }

    public string? SelectedReferenceToken
    {
        get => _selectedReferenceToken;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedReferenceToken, value);
            Persist();
        }
    }

    private async Task TestConnectionAsync()
    {
        Persist();
        IsBusy = true;
        StatusText = "Connecting...";
        try
        {
            var version = await _service.TestConnectionAsync();
            if (version is not null)
            {
                StatusText = $"Connected: {version}";
                await LoadMessagesAsync();
            }
            else
            {
                StatusText = $"Could not reach ProPresenter at {Host}:{Port}";
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ProPresenter test connection failed");
            StatusText = "Connection failed";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadMessagesAsync()
    {
        IsBusy = true;
        try
        {
            var messages = await _service.GetMessagesAsync();
            Messages.Clear();
            foreach (var m in messages) Messages.Add(m);

            // Re-select the persisted message by name after a refresh.
            var savedName = _service.Settings.MessageName;
            var match = Messages.FirstOrDefault(m => m.Name == savedName);
            if (match is not null)
            {
                _selectedMessage = match;
                this.RaisePropertyChanged(nameof(SelectedMessage));
                UpdateTokenOptions();
                RestoreSavedTokens();
            }

            if (messages.Count == 0 && Enabled)
                StatusText = "No messages found. Create a Message in ProPresenter, then refresh.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateTokenOptions()
    {
        AvailableTokens.Clear();
        if (SelectedMessage is null) return;
        foreach (var t in SelectedMessage.Tokens)
            AvailableTokens.Add(t);
    }

    private void RestoreSavedTokens()
    {
        var s = _service.Settings;
        _selectedTextToken = AvailableTokens.Contains(s.TextTokenName) ? s.TextTokenName : null;
        _selectedReferenceToken = AvailableTokens.Contains(s.ReferenceTokenName) ? s.ReferenceTokenName : null;
        this.RaisePropertyChanged(nameof(SelectedTextToken));
        this.RaisePropertyChanged(nameof(SelectedReferenceToken));
    }

    private void Persist()
    {
        if (!int.TryParse(_port, out var port) || port <= 0) port = 1025;
        if (!int.TryParse(_maxCharsPerSlide, out var maxChars) || maxChars < 0) maxChars = 0;
        _ = _service.SaveSettingsAsync(new ProPresenterSettings
        {
            Enabled = _enabled,
            Host = string.IsNullOrWhiteSpace(_host) ? "localhost" : _host.Trim(),
            Port = port,
            MessageName = SelectedMessage?.Name ?? _service.Settings.MessageName,
            TextTokenName = SelectedTextToken ?? string.Empty,
            ReferenceTokenName = SelectedReferenceToken ?? string.Empty,
            MaxCharsPerSlide = maxChars,
        });
        ApplyDeckCap();
    }

    private void ApplyDeckCap()
    {
        DeckBuilder.MaxCharsPerSlide = int.TryParse(_maxCharsPerSlide, out var n) && n > 0 ? n : 0;
    }
}
