using AvaloniaFingerprint.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatus;

    [ObservableProperty]
    private bool _statusIsError;

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
    public void OnCameraStatusChanged(bool connected, string? deviceName)
    {
        IsCameraActive = connected;
        CameraTitle = connected ? "Live Camera View" : "Kamera tidak tersedia";
        CameraStatus = connected
            ? deviceName ?? CameraServices.ConnectedDetail
            : CameraServices.DisconnectedDetail;
    }

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
