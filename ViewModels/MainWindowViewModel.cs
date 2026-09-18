using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaFingerprint.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvaloniaFingerprint.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public MainWindowViewModel(AppServices services)
    {
        _services = services;

        Daftar = new DaftarFingerprintViewModel(services);
        Verifikasi = new VerifikasiFingerprintViewModel(services);
        Registrasi = new RegistrasiFingerprintViewModel(services, Daftar);
        KodeBooking = new KodeBookingViewModel(services);

        services.Session.Changed += OnSessionChanged;
        services.Fingerprint.DeviceStatusChanged += OnDeviceStatusChanged;

        if (!services.Fingerprint.IsSdkAvailable)
            DeviceDetail = services.Fingerprint.SdkMissing;

        UpdateSessionState();
    }

    public DaftarFingerprintViewModel Daftar { get; }
    public VerifikasiFingerprintViewModel Verifikasi { get; }
    public RegistrasiFingerprintViewModel Registrasi { get; }
    public KodeBookingViewModel KodeBooking { get; }

    // ---- login state ---------------------------------------------------------

    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private string _loginButtonText = "Login";
    [ObservableProperty] private string _petugasName = "Belum login";
    [ObservableProperty] private string _petugasDetail = "Silakan login untuk mengaktifkan tab";
    [ObservableProperty] private bool _isLoginBusy;

    /// <summary>Set by the view; opens the modal login dialog and returns when it closes.</summary>
    public Func<Task>? ShowLoginDialog { get; set; }

    [RelayCommand]
    private async Task ToggleLoginAsync()
    {
        if (IsLoggedIn)
        {
            _services.Session.SignOut();
            return;
        }

        if (ShowLoginDialog is null)
            return;

        IsLoginBusy = true;
        try
        {
            await ShowLoginDialog();
        }
        finally
        {
            IsLoginBusy = false;
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(UpdateSessionState);

    private void UpdateSessionState()
    {
        var session = _services.Session;
        IsLoggedIn = session.IsLoggedIn;
        LoginButtonText = session.IsLoggedIn ? "Logout" : "Login";

        if (session.IsLoggedIn)
        {
            PetugasName = string.IsNullOrWhiteSpace(session.Nama) ? session.Username : session.Nama;
            PetugasDetail = $"{session.Username} · login {session.LoginTime:HH:mm}";
        }
        else
        {
            PetugasName = "Belum login";
            PetugasDetail = "Silakan login untuk mengaktifkan tab";
        }

        Daftar.OnSessionChanged(session.IsLoggedIn);
        Verifikasi.OnSessionChanged(session.IsLoggedIn);
        Registrasi.OnSessionChanged(session.IsLoggedIn);
        KodeBooking.OnSessionChanged(session.IsLoggedIn);
    }

    // ---- device state --------------------------------------------------------

    [ObservableProperty] private bool _deviceConnected;
    [ObservableProperty] private string _deviceStatus = "Mencari perangkat...";
    [ObservableProperty] private string _deviceDetail = string.Empty;

    private void OnDeviceStatusChanged(object? sender, DeviceStatusEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            DeviceConnected = e.Connected;
            DeviceStatus = e.Connected ? "Perangkat terhubung" : "Perangkat terputus";
            DeviceDetail = e.Connected
                ? e.DeviceName ?? "Fingerprint reader"
                : _services.Fingerprint.IsSdkAvailable
                    ? "Hubungkan USB fingerprint reader"
                    : _services.Fingerprint.SdkMissing;

            Verifikasi.OnDeviceStatusChanged(e.Connected);
            Registrasi.OnDeviceStatusChanged(e.Connected);
        });
}
