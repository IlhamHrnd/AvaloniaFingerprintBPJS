using System;
using System.Threading.Tasks;
using AvaloniaFingerprint.Services;
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
            var userId = Username.Trim();

            var result = await Task.Run(() => _services.AppUser.GetUserAuth(userId, Password));

            if (!result.succeeded || string.IsNullOrEmpty(result.data))
            {
                var apiMessage = result.errors?.Length > 0 ? result.errors[0] : string.Empty;
                if (string.IsNullOrWhiteSpace(apiMessage)) apiMessage = result.message;
                SetMessage(string.IsNullOrWhiteSpace(apiMessage)
                    ? "Login gagal: username atau password salah."
                    : $"Login gagal: {apiMessage}");
                return;
            }

            _services.Session.SignIn(
                string.Empty,
                userId,
                string.Empty,
                result.data!);

            Password = string.Empty;
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

    [RelayCommand]
    private void OpenSettingKoneksi()
    {
    }

    [RelayCommand]
    private void AddAction()
    {
    }

    private void SetMessage(string text)
    {
        Message = text;
        HasMessage = !string.IsNullOrEmpty(text);
    }
}
