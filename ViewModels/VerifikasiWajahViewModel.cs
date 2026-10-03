using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaFingerprint.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.ViewModels;

/// <summary>
/// Halaman Verifikasi Wajah. Menampilkan gambar yang akan diverifikasi, yang bisa berasal dari live
/// view kamera (<see cref="CameraServices"/>) atau dari foto yang diupload pengguna, dan menyiapkan
/// tempat untuk hasil verifikasi. Face detection/recognition, encoding, dan panggilan API belum
/// termasuk pada tahap ini.
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
    [NotifyPropertyChangedFor(nameof(PreviewImage))]
    private IImage? _cameraFrame;

    /// <summary>
    /// True bila ada frame live yang sudah digambar. Dipakai bersama <see cref="IsShowingUploadedPhoto"/>
    /// untuk menukar placeholder dengan gambar; sengaja tidak memakai "stream terbuka" supaya
    /// placeholder tidak hilang sebelum frame pertama benar-benar siap.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewVisible))]
    [NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]
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

    // ---- sumber gambar: live camera atau foto upload ------------------------
    // Dua hal yang berbeda dan sengaja tidak disatukan:
    //   * IsShowingUploadedPhoto -> apa yang mengisi area kamera besar
    //   * CapturedPhoto          -> apa yang mengisi kotak "Hasil Verifikasi"
    // Dengan pemisahan ini, foto upload tetap tampil walaupun kamera terputus, dan hasil
    // verifikasi tidak ikut hilang saat sumber gambar ditukar.

    /// <summary>
    /// Foto yang dipilih pengguna lewat tombol "Upload Foto". Bitmap lama sengaja tidak di-Dispose
    /// saat diganti, dengan alasan yang sama seperti <see cref="EnsureFrameBuffers"/>: renderer
    /// Avalonia bisa masih memakai bitmap yang baru saja dilepas dari Image.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImage))]
    [NotifyPropertyChangedFor(nameof(IsPreviewVisible))]
    [NotifyPropertyChangedFor(nameof(HasUploadedPhoto))]
    [NotifyPropertyChangedFor(nameof(HasAnyPhoto))]
    [NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]
    private IImage? _uploadedPhoto;

    /// <summary>
    /// True bila area kamera besar sedang menampilkan foto upload, bukan live camera. Dipakai
    /// tombol penukar sumber gambar dan pemilihan gambar yang akan diverifikasi.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImage))]
    [NotifyPropertyChangedFor(nameof(IsPreviewVisible))]
    [NotifyPropertyChangedFor(nameof(PhotoSourceActionLabel))]
    [NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]
    private bool _isShowingUploadedPhoto;

    /// <summary>
    /// Gambar yang ditampilkan di kotak "Hasil Verifikasi": frame kamera pada saat tombol
    /// "Verifikasi Wajah" ditekan, atau foto yang diupload. Null berarti placeholder masih tampil.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCapturedPhoto))]
    [NotifyPropertyChangedFor(nameof(HasAnyPhoto))]
    private IImage? _capturedPhoto;

    /// <summary>True bila sudah ada gambar hasil verifikasi (placeholder ditukar).</summary>
    public bool HasCapturedPhoto => CapturedPhoto is not null;

    /// <summary>True bila sudah ada foto upload, sehingga tombol penukar sumber gambar berguna.</summary>
    public bool HasUploadedPhoto => UploadedPhoto is not null;

    /// <summary>True bila ada foto upload atau hasil verifikasi yang bisa dibuang.</summary>
    public bool HasAnyPhoto => HasCapturedPhoto || HasUploadedPhoto;

    /// <summary>
    /// Gambar untuk area kamera besar. Foto upload menggantikan live view selama dipilih, supaya
    /// jelas gambar mana yang akan diverifikasi.
    /// </summary>
    public IImage? PreviewImage => IsShowingUploadedPhoto ? UploadedPhoto : CameraFrame;

    /// <summary>
    /// True bila area kamera besar menampilkan sesuatu (frame live atau foto upload). Dipakai untuk
    /// menukar placeholder; tidak memakai "stream terbuka" supaya placeholder tidak hilang sebelum
    /// frame pertama siap, dan tetap benar saat kamera terputus sementara foto upload masih tampil.
    /// </summary>
    public bool IsPreviewVisible => IsPreviewActive || (IsShowingUploadedPhoto && UploadedPhoto is not null);

    /// <summary>Label tombol penukar sumber gambar, mengikuti sumber yang sedang tampil.</summary>
    public string PhotoSourceActionLabel =>
        IsShowingUploadedPhoto ? "Ganti ke Live Camera" : "Tampilkan Foto Upload";

    /// <summary>
    /// Jendela tempat halaman ini tampil. Dibutuhkan untuk membuka dialog pilih file, karena
    /// dialog dimiliki oleh <c>StorageProvider</c> milik <see cref="TopLevel"/>, bukan oleh
    /// ViewModel. Diserahkan oleh view saat terpasang.
    /// </summary>
    public TopLevel? TopLevel { get; set; }

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

            // Foto upload dan hasil verifikasi ikut dibuang supaya data peserta tidak tertinggal
            // di layar untuk pengguna berikutnya.
            CapturedPhoto = null;
            UploadedPhoto = null;
            IsShowingUploadedPhoto = false;

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
        // Saat foto upload sedang tampil, frame kamera tidak digambar sama sekali: area kamera
        // memang menampilkan foto, jadi menyalin dan menggambar frame hanya membuang CPU. Begitu
        // sumber ditukar kembali ke live, frame berikutnya langsung menggantikan.
        if (IsShowingUploadedPhoto)
            return;

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
    /// <summary>
    /// Menyegarkan daftar nama kamera yang terdeteksi (debugging).
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
    /// Tombol "Upload Foto": memakai foto dari file sebagai sumber gambar, menggantikan live
    /// camera. Dialog yang dibatalkan tidak mengubah apa pun, dan format di luar JPG/JPEG/PNG
    /// ditolak dengan pesan, bukan exception.
    /// </summary>
    [RelayCommand]
    private async Task UploadPhotoAsync()
    {
        var topLevel = TopLevel;
        if (topLevel is null)
        {
            SetStatus("Jendela utama belum siap sehingga dialog pilih file tidak bisa dibuka.", true);
            return;
        }

        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Pilih Foto Wajah",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Foto (JPG, JPEG, PNG)")
                    {
                        Patterns = ["*.jpg", "*.jpeg", "*.png"],
                    },
                ],
            });
        }
        catch (Exception ex)
        {
            SetStatus($"Gagal membuka dialog pilih file: {ex.Message}", true);
            return;
        }

        // Dibatalkan pengguna: sumber gambar dan pesan status tidak diubah.
        if (files.Count == 0)
            return;

        var file = files[0];
        if (!IsSupportedPhotoFile(file.Name))
        {
            SetStatus("Format foto tidak didukung. Pilih foto JPG, JPEG, atau PNG.", true);
            return;
        }

        try
        {
            // Bitmap dilepas dari stream, jadi file tidak terkunci setelah dibaca.
            using var stream = await file.OpenReadAsync();

            UploadedPhoto = new Bitmap(stream);
            IsShowingUploadedPhoto = true;

            SetStatus($"Foto berhasil dipilih: {file.Name}", false);
        }
        catch (Exception ex)
        {
            SetStatus($"Gagal membaca file foto: {ex.Message}", true);
        }
    }

    private static bool IsSupportedPhotoFile(string fileName)
    {
        var extension = System.IO.Path.GetExtension(fileName);

        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Menukar sumber gambar di area kamera besar antara live camera dan foto upload. Selalu
    /// tersedia selama sudah ada foto upload, sehingga pengguna tidak pernah terjebak di salah
    /// satu sumber.
    /// </summary>
    [RelayCommand]
    private void TogglePhotoSource() => IsShowingUploadedPhoto = !IsShowingUploadedPhoto;

    /// <summary>
    /// Membuang foto upload dan hasil verifikasi, lalu kembali ke live camera.
    /// </summary>
    [RelayCommand]
    private void ClearPhoto()
    {
        CapturedPhoto = null;
        UploadedPhoto = null;
        IsShowingUploadedPhoto = false;

        SetStatus("Foto dan hasil verifikasi dihapus.", false);
    }

    /// <summary>
    /// Mengambil gambar yang sedang jadi sumber verifikasi dan menampilkannya di kotak "Hasil
    /// Verifikasi". Pengenalan wajah, encoding, dan panggilan API belum termasuk tahap ini.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanVerifyFace))]
    private void VerifyFace()
    {
        var captured = CaptureCurrentImage();
        if (captured is null)
        {
            SetStatus("Belum ada gambar untuk diverifikasi. Hubungkan kamera atau upload foto terlebih dahulu.", true);
            return;
        }

        CapturedPhoto = captured;
        SetStatus("Gambar berhasil diambil. Pengenalan wajah akan diproses pada tahap berikutnya.", false);
    }

    /// <summary>Tombol verifikasi hanya aktif bila ada yang bisa diverifikasi.</summary>
    private bool CanVerifyFace() => IsPreviewVisible;

    /// <summary>
    /// Menyalin gambar yang sedang jadi sumber verifikasi. Null bila tidak ada yang bisa dipakai.
    ///
    /// Untuk sumber live yang diambil adalah frame terakhir yang diterima dari kamera, bukan
    /// bitmap yang sedang tampil: penggambaran frame berikutnya sengaja ditunda/dibuang saat UI
    /// thread masih sibuk, sehingga bitmap yang tampil bisa tertinggal beberapa frame. Buffer
    /// staging selalu berisi frame terbaru karena isinya diperbarui sebelum penundaan itu.
    /// </summary>
    private IImage? CaptureCurrentImage()
    {
        // Foto upload: yang diverifikasi adalah foto itu sendiri, kamera tidak disentuh sama sekali.
        if (IsShowingUploadedPhoto && UploadedPhoto is not null)
            return UploadedPhoto;

        byte[] pixels;
        int width, height, stride;

        lock (_frameLock)
        {
            width = _stagingWidth;
            height = _stagingHeight;
            stride = _stagingStride;

            if (width <= 0 || height <= 0 || stride <= 0 || _staging.Length < stride * height)
                return null;

            // Disalin di dalam kunci supaya isinya tidak berubah saat dibaca.
            pixels = new byte[stride * height];
            Buffer.BlockCopy(_staging, 0, pixels, 0, pixels.Length);
        }

        return CreateStillBitmap(pixels, width, height, stride);
    }

    /// <summary>
    /// Membuat bitmap lepas dari frame BGRA. Bitmap ini tidak dipakai ulang oleh live view,
    /// sehingga gambarnya tetap utuh walaupun stream terus berjalan.
    /// </summary>
    private static Bitmap CreateStillBitmap(byte[] pixels, int width, int height, int stride)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        using (var framebuffer = bitmap.Lock())
        {
            // Stride bitmap belum tentu sama dengan stride frame, jadi disalin per baris.
            var rowBytes = Math.Min(stride, framebuffer.RowBytes);

            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(
                    pixels,
                    y * stride,
                    IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes),
                    rowBytes);
            }
        }

        return bitmap;
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
        HasStatus = !string.IsNullOrEmpty(message);
    }
}
