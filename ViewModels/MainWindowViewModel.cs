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

        Verifikasi = new VerifikasiViewModel(services);
        KodeBooking = new KodeBookingViewModel(services);

        services.Session.Changed += OnSessionChanged;
        services.Fingerprint.DeviceStateChanged += OnDeviceStateChanged;
        services.Camera.DeviceStatusChanged += OnCameraStatusChanged;

        Verifikasi.CameraUsageChanged += OnVerifikasiCameraUsageChanged;

        Trace.WriteLine($"[MainWindowViewModel] Initialized. ReaderState={_services.Fingerprint.DeviceState.State}, IsDeviceConnected={_services.Fingerprint.IsDeviceConnected}, DeviceName={_services.Fingerprint.DeviceName}");
        Trace.WriteLine($"[MainWindowViewModel] Initialized. IsCameraConnected={_services.Camera.IsDeviceConnected}, CameraName={_services.Camera.DeviceName}");

        UpdateSessionState();
        UpdateDeviceState();
        UpdateCameraState();
    }

    public VerifikasiViewModel Verifikasi { get; }
    public KodeBookingViewModel KodeBooking { get; }

    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private string _loginButtonText = "Login";
    [ObservableProperty] private string _petugasName = "Belum login";
    [ObservableProperty] private string _petugasDetail = "Silakan login untuk mengaktifkan tab";
    [ObservableProperty] private bool _isLoginBusy;

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

        Verifikasi.OnSessionChanged(session.IsLoggedIn);
        KodeBooking.OnSessionChanged(session.IsLoggedIn);

        if (session.IsLoggedIn)
            _ = EnsureCameraPreviewAsync();
        else
            _ = _services.Camera.StopPreviewAsync();
    }

    [ObservableProperty] private bool _deviceConnected;

    [ObservableProperty] private string _deviceStatus = FingerprintServices.SearchingStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceTooltip))]
    private string _deviceDetail = string.Empty;

    [ObservableProperty] private bool _cameraConnected;
    [ObservableProperty] private string _cameraStatus = CameraServices.SearchingStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CameraTooltip))]
    private string _cameraDetail = string.Empty;

    private bool _previewStarting;

    public string DeviceTooltip => $"Fingerprint: {DeviceDetail}";

    public string CameraTooltip => $"Kamera: {CameraDetail}";

    private void OnDeviceStateChanged(object? sender, FingerprintDeviceState state) =>
        Dispatcher.UIThread.Post(UpdateDeviceState);

    private void OnCameraStatusChanged(object? sender, DeviceStatusEventArgs e) =>
        Dispatcher.UIThread.Post(UpdateCameraState);

    private void OnVerifikasiCameraUsageChanged() =>
        Dispatcher.UIThread.Post(async () =>
        {
            if (!Verifikasi.IsWajahMode && _services.Camera.IsConnected)
            {
                try
                {
                    await _services.Camera.StopPreviewAsync();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[MainWindowViewModel] Camera preview stop failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            UpdateCameraState();
        });

    private void UpdateCameraState()
    {
        var isStreaming = _services.Camera.IsConnected;
        var cameraName = _services.Camera.DeviceName;

        Trace.WriteLine($"[MainWindowViewModel] UpdateCameraState. Streaming={isStreaming}, State={_services.Camera.StreamState}, Name={cameraName}");

        CameraConnected = _services.Camera.IsDeviceConnected;
        CameraStatus = _services.Camera.ResolveStatus();

        var detail = _services.Camera.ResolveDetail();
        CameraDetail = detail;

        Verifikasi.OnCameraStatusChanged(isStreaming, CameraStatus, detail);

        _ = EnsureCameraPreviewAsync();
    }

    private async Task EnsureCameraPreviewAsync()
    {
        if (_previewStarting || _services.Camera.IsConnected || !_services.Session.IsLoggedIn)
            return;

        if (!_services.Camera.IsDeviceConnected)
            return;

        if (!Verifikasi.IsWajahMode)
            return;

        _previewStarting = true;
        try
        {
            await _services.Camera.StartPreviewAsync();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MainWindowViewModel] Camera preview start failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _previewStarting = false;
        }
    }

    private void UpdateDeviceState()
    {
        var fingerprint = _services.Fingerprint;
        var state = fingerprint.DeviceState;

        Trace.WriteLine($"[MainWindowViewModel] UpdateDeviceState. State={state.State}, Connected={fingerprint.IsDeviceConnected}, Name={state.DeviceName ?? "-"}, Detail={state.Detail ?? "-"}");

        DeviceConnected = fingerprint.IsDeviceConnected;

        DeviceStatus = fingerprint.ResolveStatus();
        DeviceDetail = fingerprint.ResolveDetail();

        Verifikasi.OnDeviceStatusChanged(DeviceConnected);
    }
}

