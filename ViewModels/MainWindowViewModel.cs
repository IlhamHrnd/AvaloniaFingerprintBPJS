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

        // Halaman Verifikasi memberi tahu saat operator berpindah metode, karena kamera hanya boleh
        // menyala selama metode Wajah yang dipilih. Siklus hidup stream tetap milik kelas ini.
        Verifikasi.CameraUsageChanged += OnVerifikasiCameraUsageChanged;

        Trace.WriteLine($"[MainWindowViewModel] Initialized. ReaderState={_services.Fingerprint.DeviceState.State}, IsDeviceConnected={_services.Fingerprint.IsDeviceConnected}, DeviceName={_services.Fingerprint.DeviceName}");
        Trace.WriteLine($"[MainWindowViewModel] Initialized. IsCameraConnected={_services.Camera.IsDeviceConnected}, CameraName={_services.Camera.DeviceName}");

        UpdateSessionState();
        UpdateDeviceState();
        UpdateCameraState();
    }

    /// <summary>
    /// Halaman Verifikasi gabungan (fingerprint + wajah). Satu tab, satu ViewModel: metode
    /// dipilih di dalam halaman itu sendiri.
    /// </summary>
    public VerifikasiViewModel Verifikasi { get; }
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

        Verifikasi.OnSessionChanged(session.IsLoggedIn);
        KodeBooking.OnSessionChanged(session.IsLoggedIn);

        // Kamera mengikuti sesi: dibuka setelah login, ditutup saat logout supaya device tidak
        // terus dipakai saat tabnya tidak aktif.
        if (session.IsLoggedIn)
            _ = EnsureCameraPreviewAsync();
        else
            _ = _services.Camera.StopPreviewAsync();
    }

    // ---- device state --------------------------------------------------------

    [ObservableProperty] private bool _deviceConnected;

    // Nilai awal disamakan dengan pola _cameraStatus di bawah: sebelum probe pertama selesai,
    // indikator menampilkan "mencari", bukan "terputus".
    [ObservableProperty] private string _deviceStatus = FingerprintServices.SearchingStatus;

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

    // Snapshot device sekarang diteruskan utuh (bukan sekadar bool) sehingga alasan "belum
    // terhubung" ikut tersedia. Pembacaan state tetap dilakukan di UI thread.
    private void OnDeviceStateChanged(object? sender, FingerprintDeviceState state) =>
        Dispatcher.UIThread.Post(UpdateDeviceState);

    private void OnCameraStatusChanged(object? sender, DeviceStatusEventArgs e) =>
        Dispatcher.UIThread.Post(UpdateCameraState);

    /// <summary>
    /// Dipanggil saat operator berpindah metode di halaman Verifikasi. Kamera hanya boleh menyala
    /// selama metode Wajah yang dipilih: perpindahan ke Fingerprint menutup stream saat itu juga,
    /// dan perpindahan ke Wajah membukanya kembali bila device tersedia dan operator sudah login.
    /// </summary>
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

        // Indikator Header memakai kehadiran device, bukan keadaan stream. Sejak kamera hanya
        // dinyalakan selama metode Wajah, stream yang idle di metode Fingerprint bukan berarti
        // kameranya tidak ada. Kegagalan membuka device tetap terlihat lewat CameraDetail, dan
        // keadaan stream tetap dilaporkan apa adanya ke halaman Verifikasi lewat CameraStatus.
        CameraConnected = _services.Camera.IsDeviceConnected;
        CameraStatus = _services.Camera.ResolveStatus();

        // Detail (termasuk nama device) diambil dari CameraServices supaya Header dan halaman
        // Verifikasi memakai teks yang persis sama.
        var detail = _services.Camera.ResolveDetail();
        CameraDetail = detail;

        // Satu sumber status untuk Header dan halaman Verifikasi, supaya keduanya tidak pernah
        // menampilkan kondisi kamera yang berbeda. Yang dikirim adalah keadaan stream: panel
        // Wajah memang perlu tahu apakah frame benar-benar datang.
        Verifikasi.OnCameraStatusChanged(isStreaming, CameraStatus, detail);

        // Stream dibuka otomatis begitu device terdeteksi, sesi sudah login, dan metode Wajah
        // yang dipilih. Di metode Fingerprint pemanggilan ini langsung kembali tanpa berbuat apa
        // pun, jadi kamera tidak pernah menyala untuk halaman yang tidak memakainya.
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

        // Kamera hanya dipakai metode Wajah. Selama metode Fingerprint yang dipilih, stream tidak
        // boleh dibuka sama sekali: tidak ada yang menampilkannya dan device jadi terus terpakai.
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

        // Hanya FingerprintReaderState.Ready yang menyalakan indikator. Perangkat biometrik yang
        // terlihat Windows tetapi tidak bisa dibuka SDK (sensor WBF bawaan laptop, kamera IR,
        // atau entri ghost setelah dicabut) tidak lagi dihitung sebagai terhubung.
        DeviceConnected = fingerprint.IsDeviceConnected;

        // Teks status dan detail diambil dari FingerprintServices supaya Header dan halaman
        // fingerprint tidak pernah menampilkan kondisi yang berbeda.
        DeviceStatus = fingerprint.ResolveStatus();
        DeviceDetail = fingerprint.ResolveDetail();

        Verifikasi.OnDeviceStatusChanged(DeviceConnected);
    }
}

