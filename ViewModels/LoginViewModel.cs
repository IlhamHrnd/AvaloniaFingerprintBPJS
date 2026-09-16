using System;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaFingerprint.Models.Master;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;

namespace AvaloniaFingerprint.ViewModels;

/// <summary>
/// Login form state. Validates locally, then authenticates through the existing
/// <see cref="AppUserViewModels.GetUserAuth"/> backend call.
/// </summary>
public sealed partial class LoginViewModel(IConfiguration config) : ViewModelBase(config)
{
    private const int MinimumUsernameLength = 8;

    private readonly AppUserViewModels _appUser = new(config);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _hasMessage;

    /// <summary>True once <see cref="AppUserViewModels.GetUserAuth"/> accepted the credentials.</summary>
    public bool Succeeded { get; private set; }

    /// <summary>
    /// The username that authenticated successfully. Downstream code should read this
    /// instead of a literal; the session service that used to hold it is not present
    /// in the current working tree.
    /// </summary>
    public string AuthenticatedUsername { get; private set; } = string.Empty;

    /// <summary>Only requires a password so short/invalid usernames can still surface a message.</summary>
    private bool CanLogin() => !IsBusy && !string.IsNullOrEmpty(Password);

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        var username = Username.Trim();
        var password = Password;

        IsBusy = true;
        SetMessage(string.Empty);

        try
        {
            // GetUserAuth is synchronous (RestSharp blocking call), so it is offloaded
            // to a background thread to keep the UI responsive.
            var response = await Task.Run(() => _appUser.GetUserAuth(username, password));

            if (response is not null && response.succeeded)
            {
                // Only the login page exists so far; the post-login flow is not built yet.
                AuthenticatedUsername = username;
                Succeeded = true;
                SetMessage("Login berhasil.");
                return;
            }

            SetMessage(DescribeFailure(response));
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Picks the most informative error text from the response, falling back to the
    /// first entry of <see cref="MasterResponse{T}.errors"/> when no message is set.
    /// </summary>
    private static string DescribeFailure(MasterResponse<string>? response)
    {
        if (response is null) return "Login gagal: tidak ada respons dari server.";

        if (!string.IsNullOrWhiteSpace(response.message)) return response.message;

        var detail = response.errors?.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
        return string.IsNullOrWhiteSpace(detail)
            ? "Login gagal: username atau password salah."
            : detail;
    }

    [RelayCommand]
    private void Cancel() => Clear();

    /// <summary>Shows a message using the existing error surface.</summary>
    public void ShowMessage(string text) => SetMessage(text);

    private void Clear() => SetMessage(string.Empty);

    private void SetMessage(string text)
    {
        Message = text;
        HasMessage = !string.IsNullOrEmpty(text);
    }
}
