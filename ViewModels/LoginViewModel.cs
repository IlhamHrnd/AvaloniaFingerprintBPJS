using System;
using System.Threading.Tasks;
using AvaloniaFingerprint.Services.Old;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvaloniaFingerprint.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public LoginViewModel(AppServices services) => _services = services;

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

    /// <summary>Set by the dialog so a successful login can close the window.</summary>
    public Action? RequestClose { get; set; }

    public bool Succeeded { get; private set; }

    private bool CanLogin() =>
        !IsBusy && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        IsBusy = true;
        SetMessage(string.Empty);

        try
        {
            var result = await _services.Api.LoginAsync(Username.Trim(), Password);

            if (!result.Success)
            {
                SetMessage($"Login gagal: {result.Error}");
                return;
            }

            var response = result.Data?.response;
            if (response is null || string.IsNullOrEmpty(response.tokenLogin))
            {
                var apiMessage = result.Data?.metaData?.message;
                SetMessage(string.IsNullOrWhiteSpace(apiMessage)
                    ? "Login gagal: username atau password salah."
                    : $"Login gagal: {apiMessage}");
                return;
            }

            _services.Session.SignIn(
                response.tokenLogin!,
                response.username ?? Username.Trim(),
                response.nama ?? string.Empty,
                response.userID ?? string.Empty);

            Succeeded = true;
            RequestClose?.Invoke();
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

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();

    private void SetMessage(string text)
    {
        Message = text;
        HasMessage = !string.IsNullOrEmpty(text);
    }
}
