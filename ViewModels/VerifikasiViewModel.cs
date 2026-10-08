using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaFingerprint.Converters;
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
using Telerik.Reporting;

namespace AvaloniaFingerprint.ViewModels;

/// <summary>
/// Halaman <b>Verifikasi</b> — penggabungan "Verifikasi Fingerprint" dan "Verifikasi Wajah" dalam
/// satu tab. Operator mengisi NIK / No. BPJS sekali di bagian atas, memilih metode, dan panel
/// input berubah mengikuti metode itu; kartu "Hasil Verifikasi" di kanan dipakai bersama oleh
/// keduanya.
///
/// Metode <b>Fingerprint</b> adalah port WinForms <c>VerifikasiFingerprint</c>: capture lewat
/// <see cref="FingerprintServices"/> lalu <c>POST /frista/verify</c> (endpoint, payload, dan model
/// respon tidak berubah), diikuti cetak barcode / thermal / laser bila diaktifkan appsettings.
///
/// Metode <b>Wajah</b> menampilkan gambar dari live view kamera (<see cref="CameraServices"/>) atau
/// foto yang diupload, lalu memprosesnya menjadi face encoding 128 nilai lewat
/// <see cref="IFaceRecognitionService"/>; pengiriman ke API Frista belum termasuk.
///
/// Kedua metode memakai service yang sudah ada — tidak ada service maupun endpoint baru. Tidak ada
/// data biometrik yang disimpan: sidik jari, foto, dan encoding wajah hanya hidup di memori dan
/// dilepas saat reset, ganti metode, atau logout.
/// </summary>
public sealed partial class VerifikasiViewModel : ViewModelBase
{
    /// <summary>Panjang NIK yang diterima — 16 digit (keterangan "NIK 16 digit").</summary>
    private const int NikLength = 16;

    /// <summary>Panjang No. BPJS yang diterima — 13 digit (keterangan "No. BPJS 13 digit").</summary>
    private const int BpjsLength = 13;

    /// <summary>Satu-satunya waktu tunggu yang diperkenalkan di halaman ini: capture sidik jari.</summary>
    private static readonly TimeSpan VerifyTimeoutMs = TimeSpan.FromSeconds(15);

    // Dipakai untuk fingerprint, kamera, sesi, dan pengenalan wajah (FaceRecognition); pengiriman
    // face encoding ke API Frista belum termasuk tahap ini.
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

    public VerifikasiViewModel(AppServices services)
    {
        _services = services;
        IsLoggedIn = services.Session.IsLoggedIn;
        DeviceConnected = services.Fingerprint.IsDeviceConnected;

        // Frame kamera dilanggani langsung dari service karena datang per frame. Status perangkat
        // fingerprint dan sesi TIDAK dilanggani di sini: MainWindowViewModel adalah satu-satunya
        // pelanggan keduanya dan meneruskannya lewat OnDeviceStatusChanged / OnSessionChanged,
        // jadi tidak pernah ada dua jalur yang saling menimpa.
        _services.Camera.FrameReady += OnFrameReady;

        OnCameraStatusChanged(
            _services.Camera.IsConnected,
            _services.Camera.ResolveStatus(),
            _services.Camera.ResolveDetail());
    }

    /// <summary>
    /// Dinaikkan saat kondisi pemakaian kamera berubah, yaitu saat operator berpindah metode.
    /// Kamera hanya boleh menyala selama metode Wajah yang dipilih, dan
    /// <see cref="MainWindowViewModel"/> adalah satu-satunya pemilik siklus hidup stream — jadi
    /// halaman ini memberi tahu lewat event, bukan menyalakan/mematikan kamera sendiri.
    /// </summary>
    public event Action? CameraUsageChanged;

    // ---- metode verifikasi ----------------------------------------------------

    /// <summary>
    /// Metode yang sedang dipilih; default <b>Fingerprint</b>. Kedua flag dibuat saling meniadakan
    /// dengan logika di <c>OnIsFingerprintModeChanged</c>/<c>OnIsWajahModeChanged</c>, sehingga dua
    /// RadioButton di AXAML cukup terikat dua arah tanpa logika tambahan di view.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCameraCommand))]
    [NotifyPropertyChangedFor(nameof(ResultPlaceholderLabel))]
    private bool _isFingerprintMode = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCameraCommand))]
    [NotifyPropertyChangedFor(nameof(ResultPlaceholderLabel))]
    private bool _isWajahMode;

    partial void OnIsFingerprintModeChanged(bool value)
    {
        if (!value)
            return;

        if (IsWajahMode) IsWajahMode = false;
        EnterFingerprintMode();
    }

    partial void OnIsWajahModeChanged(bool value)
    {
        if (!value)
            return;

        if (IsFingerprintMode) IsFingerprintMode = false;
        EnterWajahMode();
    }

    /// <summary>
    /// Operator berpindah ke Fingerprint: seluruh state khusus wajah dibuang dan stream kamera
    /// dimatikan (lewat <see cref="CameraUsageChanged"/>), supaya tidak ada capture yang tetap
    /// berjalan di belakang layar. Nomor identitas sengaja dipertahankan karena field itu dipakai
    /// bersama oleh kedua metode — identitas peserta tidak perlu diketik ulang.
    /// </summary>
    private void EnterFingerprintMode()
    {
        // Encoding wajah tidak boleh tertinggal dianggap valid setelah metodenya ditinggalkan.
        FaceEncoding = null;
        CapturedPhoto = null;
        UploadedPhoto = null;
        _uploadedPhotoBytes = null;
        IsShowingUploadedPhoto = false;
        ClearFrames();

        // Hasil verifikasi wajah tidak boleh terbaca sebagai hasil fingerprint.
        ResetResult();
        SetStatus(string.Empty, false);

        CameraUsageChanged?.Invoke();
    }

    /// <summary>
    /// Operator berpindah ke Wajah: gambar dan hasil sidik jari dibuang, dan status pemindaian
    /// dihentikan supaya panel progres tidak tertinggal menyala. Perangkat pembaca sengaja TIDAK
    /// diputus — <see cref="DeviceConnected"/> juga dipakai indikator Header, dan memutus reader
    /// hanya karena operator berpindah metode bukan perilaku yang diinginkan.
    /// </summary>
    private void EnterWajahMode()
    {
        FingerImage = null;
        IsScanning = false;
        ScanStatus = string.Empty;

        ResetResult();
        SetStatus(string.Empty, false);

        CameraUsageChanged?.Invoke();
    }

    // ---- state bersama ---------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private bool _isLoggedIn;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private bool _deviceConnected;

    /// <summary>
    /// True selama verifikasi sidik jari berjalan. Bersama <see cref="IsVerifyingFace"/> dan
    /// <see cref="IsConnecting"/> inilah penjaga "tidak ada dua proses berjalan bersamaan": tiap
    /// CanExecute memeriksa ketiganya, sehingga tombol metode lain ikut mati selama satu metode
    /// sedang bekerja.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCameraCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _scanStatus = string.Empty;
    [ObservableProperty] private bool _isScanning;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasStatus;
    [ObservableProperty] private bool _statusIsError;

    // ---- input peserta -------------------------------------------------------

    /// <summary>
    /// Nomor identitas peserta — <b>satu field bersama</b> untuk kedua metode dan kedua jenis
    /// nomor. Radio "Jenis Nomor" sudah dihapus, jadi tidak ada lagi field kedua yang menyimpan
    /// nomor No. BPJS: jenis nomornya ditentukan dari panjang isi field ini oleh
    /// <see cref="ResolvedJnsFilter"/>. Nilainya dipertahankan saat operator berpindah metode dan
    /// hanya dikosongkan saat reset atau logout.
    /// </summary>
    /// <remarks>
    /// Sengaja tanpa <c>[NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]</c>: tombol harus
    /// tetap bisa ditekan saat nomor masih kosong supaya pesan validasinya benar-benar tampil,
    /// bukan tombol yang mati tanpa penjelasan. <c>VerifyCommand</c> (fingerprint) tetap diberi
    /// tahu, karena isi field inilah yang menentukan tombol boleh hidup atau tidak.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private string _identityNumber = string.Empty;

    /// <summary>
    /// Menyaring masukan nomor: hanya angka ASCII dan maksimal <see cref="NikLength"/> karakter
    /// (panjang NIK, yang terbesar di antara kedua jenis nomor), sehingga hasil tempel yang
    /// mengandung huruf atau spasi langsung bersih. Idempoten — bila hasilnya sama dengan nilai
    /// yang masuk tidak ada penugasan ulang, jadi mengetik angka tidak menggeser posisi kursor.
    /// </summary>
    partial void OnIdentityNumberChanged(string value)
    {
        var cleaned = new string(value.Where(char.IsAsciiDigit).Take(NikLength).ToArray());
        if (cleaned != value)
            IdentityNumber = cleaned;
    }

    /// <summary>
    /// Jenis nomor ditentukan dari panjangnya, menggantikan radio "Jenis Nomor": 16 digit = NIK
    /// (<c>"nik"</c>), 13 digit = No. BPJS (<c>"psnoka"</c>). Nilai inilah yang dikirim sebagai
    /// <c>jnsFilter</c> pada <c>POST /frista/verify</c>. Panjang lain tidak bisa dipastikan
    /// jenisnya, jadi dikosongkan.
    /// </summary>
    private string ResolvedJnsFilter => IdentityNumber.Length switch
    {
        NikLength => "nik",
        BpjsLength => "psnoka",
        _ => string.Empty
    };

    /// <summary>
    /// Nomor dianggap valid hanya bila panjangnya pasti menandai NIK (16 digit) atau No. BPJS
    /// (13 digit). Panjang ambigu (mis. 14–15 digit) sengaja ditolak supaya verifikasi tidak
    /// dijalankan dengan jenis nomor yang salah.
    /// </summary>
    private bool IsIdentityValid => ResolvedJnsFilter.Length > 0;

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
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private bool _isConnecting;

    /// <summary>
    /// True selama encoding wajah berjalan. Menonaktifkan tombol "Verifikasi Wajah" supaya klik
    /// berulang tidak menumpuk pekerjaan native dlib — pola yang sama dengan
    /// <see cref="IsConnecting"/> pada tombol "Hubungkan Kamera".
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private bool _isVerifyingFace;

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
    [NotifyPropertyChangedFor(nameof(HasResultImage))]
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

    // ---- hasil verifikasi (satu kartu untuk kedua metode) ----------------------
    // Nilai default "-" berarti belum ada data; diisi dari respon API oleh metode yang sedang
    // aktif. Kartu hasil hanya satu, jadi kedua metode menulis ke properti yang sama.

    [ObservableProperty] private string _resultNama = "-";
    [ObservableProperty] private string _resultNoKartu = "-";
    [ObservableProperty] private string _resultUsia = "-";
    [ObservableProperty] private string _resultKeterangan = "-";
    [ObservableProperty] private bool _hasResult;

    /// <summary>
    /// Foto sidik jari hasil capture berupa base64 PNG dari <see cref="FingerprintServices"/>,
    /// diubah menjadi gambar oleh Base64Image converter di AXAML; null berarti placeholder.
    /// Hanya hidup di memori — dibuang saat reset, ganti metode, atau logout.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultImage))]
    private string? _fingerImage;

    /// <summary>
    /// Label area gambar di kartu Hasil Verifikasi. Kartu ini satu dan dipakai bersama kedua
    /// metode, jadi hanya teksnya yang berbeda: "Sidik jari" atau "Wajah". Sengaja TIDAK ada
    /// emoji/icon khusus salah satu metode, supaya visual kedua metode identik.
    /// </summary>
    public string ResultPlaceholderLabel => IsWajahMode ? "Wajah" : "Sidik jari";

    /// <summary>
    /// True bila area gambar kartu hasil sudah berisi sesuatu (sidik jari hasil capture atau foto
    /// hasil verifikasi wajah), sehingga placeholder disembunyikan. Satu penanda untuk kedua
    /// metode, supaya aturan tampil/sembunyi placeholder tidak berbeda antar metode.
    /// </summary>
    public bool HasResultImage => FingerImage is not null || CapturedPhoto is not null;

    /// <summary>
    /// Vektor embedding wajah 128 nilai hasil <see cref="IFaceRecognitionService"/>, disimpan untuk
    /// tahap berikutnya (API Frista). <see langword="null"/> berarti belum ada encoding yang valid;
    /// setiap jalur gagal mengosongkannya supaya nilai basi tidak terpakai. Sengaja bukan
    /// <c>[ObservableProperty]</c> karena belum ada UI yang mengikatnya.
    /// </summary>
    private List<double>? FaceEncoding { get; set; }

    /// <summary>
    /// Mengembalikan kartu hasil ke keadaan kosong, termasuk gambar sidik jari. Dipakai tombol
    /// Bersihkan, saat metode berpindah, dan saat logout — satu jalur supaya tidak ada data
    /// peserta yang tertinggal di layar.
    /// </summary>
    private void ResetResult()
    {
        ResultNama = "-";
        ResultNoKartu = "-";
        ResultUsia = "-";
        ResultKeterangan = "-";
        FingerImage = null;
        HasResult = false;
    }

    public void OnSessionChanged(bool loggedIn)
    {
        IsLoggedIn = loggedIn;
        if (loggedIn)
            return;

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

        // Gambar sidik jari, nomor yang sudah diketik, dan hasil verifikasinya juga dibuang:
        // semuanya milik peserta sebelumnya dan tidak boleh terbawa ke sesi berikutnya.
        FingerImage = null;
        ResetResult();
        IsScanning = false;
        ScanStatus = string.Empty;

        // Nomor peserta sebelumnya juga dibuang, supaya tidak ikut terverifikasi untuk pasien lain.
        IdentityNumber = string.Empty;

        SetStatus(string.Empty, false);
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

    /// <summary>
    /// Tombol "Hubungkan Kamera" hanya berguna selama metode Wajah yang dipilih, dan tidak boleh
    /// menumpuk dengan scan kamera lain maupun dengan verifikasi sidik jari yang sedang berjalan.
    /// </summary>
    private bool CanConnectCamera() => IsWajahMode && !IsConnecting && !IsBusy;

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
        // Validasi nomor lebih dulu, sebelum gambar disalin atau model dlib disentuh: permintaan yang
        // pasti ditolak tidak boleh menanggung biaya apa pun, dan CapturedPhoto/FaceEncoding tidak
        // boleh berubah oleh verifikasi yang batal berjalan. Aturan panjangnya sama dengan metode
        // Fingerprint: 16 digit (NIK) atau 13 digit (No. BPJS).
        var identity = IdentityNumber.Trim();

        if (identity.Length == 0)
        {
            SetStatus("NIK / No. BPJS masih kosong. Silakan masukkan NIK atau No. BPJS terlebih dahulu.", true);
            return;
        }

        if (!identity.All(char.IsAsciiDigit))
        {
            SetStatus("NIK / No. BPJS hanya boleh berisi angka.", true);
            return;
        }

        if (identity.Length != NikLength && identity.Length != BpjsLength)
        {
            SetStatus(
                $"NIK / No. BPJS harus {NikLength} atau {BpjsLength} digit. " +
                $"Jumlah digit saat ini: {identity.Length}.", true);
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

            // Operator bisa saja berpindah ke metode Fingerprint selama encoding berjalan. Encoding
            // yang baru selesai sesudah itu sudah tidak relevan, jadi dibuang alih-alih disimpan —
            // pola penjaga yang sama dengan pemeriksaan DeviceConnected sesudah capture sidik jari.
            if (!IsWajahMode)
            {
                FaceEncoding = null;
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
    /// Tombol verifikasi wajah hanya aktif selama metode Wajah yang dipilih, ada gambar yang bisa
    /// diverifikasi (kamera berjalan atau ada foto upload), dan tidak ada proses lain yang sedang
    /// berjalan. <see cref="IsVerifyingFace"/> dihubungkan lewat <c>NotifyCanExecuteChangedFor</c>,
    /// jadi tombol nonaktif sendiri selama proses berjalan tanpa perlu mekanisme loading baru di
    /// AXAML.
    /// </summary>
    private bool CanVerifyFace() => IsWajahMode && IsPreviewVisible && !IsVerifyingFace && !IsBusy;

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

    // ---- METODE FINGERPRINT ---------------------------------------------------
    //
    // Port apa adanya dari WinForms VerifikasiFingerprint. Seluruh alur dipertahankan: capture
    // sidik jari -> POST /frista/verify -> validasi nomor antrean -> generate nomor registrasi ->
    // cetak SEP -> cetak Label -> isi kartu hasil. Endpoint, payload, model respon, template
    // laporan, dan nama printer semuanya dipakai kembali tanpa perubahan.
    //
    // Hanya ada penyesuaian yang menjadi konsekuensi langsung penggabungan halaman:
    //   1. nomor yang dikirim diambil dari IdentityNumber, satu field bersama untuk kedua metode
    //      dan kedua jenis nomor (jenisnya ditentukan dari panjang nomor oleh ResolvedJnsFilter);
    //   2. ClearResult() menjadi ResetResult() dan ResultMeta menjadi ResultKeterangan, karena
    //      kartu hasil kini hanya satu dan dipakai bersama oleh kedua metode;
    //   3. dua penjaga IsFingerprintMode ditambahkan agar hasil yang datang sesudah operator
    //      berpindah metode tidak menulis ke kartu hasil milik metode lain.

    /// <summary>
    /// Status perangkat pembaca, diteruskan oleh <see cref="MainWindowViewModel"/> - satu-satunya
    /// pelanggan <c>DeviceStateChanged</c> - memakai nilai yang sama dengan indikator Header.
    /// </summary>
    public void OnDeviceStatusChanged(bool connected)
    {
        DeviceConnected = connected;
        if (!connected && IsScanning)
        {
            IsScanning = false;
            ScanStatus = string.Empty;
            SetStatus("Perangkat fingerprint terputus saat memindai.", true);
        }
    }

    /// <summary>
    /// Tombol "Verifikasi" (fingerprint) hanya aktif selama metode Fingerprint yang dipilih,
    /// operator sudah login, reader siap, tidak ada proses lain berjalan, dan panjang nomor sudah
    /// pasti menandai NIK (16 digit) atau No. BPJS (13 digit). <see cref="IsVerifyingFace"/> dan
    /// <see cref="IsConnecting"/> ikut diperiksa supaya verifikasi wajah tidak pernah berjalan
    /// berbarengan dengan capture jari.
    /// </summary>
    private bool CanVerify() =>
        IsFingerprintMode
        && IsLoggedIn
        && DeviceConnected
        && !IsBusy
        && !IsVerifyingFace
        && !IsConnecting
        && IsIdentityValid;

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync()
    {
        IsBusy = true;
        IsScanning = true;
        ScanStatus = "Letakkan jari pada perangkat...";
        SetStatus(string.Empty, false);

        try
        {
            var capture = await _services.Fingerprint.CaptureAsync(_services.Fingerprint.VerifyTimeoutMs);

            // Operator bisa berpindah ke metode Wajah selama capture berjalan. Hasil yang baru
            // datang sesudah itu sudah tidak relevan, jadi dibuang sebelum menyentuh kartu hasil
            // maupun memicu cetak SEP/Label untuk metode yang sudah ditinggalkan.
            if (!IsFingerprintMode)
                return;

            ScanStatus = "Memverifikasi ke BPJS...";
            FingerImage = capture.ImageBase64;
            var result = await Task.Run(() => _services.Fingerprint.Verify(new Filters.Fingerprint.Verify
            {
                finger = capture.VerificationTemplate,
                keyword = IdentityNumber.Trim(),
                jnsFilter = ResolvedJnsFilter
            }));

            if (!result.succeeded)
            {
                ResetResult();
                SetStatus($"Verifikasi gagal: {string.Join(' ', result.errors)}", true);
                return;
            }

            if (!result.succeeded || (result.errors != null && result.errors.Length > 0))
            {
                ResetResult();
                string message = result.errors.Length > 0 ? string.Join(", ", result.errors) : result.message;
                SetStatus(message, true);
                return;
            }

            ScanStatus = "Memverifikasi kode booking pasien...";
            var validateApt = await Task.Run(() => _services.Appointment.AppointmentNoFromFinger(new Filters.Appointment.AppointmentNoFromFinger
            {
                dateTime = DateTime.Now,
                nik = result.data?.response.nik ?? string.Empty,
                noKartu = result.data?.response.noKartu ?? string.Empty,
                patientName = result.data?.response.nama ?? string.Empty
            }));

            if (!validateApt.succeeded || (validateApt.errors != null && validateApt.errors.Length > 0))
            {
                ResetResult();
                string message = validateApt.errors.Length > 0 ? string.Join(", ", validateApt.errors) : validateApt.message;
                SetStatus(message, true);
                return;
            }

            // Nomor registrasi diminta sebelum cetak SEP karena nomor itu yang dipakai endpoint
            // cetak; kegagalan di sini membatalkan sisa alur.
            ScanStatus = "Memproses SEP ke BPJS...";
            var generateReg = await Task.Run(() => _services.Appointment.GenerateRegistration(validateApt.data ?? string.Empty));
            if (!generateReg.succeeded || (generateReg.errors != null && generateReg.errors.Length > 0))
            {
                ResetResult();
                string message = generateReg.errors.Length > 0 ? string.Join(", ", generateReg.errors) : generateReg.message;
                SetStatus(message, true);
                return;
            }

            var dateTime = DateTime.Now;
            var printerNormal = _services.Settings.Configuration["Apps:PrinterNormal"] ?? string.Empty;
            var printerLaser = _services.Settings.Configuration["Apps:PrinterLaser"] ?? string.Empty;

            // Tahap 1: cetak SEP. Kegagalan di sini hanya dilaporkan lewat SetStatus; cetak Label
            // di bawah tetap dijalankan.
            ScanStatus = "Memproses cetak SEP...";
            var sep = await Task.Run(() => _services.Printout.Sep(validateApt.data ?? string.Empty, true, dateTime));
            if (sep.succeeded && sep.errors == null && sep.data != null)
            {
                try
                {
                    var data = sep.data;
                    List<InstanceReportSource> rptSource = [];
                    List<Models.Master.Parameter> parameters = [];

                    rptSource.Add(new InstanceReportSource
                    {
                        ReportDocument = new ReportLibrary.Sep()
                    });

                    parameters =
                    [
                        new()
                    {
                        name = "sep",
                        value = data.sep
                    },
                    new()
                    {
                        name = "tglsep",
                        value = data.tglsep
                    },
                    new()
                    {
                        name = "nokartu",
                        value = data.nokartu
                    },
                    new()
                    {
                        name = "nama",
                        value = data.nama
                    },
                    new()
                    {
                        name = "tgllahir",
                        value = data.tgllahir
                    },
                    new()
                    {
                        name = "sex",
                        value = data.sex
                    },
                    new()
                    {
                        name = "politujuan",
                        value = data.politujuan
                    },
                    new()
                    {
                        name = "polirujukan",
                        value = data.polirujukan
                    },
                    new()
                    {
                        name = "dokterpemeriksa",
                        value = data.dokterpemeriksa
                    },
                    new()
                    {
                        name = "dpjp",
                        value = data.dpjp
                    },
                    new()
                    {
                        name = "asalfaskes",
                        value = data.asalfaskes
                    },
                    new()
                    {
                        name = "diagnosa",
                        value = data.diagnosa
                    },
                    new()
                    {
                        name = "antrian",
                        value = Converter.IntToString(data.antrian)
                    },
                    new()
                    {
                        name = "jenispeserta",
                        value = data.jenispeserta
                    },
                    new()
                    {
                        name = "rm",
                        value = data.rm
                    },
                    new()
                    {
                        name = "jenisrawat",
                        value = data.jenisrawat
                    },
                    new()
                    {
                        name = "kelasrawat",
                        value = data.kelasrawat
                    },
                    new()
                    {
                        name = "cetakby",
                        value = data.cetakby
                    },
                    new()
                    {
                        name = "notlp",
                        value = data.notlp
                    },
                    new()
                    {
                        name = "urlqrcode",
                        value = data.linkqrcode
                    },
                    new()
                    {
                        name = "queueqr",
                        value = data.queueqr
                    }
                    ];

                    printProcess(new Filters.Printout.Print
                    {
                        rptSource = rptSource,
                        parameters = parameters,
                        printerName = printerNormal,
                        copies = 3
                    });
                }
                catch (Exception e)
                {
                    ResetResult();
                    SetStatus(e.Message, true);
                    return;
                }
            }

            // Tahap 2: cetak Label. Berbeda dari SEP, kegagalan di sini menghentikan seluruh proses.
            ScanStatus = "Memproses cetak Label...";
            string aptNo = $"APT/{Converter.DateFormat(dateTime, DateFormat.Shortyearpattern)}-{validateApt.data}";
            var regNo = await Task.Run(() => _services.Appointment.AppointmentToRegistrationNumber(aptNo));
            if (!regNo.succeeded && regNo.errors != null)
            {
                ResetResult();
                string message = regNo.errors.Length > 0 ? string.Join(", ", regNo.errors) : regNo.message;
                SetStatus(message, true);
                return;
            }

            var label = await Task.Run(() => _services.Printout.Label(regNo.data ?? string.Empty, dateTime));
            if (label.succeeded && label.errors == null && label.data != null)
            {
                try
                {
                    var data = label.data;
                    List<InstanceReportSource> rptSource = [];
                    List<Models.Master.Parameter> parameters = [];

                    rptSource.Add(new InstanceReportSource
                    {
                        ReportDocument = new ReportLibrary.Barcode()
                    });

                    parameters =
                    [
                        new()
                        {
                            name = "nama",
                            value = data.nama
                        },
                        new()
                        {
                            name = "tgllahir",
                            value = data.tgllhr
                        },
                        new()
                        {
                            name = "sex",
                            value = data.lahirjk.Equals("m", StringComparison.CurrentCultureIgnoreCase) ? "Laki - Laki" : "Perempuan"
                        },
                        new()
                        {
                            name = "rm",
                            value = data.mr
                        },
                        new()
                        {
                            name = "noreg",
                            value = $"{data.mr} / {data.noreg}"
                        },
                        new()
                        {
                            name = "noantrian",
                            value = Converter.IntToString(data.noantrian)
                        }
                    ];

                    printProcess(new Filters.Printout.Print
                    {
                        rptSource = rptSource,
                        parameters = parameters,
                        printerName = printerLaser
                    });
                }
                catch (Exception e)
                {
                    ResetResult();
                    SetStatus(e.Message, true);
                    return;
                }
            }

            // Penjaga yang sama sebelum kartu hasil ditulis: rangkaian cetak di atas bisa
            // memakan waktu, dan metode bisa saja sudah berpindah saat itu terjadi.
            if (!IsFingerprintMode)
                return;

            ResultNama = result.data?.response.nama ?? "-";
            ResultNoKartu = result.data?.response.noKartu ?? "-";
            ResultUsia = $"{result.data?.response.usia} Tahun";
            ResultKeterangan = $"Code {result.data?.metaData?.code} · {result.data?.metaData?.message}";
            HasResult = true;
            SetStatus("Verifikasi berhasil.", false);
        }
        catch (FingerprintExceptions ex)
        {
            SetStatus(ex.Message, true);
            await _services.Fingerprint.RefreshDeviceAsync();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, true);
        }
        finally
        {
            IsScanning = false;
            ScanStatus = string.Empty;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Tombol "Bersihkan" di panel fingerprint: mengosongkan nomor identitas, gambar sidik jari,
    /// dan kartu hasil. <see cref="IdentityNumber"/> ikut dikosongkan karena field itulah yang
    /// menentukan jenis nomor sekaligus nomor yang diverifikasi - persis isi field yang
    /// dibersihkan tombol ini di halaman lama.
    /// </summary>
    [RelayCommand]
    private void Clear()
    {
        IdentityNumber = string.Empty;
        ResetResult();
        SetStatus(string.Empty, false);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
        HasStatus = !string.IsNullOrEmpty(message);
    }
}
