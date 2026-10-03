using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaFingerprint.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.ViewModels;

/// <summary>
/// Halaman Verifikasi Wajah. Menampilkan live view dari <see cref="CameraServices"/> dan
/// menyiapkan tempat untuk hasil verifikasi. Face detection/recognition, encoding, dan
/// panggilan API belum termasuk pada tahap ini.
/// </summary>
public sealed partial class VerifikasiWajahViewModel : ViewModelBase
{
    // Disimpan untuk tahap berikutnya (face recognition / Frista API).
    private readonly AppServices _services;

    // ---- double buffering frame kamera --------------------------------------
    // Avalonia hanya me-render ulang sebuah Image saat referensi Source berubah; menulis ke
    // WriteableBitmap yang sama berulang kali tidak memicu repaint. Karena itu dua bitmap
    // dipakai bergantian setiap frame.
    private WriteableBitmap? _frameA;
    private WriteableBitmap? _frameB;
    private bool _useSecondBuffer;

    // Salinan frame dari thread capture. Dilindungi _frameLock karena ditulis di thread capture
    // dan dibaca di UI thread.
    private readonly object _frameLock = new();
    private byte[] _staging = Array.Empty<byte>();
    private int _stagingWidth;
    private int _stagingHeight;
    private int _stagingStride;

    // 0/1: penanda UI thread sedang menggambar frame. Frame berikutnya yang datang saat masih
    // 1 akan dibuang supaya tidak ada penumpukan pekerjaan di UI thread.
    private int _renderPending;

    public VerifikasiWajahViewModel(AppServices services)
    {
        _services = services;
        IsLoggedIn = _services.Session.IsLoggedIn;

        // Frame datang dari thread capture kamera. Handler menyalin datanya lebih dulu, lalu
        // menggambar di UI thread — objek UI tidak pernah disentuh dari thread lain.
        _services.Camera.FrameReady += OnFrameReady;

        // Kamera yang sudah streaming saat halaman dibuat langsung ditampilkan, memakai sumber
        // status yang sama dengan Header (CameraServices).
        OnCameraStatusChanged(
            _services.Camera.IsConnected,
            _services.Camera.ResolveStatus(),
            _services.Camera.ResolveDetail());
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

    /// <summary>Bitmap yang sedang ditampilkan di area kamera (berganti isi setiap frame).</summary>
    [ObservableProperty]
    private IImage? _cameraFrame;

    /// <summary>
    /// True bila ada frame yang sudah digambar. Dipakai untuk menukar placeholder dengan live
    /// view; sengaja tidak memakai "stream terbuka" supaya placeholder tidak hilang sebelum
    /// frame pertama benar-benar siap.
    /// </summary>
    [ObservableProperty]
    private bool _isPreviewActive;

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
            // Siklus hidup stream diatur MainWindowViewModel (satu pemilik). Di sini cukup
            // memastikan tidak ada frame basi yang tertinggal setelah logout.
            ClearFrames();
            SetStatus(string.Empty, false);
        }
    }

    /// <summary>
    /// Dipanggil dari <see cref="MainWindowViewModel"/> memakai status stream dari
    /// <see cref="Services.CameraServices"/> — sumber yang sama dengan indikator di Header,
    /// sehingga halaman ini tidak pernah menyimpang dari status Header.
    /// </summary>
    public void OnCameraStatusChanged(bool connected, string? cameraStatus, string? cameraDetail)
    {
        // Placeholder hanya terlihat saat stream tidak berjalan, jadi judulnya mengikuti kondisi.
        CameraTitle = connected ? "Live Camera View" : "Kamera tidak tersedia";
        CameraStatus = connected
            ? cameraDetail ?? CameraServices.ConnectedDetail
            : cameraStatus ?? CameraServices.DisconnectedDetail;

        if (!connected)
        {
            // Stream tertutup: jangan tinggalkan frame terakhir yang sudah basi — placeholder
            // harus terlihat lagi.
            ClearFrames();
        }

        RefreshCameraDeviceList();
    }

    // ---- rendering frame kamera ---------------------------------------------

    /// <summary>
    /// Menerima frame dari thread capture. Handler ini harus selesai cepat karena buffer milik
    /// service dipakai ulang untuk frame berikutnya, jadi isinya disalin lebih dulu dan
    /// penggambaran dilakukan di UI thread.
    /// </summary>
    private void OnFrameReady(object? sender, CameraFrameEventArgs e)
    {
        if (e.Width <= 0 || e.Height <= 0 || e.Stride <= 0)
            return;

        var needed = e.Stride * e.Height;
        if (needed <= 0 || e.Pixels.Length < needed)
            return;

        lock (_frameLock)
        {
            if (_staging.Length < needed)
                _staging = new byte[needed];

            Buffer.BlockCopy(e.Pixels, 0, _staging, 0, needed);
            _stagingWidth = e.Width;
            _stagingHeight = e.Height;
            _stagingStride = e.Stride;
        }

        // Bila UI thread belum selesai menggambar frame sebelumnya, frame ini dibuang. Live view
        // tidak perlu setiap frame; yang penting tidak ada penumpukan kerja di UI thread.
        if (Interlocked.Exchange(ref _renderPending, 1) == 1)
            return;

        Dispatcher.UIThread.Post(RenderFrame);
    }

    /// <summary>
    /// Menggambar frame terakhir ke bitmap. Selalu dipanggil di UI thread.
    /// </summary>
    private void RenderFrame()
    {
        try
        {
            int width, height, stride;
            lock (_frameLock)
            {
                width = _stagingWidth;
                height = _stagingHeight;
                stride = _stagingStride;
            }

            if (width <= 0 || height <= 0 || stride <= 0)
                return;

            EnsureFrameBuffers(width, height);

            // Buffer ditukar setiap frame. Referensi Source yang selalu berubah inilah yang
            // memicu Avalonia menggambar ulang.
            var target = _useSecondBuffer ? _frameA : _frameB;
            _useSecondBuffer = !_useSecondBuffer;

            if (target is null)
                return;

            using (var framebuffer = target.Lock())
            {
                // Stride bitmap belum tentu sama dengan stride frame, jadi disalin per baris
                // memakai lebar baris milik bitmap.
                var rowBytes = Math.Min(stride, framebuffer.RowBytes);

                lock (_frameLock)
                {
                    for (var y = 0; y < height; y++)
                    {
                        Marshal.Copy(
                            _staging,
                            y * stride,
                            IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes),
                            rowBytes);
                    }
                }
            }

            CameraFrame = target;
            IsPreviewActive = true;
        }
        finally
        {
            // Dilepas terakhir supaya frame berikutnya baru diambil setelah penggambaran selesai.
            Interlocked.Exchange(ref _renderPending, 0);
        }
    }

    /// <summary>
    /// Menyiapkan dua bitmap sesuai ukuran frame. Dibuat ulang hanya saat ukuran berubah
    /// (mis. kamera diganti), karena alokasi bitmap tidak murah.
    /// </summary>
    private void EnsureFrameBuffers(int width, int height)
    {
        if (_frameA is not null && _frameB is not null &&
            _frameA.PixelSize.Width == width && _frameA.PixelSize.Height == height)
        {
            return;
        }

        // Bitmap lama dilepas dari Image lebih dulu supaya tidak ada frame basi yang tampil
        // dengan ukuran baru.
        CameraFrame = null;
        IsPreviewActive = false;

        // Bitmap lama sengaja TIDAK di-Dispose di sini. Avalonia mencatat perintah gambar lalu
        // mengeksekusinya di render thread, sehingga bitmap yang masih dipakai pada frame yang
        // sedang berjalan bisa di-Dispose di bawahnya. Karena penggantian ukuran praktis tidak
        // pernah terjadi (kamera ini tetap 1280x720), bitmap lama dibiarkan diambil GC daripada
        // mengambil risiko crash pada renderer.
        _frameA = CreateFrameBitmap(width, height);
        _frameB = CreateFrameBitmap(width, height);
        _useSecondBuffer = false;
    }

    private static WriteableBitmap CreateFrameBitmap(int width, int height) =>
        new(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

    /// <summary>
    /// Melepas frame dan mengembalikan area kamera ke placeholder. Hanya dipanggil di UI thread.
    /// </summary>
    private void ClearFrames()
    {
        CameraFrame = null;
        IsPreviewActive = false;

        lock (_frameLock)
        {
            _stagingWidth = 0;
            _stagingHeight = 0;
            _stagingStride = 0;
        }

        Interlocked.Exchange(ref _renderPending, 0);
    }
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
            OnCameraStatusChanged(result.Connected, _services.Camera.ResolveStatus(), result.Detail);

            if (result.Connected)
            {
                SetStatus($"Kamera berhasil terhubung: {result.Detail}", false);
            }
            else if (_services.Camera.IsDeviceConnected)
            {
                // Device dikenali Windows tetapi stream tidak bisa dibuka. Pesannya dibedakan
                // supaya pengguna tidak disuruh mengecek koneksi HP padahal masalahnya lain.
                SetStatus(
                    $"Kamera terdeteksi tetapi gagal dibuka: {result.Detail}. " +
                    "Pastikan kamera tidak sedang dipakai aplikasi lain (mis. kamera sedang dibuka di HP).",
                    true);
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
