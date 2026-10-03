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

        // Kamera mengikuti sesi: dibuka setelah login, ditutup saat logout supaya device tidak
        // terus dipakai saat tabnya tidak aktif.
        if (session.IsLoggedIn)
            _ = EnsureCameraPreviewAsync();
        else
            _ = _services.Camera.StopPreviewAsync();
    }

    // ---- device state --------------------------------------------------------

    [ObservableProperty] private bool _deviceConnected;
    [ObservableProperty] private string _deviceStatus = "Mencari perangkat...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceTooltip))]
    private string _deviceDetail = string.Empty;

    // ---- camera state --------------------------------------------------------

    [ObservableProperty] private bool _cameraConnected;
    [ObservableProperty] private string _cameraStatus = CameraServices.SearchingStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CameraTooltip))]
    private string _cameraDetail = string.Empty;

    // Penjaga supaya percobaan membuka stream tidak menumpuk saat beberapa event datang
    // berdekatan (deteksi device dan perubahan sesi).
    private bool _previewStarting;

    /// <summary>
    /// Tooltip indikator fingerprint. Header hanya menampilkan chip ringkas
    /// ("Fingerprint"), sehingga detail lengkap disajikan saat hover.
    /// </summary>
    public string DeviceTooltip => $"Fingerprint: {DeviceDetail}";

    /// <summary>
    /// Tooltip indikator kamera. Nama device panjang (mis. "Windows Virtual Camera Device")
    /// cukup tampil di sini, bukan di Header.
    /// </summary>
    public string CameraTooltip => $"Kamera: {CameraDetail}";

    private void OnDeviceStatusChanged(object? sender, DeviceStatusEventArgs e) =>
        Dispatcher.UIThread.Post(UpdateDeviceState);

    private void OnCameraStatusChanged(object? sender, DeviceStatusEventArgs e) =>
        Dispatcher.UIThread.Post(UpdateCameraState);

    private void UpdateCameraState()
    {
        var isStreaming = _services.Camera.IsConnected;
        var cameraName = _services.Camera.DeviceName;

        Trace.WriteLine($"[MainWindowViewModel] UpdateCameraState. Streaming={isStreaming}, State={_services.Camera.StreamState}, Name={cameraName}");

        // Indikator Header hijau hanya bila stream benar-benar terbuka, bukan sekadar device
        // terdeteksi — kamera yang gagal dibuka tetap dihitung belum terhubung.
        CameraConnected = isStreaming;
        CameraStatus = _services.Camera.ResolveStatus();

        // Detail (termasuk nama device) diambil dari CameraServices supaya Header dan halaman
        // Verifikasi Wajah memakai teks yang persis sama.
        var detail = _services.Camera.ResolveDetail();
        CameraDetail = detail;

        // Satu sumber status untuk Header dan halaman Verifikasi Wajah, supaya keduanya
        // tidak pernah menampilkan kondisi kamera yang berbeda.
        VerifikasiWajah.OnCameraStatusChanged(isStreaming, CameraStatus, detail);

        // Stream dibuka otomatis begitu device terdeteksi dan sesi sudah login.
        _ = EnsureCameraPreviewAsync();
    }

    /// <summary>
    /// Membuka stream kamera secara otomatis (setelah login dan saat device terdeteksi) tanpa
    /// memblokir UI thread. Aman dipanggil berulang: penjaga di sini mencegah percobaan menumpuk,
    /// dan <see cref="CameraServices"/> sendiri menolak start saat sudah streaming.
    /// </summary>
    private async Task EnsureCameraPreviewAsync()
    {
        if (_previewStarting || _services.Camera.IsConnected || !_services.Session.IsLoggedIn)
            return;

        if (!_services.Camera.IsDeviceConnected)
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

