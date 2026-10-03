using System;
using System.Diagnostics;
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
        VerifikasiWajah = new VerifikasiWajahViewModel(services);

        services.Session.Changed += OnSessionChanged;
        services.Fingerprint.DeviceStatusChanged += OnDeviceStatusChanged;
        services.Camera.DeviceStatusChanged += OnCameraStatusChanged;

        Trace.WriteLine($"[MainWindowViewModel] Initialized. IsDeviceConnected={_services.Fingerprint.IsDeviceConnected}, DeviceName={_services.Fingerprint.DeviceName}");
        Trace.WriteLine($"[MainWindowViewModel] Initialized. IsCameraConnected={_services.Camera.IsDeviceConnected}, CameraName={_services.Camera.DeviceName}");

        UpdateSessionState();
        UpdateDeviceState();
        UpdateCameraState();
    }

    public DaftarFingerprintViewModel Daftar { get; }
    public VerifikasiFingerprintViewModel Verifikasi { get; }
    public RegistrasiFingerprintViewModel Registrasi { get; }
    public KodeBookingViewModel KodeBooking { get; }
    public VerifikasiWajahViewModel VerifikasiWajah { get; }

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
        VerifikasiWajah.OnSessionChanged(session.IsLoggedIn);
    }

    // ---- device state --------------------------------------------------------

    [ObservableProperty] private bool _deviceConnected;
    [ObservableProperty] private string _deviceStatus = "Mencari perangkat...";
    [ObservableProperty] private string _deviceDetail = string.Empty;

    // ---- camera state --------------------------------------------------------
    [ObservableProperty] private bool _cameraConnected;
    [ObservableProperty] private string _cameraStatus = CameraServices.SearchingStatus;
    [ObservableProperty] private string _cameraDetail = string.Empty;

    private void OnDeviceStatusChanged(object? sender, DeviceStatusEventArgs e) =>
        Dispatcher.UIThread.Post(UpdateDeviceState);

    private void OnCameraStatusChanged(object? sender, DeviceStatusEventArgs e) =>
        Dispatcher.UIThread.Post(UpdateCameraState);

    private void UpdateCameraState()
    {
        var isConnected = _services.Camera.IsDeviceConnected;
        var cameraName = _services.Camera.DeviceName;

        Trace.WriteLine($"[MainWindowViewModel] UpdateCameraState. Connected={isConnected}, Name={cameraName}");

        CameraConnected = isConnected;
        CameraStatus = isConnected ? CameraServices.ConnectedStatus : CameraServices.DisconnectedStatus;
        CameraDetail = isConnected
            ? cameraName ?? CameraServices.ConnectedDetail
            : CameraServices.DisconnectedDetail;

        // Satu sumber status untuk Header dan halaman Verifikasi Wajah, supaya keduanya
        // tidak pernah menampilkan kondisi kamera yang berbeda.
        VerifikasiWajah.OnCameraStatusChanged(isConnected, cameraName);
    }

    private void UpdateDeviceState()
    {
        var isConnected = _services.Fingerprint.IsDeviceConnected;
        var deviceName = _services.Fingerprint.DeviceName;

        Trace.WriteLine($"[MainWindowViewModel] UpdateDeviceState. Connected={isConnected}, Name={deviceName}");

        DeviceConnected = isConnected;
        DeviceStatus = isConnected ? "Perangkat terhubung" : "Perangkat terputus";
        DeviceDetail = isConnected
            ? deviceName ?? "Fingerprint reader"
            : _services.Fingerprint.IsSdkAvailable
                ? "Hubungkan USB fingerprint reader"
                : _services.Fingerprint.SdkMissing;

        Verifikasi.OnDeviceStatusChanged(isConnected);
        Registrasi.OnDeviceStatusChanged(isConnected);
    }
}

