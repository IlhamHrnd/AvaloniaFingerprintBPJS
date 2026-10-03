using AvaloniaFingerprint.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.ViewModels;

/// <summary>
/// Halaman Verifikasi Wajah. Tahap ini hanya UI + logic sederhana: belum ada
/// kamera, face detection/recognition, encoding, maupun panggilan API.
/// </summary>
public sealed partial class VerifikasiWajahViewModel : ViewModelBase
{
    // Disimpan untuk tahap berikutnya (camera service / Frista API).
    private readonly AppServices _services;

    public VerifikasiWajahViewModel(AppServices services)
    {
        _services = services;
        IsLoggedIn = _services.Session.IsLoggedIn;

        // Kamera yang sudah tersedia saat halaman dibuat langsung ditampilkan sebagai siap,
        // memakai sumber status yang sama dengan Header (CameraServices).
        OnCameraStatusChanged(_services.Camera.IsDeviceConnected, _services.Camera.ResolveDetail());
    }

    [ObservableProperty]
    private bool _isLoggedIn;

    /// <summary>
    /// Placeholder area kamera. Nantinya container ini diganti menjadi actual
    /// camera preview, sehingga title/status disiapkan sebagai binding.
    /// </summary>
    [ObservableProperty]
    private string _cameraTitle = "Live Camera View";

    [ObservableProperty]
    private string _cameraStatus = "Kamera belum diaktifkan";

    /// <summary>Belum dipakai; disiapkan untuk tahap implementasi kamera berikutnya.</summary>
    [ObservableProperty]
    private bool _isCameraActive;

    /// <summary>
    /// True selama proses pencarian/pemilihan kamera berjalan, supaya tombol "Hubungkan Kamera"
    /// tidak memicu scan bertumpuk saat ditekan berulang.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCameraCommand))]
    private bool _isConnecting;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatus;

    [ObservableProperty]
    private bool _statusIsError;

    /// <summary>
    /// Daftar nama kamera yang terdeteksi (debugging). Ditampilkan di halaman Verifikasi Wajah
    /// supaya bisa dilihat langsung device mana yang dikenali Windows — termasuk kamera HP yang
    /// datang lewat Link to Windows / Phone Link maupun kamera virtual lain.
    /// </summary>
    [ObservableProperty]
    private string _cameraDeviceList = string.Empty;

    [ObservableProperty]
    private bool _hasCameraDeviceList;

    // ---- hasil verifikasi ----------------------------------------------------
    // Nilai default "-" (belum ada data). Pengambilan data peserta akan
    // diimplementasikan pada tahap berikutnya.

    [ObservableProperty]
    private string _resultNama = "-";

    [ObservableProperty]
    private string _resultNoKartu = "-";

    [ObservableProperty]
    private string _resultUsia = "-";

    [ObservableProperty]
    private string _resultKeterangan = "-";

    public void OnSessionChanged(bool loggedIn)
    {
        IsLoggedIn = loggedIn;
        if (!loggedIn)
        {
            SetStatus(string.Empty, false);
        }
    }

    /// <summary>
    /// Dipanggil dari <see cref="MainWindowViewModel"/> memakai status dari
    /// <see cref="Services.CameraServices"/> — sumber yang sama dengan indikator di Header,
    /// sehingga halaman ini tidak pernah menyimpang dari status Header. Area preview tetap
    /// placeholder: stream kamera belum dibuka pada tahap ini.
    /// </summary>
    public void OnCameraStatusChanged(bool connected, string? cameraDetail)
    {
        IsCameraActive = connected;
        CameraTitle = connected ? "Live Camera View" : "Kamera tidak tersedia";
        CameraStatus = connected
            ? cameraDetail ?? CameraServices.ConnectedDetail
            : CameraServices.DisconnectedDetail;

        RefreshCameraDeviceList();
    }

    /// <summary>
    /// Menampilkan daftar kamera yang dikenali Windows pada scan terakhir. Sumbernya tetap
    /// <see cref="Services.CameraServices"/> — sama dengan Header — jadi tidak ada state kedua.
    /// </summary>
    private void RefreshCameraDeviceList()
    {
        var cameras = _services.Camera.AvailableCameras;
        HasCameraDeviceList = cameras.Count > 0;
        CameraDeviceList = cameras.Count > 0
            ? "Kamera terdeteksi:" + Environment.NewLine +
              string.Join(Environment.NewLine, cameras.Select(n => $"- {n}"))
            : string.Empty;
    }

    /// <summary>
    /// Mencari dan memilih kamera aktif memakai mekanisme deteksi yang sudah ada
    /// (<see cref="CameraServices.ReconnectAsync"/>): pencocokan <c>cameraKeywords</c>.
    /// Tidak membuat mekanisme deteksi kedua. Aman dipakai ulang untuk reconnect
    /// tanpa restart aplikasi.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConnectCamera))]
    private async Task ConnectCameraAsync()
    {
        IsConnecting = true;
        SetStatus(string.Empty, false);

        try
        {
            var result = await _services.Camera.ReconnectAsync();

            // Area kamera memakai status yang sama dengan Header (satu sumber state).
            OnCameraStatusChanged(result.Connected, result.Detail);

            if (result.Connected)
            {
                SetStatus($"Kamera berhasil terhubung: {result.Detail}", false);
            }
            else
            {
                SetStatus("Kamera tidak ditemukan. Pastikan kamera HP sudah terhubung ke Windows melalui Link to Windows.", true);
            }
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private bool CanConnectCamera() => !IsConnecting;

    /// <summary>
    /// Sementara hanya logic UI. Proses verifikasi wajah (kamera, encoding, API)
    /// akan diimplementasikan pada tahap berikutnya.
    /// </summary>
    [RelayCommand]
    private void VerifyFace()
    {
        SetStatus("Fitur verifikasi wajah belum diimplementasikan.", false);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
        HasStatus = !string.IsNullOrEmpty(message);
    }
}
