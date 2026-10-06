using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaFingerprint.Exceptions;
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
/// tempat untuk hasil verifikasi. Tombol "Verifikasi Wajah" memproses gambar yang sedang aktif
/// menjadi face encoding 128 nilai lewat <see cref="IFaceRecognitionService"/>; deteksi wajah dan
/// encoding sudah termasuk, sedangkan pengiriman ke API Frista belum.
/// </summary>
public sealed partial class VerifikasiWajahViewModel : ViewModelBase
{
    // Dipakai untuk kamera, sesi, dan pengenalan wajah (FaceRecognition); pengiriman ke API Frista
    // belum termasuk tahap ini.
    private readonly AppServices _services;
    private WriteableBitmap? _frameA;
    private WriteableBitmap? _frameB;
    private bool _useSecondBuffer;
    private readonly object _frameLock = new();
    private byte[] _staging = Array.Empty<byte>();
    private int _stagingWidth;
    private int _stagingHeight;
    private int _stagingStride;
    private int _renderPending;

    public VerifikasiWajahViewModel(AppServices services)
    {
        _services = services;
        IsLoggedIn = _services.Session.IsLoggedIn;
        _services.Camera.FrameReady += OnFrameReady;
        OnCameraStatusChanged(
            _services.Camera.IsConnected,
            _services.Camera.ResolveStatus(),
            _services.Camera.ResolveDetail());
    }

    [ObservableProperty]
    private bool _isLoggedIn;

    // ---- input peserta -------------------------------------------------------

    /// <summary>
    /// NIK peserta yang wajahnya akan diverifikasi. Diisi operator sebelum menekan "Verifikasi
    /// Wajah" dan divalidasi di awal <see cref="VerifyFaceAsync"/>. Untuk tahap ini hanya disimpan
    /// sebagai state: belum diteruskan ke endpoint mana pun, menyusul bersama
    /// <see cref="FaceEncoding"/> saat pengiriman ke API Frista dikerjakan.
    /// </summary>
    /// <remarks>
    /// Sengaja tanpa <c>[NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]</c>: tombol harus
    /// tetap bisa ditekan saat NIK kosong supaya pesan validasinya benar-benar tampil, bukan tombol
    /// yang mati tanpa penjelasan.
    /// </remarks>
    [ObservableProperty]
    private string _nik = string.Empty;

    /// <summary>
    /// Panjang NIK yang diterima — 16 digit, sama dengan keterangan "NIK 16 digit" di halaman
    /// Verifikasi Fingerprint.
    /// </summary>
    private const int NikLength = 16;

    /// <summary>
    /// Menyaring masukan NIK: hanya angka ASCII dan maksimal <see cref="NikLength"/> karakter,
    /// sehingga hasil tempel yang mengandung huruf atau spasi langsung bersih.
    /// Idempoten — bila hasilnya sama dengan nilai yang masuk tidak ada penugasan ulang, jadi
    /// mengetik angka secara normal tidak menggeser posisi kursor.
    /// </summary>
    partial void OnNikChanged(string value)
    {
        var cleaned = new string(value.Where(char.IsAsciiDigit).Take(NikLength).ToArray());
        if (cleaned != value)
            Nik = cleaned;
    }

    [ObservableProperty]
    private string _cameraTitle = "Live Camera View";

    [ObservableProperty]
    private string _cameraStatus = "Kamera belum diaktifkan";

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

    /// <summary>
    /// True selama encoding wajah berjalan. Menonaktifkan tombol "Verifikasi Wajah" supaya klik
    /// berulang tidak menumpuk pekerjaan native dlib — pola yang sama dengan
    /// <see cref="IsConnecting"/> pada tombol "Hubungkan Kamera".
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]
    private bool _isVerifyingFace;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImage))]
    [NotifyPropertyChangedFor(nameof(IsPreviewVisible))]
    [NotifyPropertyChangedFor(nameof(HasUploadedPhoto))]
    [NotifyPropertyChangedFor(nameof(HasAnyPhoto))]
    [NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]
    private IImage? _uploadedPhoto;

    /// <summary>
    /// Byte asli file upload, disimpan bersama <see cref="UploadedPhoto"/> supaya encoding memakai
    /// byte yang persis dipilih pengguna. Jalur ini yang diharapkan
    /// <see cref="IFaceRecognitionService.GetFaceEncodingAsync(byte[], CancellationToken)"/>:
    /// format file tidak perlu diketahui pemanggil dan tidak ada konversi pixel manual.
    /// Selalu disetel/dikosongkan bersamaan dengan <see cref="UploadedPhoto"/>.
    /// </summary>
    private byte[]? _uploadedPhotoBytes;

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

    /// <summary>
    /// Vektor embedding wajah 128 nilai hasil <see cref="IFaceRecognitionService"/>, disimpan untuk
    /// tahap berikutnya (API Frista). <see langword="null"/> berarti belum ada encoding yang valid;
    /// setiap jalur gagal mengosongkannya supaya nilai basi tidak terpakai. Sengaja bukan
    /// <c>[ObservableProperty]</c> karena belum ada UI yang mengikatnya.
    /// </summary>
    private List<double>? FaceEncoding { get; set; }

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
            _uploadedPhotoBytes = null;
            IsShowingUploadedPhoto = false;

            // Encoding wajah peserta sebelumnya tidak boleh tertinggal untuk pengguna berikutnya.
            FaceEncoding = null;

            // NIK peserta sebelumnya juga dibuang, supaya tidak ikut terverifikasi untuk pasien lain.
            Nik = string.Empty;

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
    /// (<see cref="CameraServices.ReconnectAsync"/>): pencocokan <c>cameraKeywords</c> dengan
    /// fallback kelas PnP <c>Camera</c>.
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
                SetStatus($"Kamera berhasil terhubung: {result.Detail}", false);
            else if (_services.Camera.IsDeviceConnected)
                SetStatus(CameraServices.DetailOpenFailed, true);
            else
                SetStatus(CameraServices.DetailDeviceNotFound, true);
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
            // File dibaca sekali ke memori lalu Bitmap dibuat dari stream terpisah, jadi file
            // tidak terkunci setelah dibaca dan byte aslinya tetap tersedia untuk encoding.
            using var stream = await file.OpenReadAsync();
            using var buffer = new System.IO.MemoryStream();
            await stream.CopyToAsync(buffer);
            var bytes = buffer.ToArray();

            UploadedPhoto = new Bitmap(new System.IO.MemoryStream(bytes));
            _uploadedPhotoBytes = bytes;
            IsShowingUploadedPhoto = true;

            SetStatus($"Foto berhasil dipilih: {file.Name}", false);
        }
        catch (Exception ex)
        {
            // Gagal baca: jangan tinggalkan pasangan UploadedPhoto/_uploadedPhotoBytes yang
            // tidak konsisten, supaya verifikasi tidak memakai foto lama dengan byte baru.
            UploadedPhoto = null;
            _uploadedPhotoBytes = null;
            IsShowingUploadedPhoto = false;

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
        _uploadedPhotoBytes = null;
        IsShowingUploadedPhoto = false;

        // Encoding dari foto yang baru dibuang tidak boleh tetap dianggap valid.
        FaceEncoding = null;

        SetStatus("Foto dan hasil verifikasi dihapus.", false);
    }

    /// <summary>
    /// Memproses foto yang sedang aktif (live camera atau upload) menjadi face encoding 128 nilai
    /// lewat <see cref="IFaceRecognitionService"/>, lalu menyimpannya di <see cref="FaceEncoding"/>
    /// untuk tahap berikutnya. ViewModel tidak menyentuh <c>FaceRecognitionDotNet</c> dan tidak
    /// melakukan konversi byte/pixel ke double secara manual: ia hanya memilih jalur masukan yang
    /// sesuai sumber gambar, lalu service yang melakukan deteksi wajah dan encoding.
    /// Belum ada panggilan API Frista pada tahap ini.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanVerifyFace))]
    private async Task VerifyFaceAsync()
    {
        // Validasi NIK lebih dulu, sebelum gambar disalin atau model dlib disentuh: permintaan yang
        // pasti ditolak tidak boleh menanggung biaya apa pun, dan CapturedPhoto/FaceEncoding tidak
        // boleh berubah oleh verifikasi yang batal berjalan.
        var nik = Nik.Trim();

        if (nik.Length == 0)
        {
            SetStatus("NIK masih kosong. Silakan masukkan NIK terlebih dahulu.", true);
            return;
        }

        if (!nik.All(char.IsAsciiDigit))
        {
            SetStatus("NIK hanya boleh berisi angka.", true);
            return;
        }

        if (nik.Length != NikLength)
        {
            SetStatus($"NIK harus {NikLength} digit. Jumlah digit saat ini: {nik.Length}.", true);
            return;
        }

        var snapshot = CaptureCurrentImage();

        // Foto belum tersedia: tidak ada yang bisa dienkode.
        if (snapshot is null || !snapshot.HasInput)
        {
            SetStatus("Belum ada gambar untuk diverifikasi. Hubungkan kamera atau upload foto terlebih dahulu.", true);
            return;
        }

        // Yang ditampilkan di kotak "Hasil Verifikasi" adalah snapshot yang sama persis dengan yang
        // dienkode di bawah, sehingga pengguna melihat gambar yang benar-benar diproses.
        CapturedPhoto = snapshot.Display;

        // Pemeriksaan murah sebelum menanggung muat model dlib (~126 MB, ±1,5 detik) pada
        // pemanggilan pertama.
        if (!_services.FaceRecognition.IsReady)
        {
            FaceEncoding = null;
            SetStatus(
                "Model wajah belum tersedia sehingga encoding tidak bisa diproses. " +
                $"Folder yang dicari: {_services.FaceRecognition.ModelDirectory}", true);
            return;
        }

        IsVerifyingFace = true;
        SetStatus("Memproses wajah...", false);

        try
        {
            // Di-await biasa (TANPA ConfigureAwait(false)) supaya kelanjutannya kembali ke UI thread
            // dan SetStatus aman dipanggil. Kerja native dlib sudah dilepas service ke thread pool
            // lewat Task.Run, jadi UI thread tidak membeku selama proses berjalan.
            //
            // Upload memakai byte file apa adanya; kamera memakai buffer BGRA hasil salinan pribadi
            // di CaptureCurrentImage, sehingga keduanya bebas dari manipulasi pixel manual.
            var encoding = snapshot.EncodedBytes is { Length: > 0 }
                ? await _services.FaceRecognition.GetFaceEncodingAsync(snapshot.EncodedBytes)
                : await _services.FaceRecognition.GetFaceEncodingAsync(
                      snapshot.BgraPixels!, snapshot.Width, snapshot.Height, snapshot.Stride);

            // Penjaga kontrak. Service sudah menjamin tepat 128 nilai finite atau melempar, jadi
            // cabang ini praktis tak tercapai; tetap dipasang sebagai pertahanan terakhir sebelum
            // nilainya dipakai tahap berikutnya.
            if (encoding is null || encoding.Count != 128)
            {
                FaceEncoding = null;
                SetStatus("Encoding wajah tidak valid. Jumlah data harus 128.", true);
                return;
            }

            FaceEncoding = encoding;
            SetStatus("Wajah berhasil diproses.", false);
        }
        catch (NoFaceDetectedExceptions)
        {
            FaceEncoding = null;
            SetStatus("Wajah tidak ditemukan pada foto.", true);
        }
        catch (MultipleFacesDetectedExceptions)
        {
            FaceEncoding = null;
            SetStatus("Terdapat lebih dari satu wajah pada foto. Pastikan hanya satu wajah yang terlihat.", true);
        }
        catch (FaceEncodingExceptions)
        {
            FaceEncoding = null;
            SetStatus("Encoding wajah gagal diproses.", true);
        }
        catch (OperationCanceledException)
        {
            FaceEncoding = null;
            SetStatus("Proses wajah dibatalkan.", true);
        }
        catch (FaceRecognitionExceptions ex)
        {
            // Termasuk FaceModelMissingExceptions dan FaceImageExceptions: pesan dari service sudah
            // memuat path yang dicari dan perintah pemulihan, jadi ditampilkan apa adanya.
            FaceEncoding = null;
            SetStatus($"Pengenalan wajah gagal: {ex.Message}", true);
        }
        catch (Exception ex)
        {
            // Kegagalan native di luar hierarki FaceRecognitionExceptions tidak boleh menjatuhkan
            // aplikasi dari sebuah command UI.
            FaceEncoding = null;
            SetStatus($"Pengenalan wajah gagal: {ex.Message}", true);
        }
        finally
        {
            IsVerifyingFace = false;
        }
    }

    /// <summary>
    /// Tombol verifikasi hanya aktif bila ada yang bisa diverifikasi (kamera berjalan atau ada foto
    /// upload) dan tidak ada proses encoding yang sedang berjalan. <see cref="IsVerifyingFace"/>
    /// dihubungkan lewat <c>NotifyCanExecuteChangedFor</c>, jadi tombol nonaktif sendiri selama
    /// proses berjalan tanpa perlu mekanisme loading baru di AXAML.
    /// </summary>
    private bool CanVerifyFace() => IsPreviewVisible && !IsVerifyingFace;

    /// <summary>
    /// Menyalin gambar yang sedang jadi sumber verifikasi menjadi <see cref="ImageSnapshot"/>.
    /// Null bila tidak ada yang bisa dipakai.
    ///
    /// Untuk sumber live yang diambil adalah frame terakhir yang diterima dari kamera, bukan
    /// bitmap yang sedang tampil: penggambaran frame berikutnya sengaja ditunda/dibuang saat UI
    /// thread masih sibuk, sehingga bitmap yang tampil bisa tertinggal beberapa frame. Buffer
    /// staging selalu berisi frame terbaru karena isinya diperbarui sebelum penundaan itu.
    /// </summary>
    private ImageSnapshot? CaptureCurrentImage()
    {
        // Foto upload: yang diverifikasi adalah foto itu sendiri, kamera tidak disentuh sama sekali.
        // Byte aslinya diteruskan apa adanya ke service, jadi tidak ada decode/encode ulang di sini.
        if (IsShowingUploadedPhoto && UploadedPhoto is not null)
        {
            return new ImageSnapshot
            {
                Display = UploadedPhoto,
                EncodedBytes = _uploadedPhotoBytes,
            };
        }

        byte[] pixels;
        int width, height, stride;

        lock (_frameLock)
        {
            width = _stagingWidth;
            height = _stagingHeight;
            stride = _stagingStride;

            if (width <= 0 || height <= 0 || stride <= 0 || _staging.Length < stride * height)
                return null;

            // Disalin di dalam kunci supaya isinya tidak berubah saat dibaca. Salinan pribadi ini
            // juga yang membuat buffer tetap aman dipakai selama await, walau stream terus berjalan.
            pixels = new byte[stride * height];
            Buffer.BlockCopy(_staging, 0, pixels, 0, pixels.Length);
        }

        // Pixel kamera sudah BGRA8888 — format yang sama dipakai CreateStillBitmap dan seluruh
        // pipeline preview — jadi buffer ini langsung cocok dengan overload pixel milik
        // IFaceRecognitionService tanpa konversi urutan kanal apa pun.
        return new ImageSnapshot
        {
            Display = CreateStillBitmap(pixels, width, height, stride),
            BgraPixels = pixels,
            Width = width,
            Height = height,
            Stride = stride,
        };
    }

    /// <summary>
    /// Gambar aktif yang siap diverifikasi: <see cref="Display"/> untuk kotak "Hasil Verifikasi",
    /// dan tepat satu dari <see cref="EncodedBytes"/> (sumber upload) atau <see cref="BgraPixels"/>
    /// (sumber kamera) sebagai masukan mentah <see cref="IFaceRecognitionService"/>. Menyimpan
    /// keduanya dalam satu snapshot menjamin gambar yang dilihat pengguna adalah gambar yang
    /// benar-benar dienkode.
    /// </summary>
    private sealed record ImageSnapshot
    {
        /// <summary>Gambar untuk ditampilkan. Selalu terisi pada snapshot yang valid.</summary>
        public required IImage Display { get; init; }

        /// <summary>Byte file upload (JPEG/PNG) apa adanya, atau null untuk sumber kamera.</summary>
        public byte[]? EncodedBytes { get; init; }

        /// <summary>Buffer pixel BGRA frame kamera, atau null untuk sumber upload.</summary>
        public byte[]? BgraPixels { get; init; }

        /// <summary>Lebar pixel frame kamera.</summary>
        public int Width { get; init; }

        /// <summary>Tinggi pixel frame kamera.</summary>
        public int Height { get; init; }

        /// <summary>Jarak antar-baris frame kamera dalam byte.</summary>
        public int Stride { get; init; }

        /// <summary>
        /// True bila ada masukan mentah yang bisa dienkode. Menghindari melempar buffer kosong ke
        /// service, yang pesannya ("Gambar terlalu kecil untuk deteksi wajah") akan menyesatkan.
        /// </summary>
        public bool HasInput =>
            EncodedBytes is { Length: > 0 } ||
            (BgraPixels is { Length: > 0 } && Width > 0 && Height > 0 && Stride >= Width * 4);
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
