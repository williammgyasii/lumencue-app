using System.Reactive;
using ChurchProjection.Core.Models.Tenancy;
using ChurchProjection.Core.Services;
using ReactiveUI;
using Serilog;

namespace ChurchProjection.UI.ViewModels;

public sealed class SignInViewModel : ViewModelBase
{
    private readonly ICloudGateway _gateway;
    private readonly ISessionStore _store;

    /// <summary>Raised after a successful sign-in with the saved session.</summary>
    public event Action<AuthSession>? SignedIn;

    public SignInViewModel(ICloudGateway gateway, ISessionStore store)
    {
        _gateway = gateway;
        _store = store;
        ModeNote = gateway.IsConfigured
            ? "Sign in with your branch credentials to claim a seat."
            : "Offline/dev mode: any organization and branch will sign in.";

        var canSignIn = this.WhenAnyValue(
            x => x.OrganizationCode, x => x.BranchCode, x => x.Busy,
            (org, branch, busy) => !busy && !string.IsNullOrWhiteSpace(org) && !string.IsNullOrWhiteSpace(branch));

        SignInCommand = ReactiveCommand.CreateFromTask(SignInAsync, canSignIn);
    }

    private string _organizationCode = "";
    public string OrganizationCode { get => _organizationCode; set => this.RaiseAndSetIfChanged(ref _organizationCode, value); }

    private string _branchCode = "";
    public string BranchCode { get => _branchCode; set => this.RaiseAndSetIfChanged(ref _branchCode, value); }

    private string _password = "";
    public string Password { get => _password; set => this.RaiseAndSetIfChanged(ref _password, value); }

    private bool _busy;
    public bool Busy { get => _busy; set => this.RaiseAndSetIfChanged(ref _busy, value); }

    private string _error = "";
    public string Error { get => _error; set => this.RaiseAndSetIfChanged(ref _error, value); }

    private string _modeNote = "";
    public string ModeNote { get => _modeNote; set => this.RaiseAndSetIfChanged(ref _modeNote, value); }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public ReactiveCommand<Unit, Unit> SignInCommand { get; }

    private async Task SignInAsync()
    {
        Busy = true;
        Error = "";
        this.RaisePropertyChanged(nameof(HasError));
        try
        {
            var deviceId = await _store.GetOrCreateDeviceIdAsync();
            var result = await _gateway.SignInAsync(
                new SignInRequest(OrganizationCode, BranchCode, Password, deviceId));

            if (!result.Success || result.Session is null)
            {
                Error = result.Error ?? "Sign-in failed. Check your codes and try again.";
                this.RaisePropertyChanged(nameof(HasError));
                return;
            }

            await _store.SaveAsync(result.Session);
            SignedIn?.Invoke(result.Session);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Sign-in failed");
            Error = "Could not reach the sign-in service. Check your connection and try again.";
            this.RaisePropertyChanged(nameof(HasError));
        }
        finally
        {
            Busy = false;
        }
    }
}
