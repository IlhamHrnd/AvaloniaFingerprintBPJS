using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaFingerprint.Exceptions;
using FaceRecognitionDotNet;
using Microsoft.Extensions.Configuration;
using OpenCvSharp;

// Alias dipakai karena FaceRecognitionDotNet punya tipe bernama generik (Image, Model,
// Mode, Location, Point) yang gampang bertabrakan dengan OpenCvSharp / System.Drawing /
// namespace AvaloniaFingerprint.Models di file yang sama.
using DetectorModel = FaceRecognitionDotNet.Model;
using FaceEncoding = FaceRecognitionDotNet.FaceEncoding;
using FaceImage = FaceRecognitionDotNet.Image;
using ImageMode = FaceRecognitionDotNet.Mode;

namespace AvaloniaFingerprint.Services;

/// <summary>
/// Menghasilkan vektor embedding wajah 128 dimensi memakai FaceRecognitionDotNet (dlib).
/// <para>
/// Service ini berdiri sendiri dan TIDAK menyentuh alur verifikasi wajah yang sudah ada
/// (<see cref="ViewModels.VerifikasiWajahViewModel"/>, perintah <c>VerifyFace</c>) maupun
/// <see cref="FingerprintServices"/> yang khusus sidik jari.
/// Ia hanya membaca gambar dan mengembalikan <see cref="List{T}"/> berisi 128 <see cref="double"/>;
/// integrasi ke alur BPJS diserahkan ke pemanggil.
/// </para>
/// <para>
/// Model dlib (~126 MB) dimuat malas pada pemanggilan pertama, bukan di konstruktor, supaya
/// startup app tetap cepat dan tidak ada ~500 MB native heap terpakai sebelum fitur wajah dibuka.
/// </para>
/// </summary>
public sealed class FaceRecognitionService : BaseService, IFaceRecognitionService
{
    /// <summary>Panjang vektor yang dihasilkan dlib ResNet v1. Kontrak publik service ini.</summary>
    public const int EncodingLength = 128;

    /// <summary>
    /// Keempat file ini wajib ada di <see cref="ModelDirectory"/>.
    /// <c>FaceRecognition.Create()</c> memvalidasi semuanya dan melempar
    /// <see cref="FileNotFoundException"/> bila salah satunya hilang - termasuk
    /// <c>shape_predictor_68_face_landmarks.dat</c> yang TIDAK dipakai saat
    /// <c>LandmarkModel</c> disetel ke <c>"Small"</c>.
    /// </summary>
    private static readonly string[] RequiredModelFiles =
    [
        "mmod_human_face_detector.dat",
        "shape_predictor_5_face_landmarks.dat",
        "shape_predictor_68_face_landmarks.dat",
        "dlib_face_recognition_resnet_model_v1.dat",
    ];

    /// <summary>Perintah pemulihan yang disisipkan ke setiap pesan <see cref="FaceModelMissingExceptions"/>.</summary>
    private const string DownloadHint =
        "Jalankan: powershell -ExecutionPolicy Bypass -File tools\\download-face-assets.ps1";

    /// <summary>
    /// Handle native dlib tidak thread-safe, jadi seluruh kerja native diserialisasi lewat gate
    /// ini. Bonus: tidak pernah ada lebih dari satu pekerjaan wajah berjalan bersamaan.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly string _modelDirectory;
    private readonly DetectorModel _detector;
    private readonly int _upSample;
    private readonly PredictorModel _landmarkModel;
    private readonly int _numJitterings;
    private readonly int _maxEdge;

    private FaceRecognition? _faceRecognition;
    private bool _disposed;

    /// <param name="configuration">Dipakai untuk membaca section <c>FaceRecognition</c>.</param>
    /// <param name="session">Diteruskan ke <see cref="BaseService"/> agar konsisten dengan service lain; tidak dipakai di sini.</param>
    public FaceRecognitionService(IConfiguration configuration, SessionService session)
        : base(configuration, session)
    {
        _modelDirectory = ResolveModelDirectory(configuration["FaceRecognition:ModelDirectory"]);
        _detector       = ParseDetector(configuration["FaceRecognition:Detector"]);
        _upSample       = ParsePositiveInt(configuration["FaceRecognition:Upsample"], 0, 0, 4);
        _landmarkModel  = ParseLandmarkModel(configuration["FaceRecognition:LandmarkModel"]);
        _numJitterings  = ParsePositiveInt(configuration["FaceRecognition:NumJitterings"], 1, 0, 8);
        _maxEdge        = ParsePositiveInt(configuration["FaceRecognition:MaxEdge"], 800, 0, 8192);
    }

    /// <inheritdoc/>
    public string ModelDirectory => _modelDirectory;

    /// <inheritdoc/>
    public bool IsReady =>
        _faceRecognition is not null
        || (Directory.Exists(_modelDirectory)
            && RequiredModelFiles.All(f => File.Exists(Path.Combine(_modelDirectory, f))));

    // ------------------------------------------------------------------
    // Konfigurasi
    // ------------------------------------------------------------------

    /// <summary>
    /// Menyelesaikan <c>FaceRecognition:ModelDirectory</c>. Path relatif diselesaikan terhadap
    /// <see cref="AppContext.BaseDirectory"/> (folder output build), BUKAN
    /// <see cref="Directory.GetCurrentDirectory()"/> - keduanya berbeda saat app diluncurkan
    /// dari shortcut atau lewat <c>dotnet run</c> di direktori lain.
    /// </summary>
    private static string ResolveModelDirectory(string? configured)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? "FaceModels" : configured.Trim();
        return Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, value));
    }

    /// <summary>
    /// Memetakan <c>FaceRecognition:Detector</c> ke <see cref="DetectorModel"/>; nilai tak
    /// dikenal jatuh ke <c>Cnn</c>.
    /// <para>
    /// <c>Cnn</c> dipakai sebagai default karena <c>Hog</c> terbukti gagal mendeteksi wajah
    /// pada frame kamera 640x480 di pengukuran benchmark (lihat README), sementara <c>Cnn</c>
    /// berhasil di semua ukuran yang diuji. Konsekuensinya biaya: sekitar 5-14 detik per
    /// gambar di mesin pengembangan. Naikkan <c>MaxEdge</c> ke 0 dan <c>Detector</c> ke
    /// <c>Hog</c> hanya bila kecepatan lebih penting daripada ketahanan pose/pencahayaan.
    /// </para>
    /// </summary>
    private static DetectorModel ParseDetector(string? configured) =>
        string.Equals(configured?.Trim(), "Hog", StringComparison.OrdinalIgnoreCase)
            ? DetectorModel.Hog
            : DetectorModel.Cnn;

    /// <summary>
    /// Memetakan <c>FaceRecognition:LandmarkModel</c>. <see cref="PredictorModel.Small"/>
    /// (shape_predictor_5) 17x lebih kecil dari <c>Large</c> (shape_predictor_68) dan nyaris
    /// tidak mengubah akurasi encoding, jadi dipakai sebagai default.
    /// </summary>
    private static PredictorModel ParseLandmarkModel(string? configured) =>
        string.Equals(configured?.Trim(), "Large", StringComparison.OrdinalIgnoreCase)
            ? PredictorModel.Large
            : PredictorModel.Small;

    /// <summary>
    /// Membaca int dari konfigurasi dengan fallback dan clamp. Sengaja tidak melempar: nilai
    /// konfigurasi yang salah ketik tidak boleh membuat fitur wajah mati total.
    /// </summary>
    private static int ParsePositiveInt(string? configured, int fallback, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return fallback;

        if (!int.TryParse(configured.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return fallback;

        return Math.Clamp(value, min, max);
    }

    // ------------------------------------------------------------------
    // API publik
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<List<double>> GetFaceEncodingAsync(byte[] encodedImage, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (encodedImage is null)
            throw new FaceImageExceptions("Byte gambar null. Pastikan file berhasil dibaca sebelum meminta encoding wajah.");
        if (encodedImage.Length == 0)
            throw new FaceImageExceptions("Byte gambar kosong (0 byte).");

        // byte[] ini milik pemanggil dan TIDAK dipinjam dari ArrayPool (beda dengan
        // CameraFrameEventArgs.Pixels), jadi aman dibaca di thread pool setelah await.
        return EncodeEncodedAsync(encodedImage, $"gambar {encodedImage.Length:N0} byte", cancellationToken);
    }

    /// <inheritdoc/>
    public Task<List<double>> GetFaceEncodingAsync(byte[] bgraPixels, int width, int height, int stride, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateBgra(bgraPixels!, width, height, stride);

        var effectiveStride = stride == 0 ? width * 4 : stride;

        // Konversi dilakukan SEKARANG, sinkron di thread pemanggil, sebelum ada await apa pun:
        // buffer CameraFrameEventArgs.Pixels berasal dari ArrayPool dan boleh dikembalikan
        // segera setelah method ini mengembalikan Task-nya.
        var rgb = BgraToRgb(bgraPixels!, width, height, effectiveStride);

        return WithGateAsync(faceRecognition => Task.Run(
            () => Encode(faceRecognition, rgb, cancellationToken),
            cancellationToken), cancellationToken);
    }

    /// <inheritdoc/>
    public Task<List<double>> GetFaceEncodingFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(filePath))
            throw new FaceImageExceptions("Path gambar kosong.");
        if (!File.Exists(filePath))
            throw new FaceImageExceptions($"File gambar tidak ditemukan: {filePath}");

        var resolved = Path.GetFullPath(filePath);

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(resolved);
        }
        catch (Exception ex)
        {
            throw new FaceImageExceptions($"Gagal membaca '{resolved}': {ex.Message}", ex);
        }

        if (bytes.Length == 0)
            throw new FaceImageExceptions($"File '{resolved}' kosong (0 byte).");

        return EncodeEncodedAsync(bytes, resolved, cancellationToken);
    }

    /// <summary>
    /// Inti bersama jalur "gambar terkode" (byte JPEG/PNG di memori maupun file di disk).
    /// <para>
    /// Sengaja hanya ada SATU jalur. Sebelumnya file didekode oleh dlib
    /// (<c>FaceRecognition.LoadImageFile</c>) sementara byte didekode oleh OpenCV, sehingga dua
    /// decoder berbeda bisa menghasilkan pixel yang sedikit berbeda untuk gambar yang sama dan
    /// vektornya pun berbeda tipis. Dengan satu decoder, encoding dari file dan dari byte-nya
    /// dijamin identik bit-per-bit - syarat penting bila vektor ini nanti dibandingkan 1:1.
    /// </para>
    /// </summary>
    /// <param name="encodedImage">Byte JPEG/PNG/BMP/TIFF/WebP yang utuh.</param>
    /// <param name="source">Deskripsi sumber untuk pesan error (path file atau ukuran byte).</param>
    private Task<List<double>> EncodeEncodedAsync(byte[] encodedImage, string source, CancellationToken cancellationToken)
    {
        return WithGateAsync(faceRecognition => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using Mat decoded = Cv2.ImDecode(encodedImage, ImreadModes.Color);
            if (decoded is null || decoded.Empty())
                throw new FaceImageExceptions(
                    $"'{source}' tidak bisa didekode sebagai gambar. Format yang didukung: JPEG, PNG, BMP, TIFF, WebP.");

            // Cv2.ImDecode(..., Color) menghasilkan BGR, sedangkan dlib hanya menerima
            // RGB atau Greyscale - tidak ada mode Bgr. Konversi ini wajib.
            using Mat rgb = new();
            Cv2.CvtColor(decoded, rgb, ColorConversionCodes.BGR2RGB);

            return Encode(faceRecognition, Prepare(rgb), cancellationToken);
        }, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Menghitung jarak Euclidean antar dua vektor 128 dimensi. Bukan bagian dari kontrak
    /// encoding, tapi berguna untuk smoke test dan sebagai referensi bila nanti dibutuhkan
    /// perbandingan 1:1. Ambang dlib untuk "orang yang sama" adalah 0.6.
    /// </summary>
    public static double EuclideanDistance(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Count != right.Count)
            throw new ArgumentException($"Panjang vektor berbeda: {left.Count} vs {right.Count}.");

        var sum = 0d;
        for (var i = 0; i < left.Count; i++)
        {
            var delta = left[i] - right[i];
            sum += delta * delta;
        }

        return Math.Sqrt(sum);
    }

    // ------------------------------------------------------------------
    // Inti: inisialisasi native, deteksi, encoding
    // ------------------------------------------------------------------

    /// <summary>
    /// Menjalankan <paramref name="work"/> di bawah gate serialisasi setelah model dimuat.
    /// Inisialisasi sengaja dilakukan di sini (malas) dan di dalam gate supaya pemanggilan
    /// pertama dari dua thread sekaligus tidak memuat 126 MB model dua kali.
    /// </summary>
    private async Task<List<double>> WithGateAsync(Func<FaceRecognition, Task<List<double>>> work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await work(EnsureInitialized()).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Memuat model dlib sekali, dengan pesan error yang menyebutkan file mana yang hilang dan
    /// cara mengunduhnya. Pemanggil wajib menahan gate.
    /// </summary>
    private FaceRecognition EnsureInitialized()
    {
        if (_faceRecognition is not null)
            return _faceRecognition;

        if (!Directory.Exists(_modelDirectory))
            throw new FaceModelMissingExceptions(
                $"Folder model dlib tidak ditemukan: '{_modelDirectory}'. {DownloadHint}");

        var missing = RequiredModelFiles
            .Where(file => !File.Exists(Path.Combine(_modelDirectory, file)))
            .ToArray();

        if (missing.Length > 0)
            throw new FaceModelMissingExceptions(
                $"File model dlib tidak ditemukan di '{_modelDirectory}': {string.Join(", ", missing)}. {DownloadHint}");

        try
        {
            _faceRecognition = FaceRecognition.Create(_modelDirectory);
        }
        catch (Exception ex)
        {
            // Kegagalan di titik ini hampir selalu berarti native library tidak termuat
            // (build x86, atau runtimes\win-x64\native tidak ikut ter-copy).
            throw new FaceModelMissingExceptions(
                $"Gagal menginisialisasi FaceRecognitionDotNet dari '{_modelDirectory}': {ex.GetType().Name}: {ex.Message}. " +
                $"Pastikan proses berjalan sebagai x64 karena native package hanya menyediakan win-x64. {DownloadHint}",
                ex);
        }

        return _faceRecognition;
    }

    /// <summary>Buffer RGB rapat (3 byte per pixel, tanpa padding baris) siap diserahkan ke dlib.</summary>
    private sealed record RgbImage(byte[] Pixels, int Width, int Height);

    /// <summary>
    /// Membuat <see cref="FaceImage"/> dari buffer RGB milik sendiri lalu menjalankan
    /// deteksi + encoding. Method ini instance karena <see cref="DetectAndEncode"/> membaca
    /// setelan detektor dari field instance.
    /// </summary>
    private List<double> Encode(FaceRecognition faceRecognition, RgbImage rgb, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // PENTING - urutan parameter FaceRecognitionDotNet adalah
        //   LoadImage(byte[] array, int ROW, int COLUMN, int STRIDE, Mode mode)
        // bukan (width, height, channels). Baris = tinggi, kolom = lebar, dan stride adalah
        // jumlah BYTE per baris. Salah urutan di sini tidak selalu gagal keras: kadang hanya
        // menghasilkan gambar ter-transpose yang diam-diam merusak deteksi.
        // Library juga memvalidasi stride >= column dan row * stride <= array.Length, yang
        // keduanya terpenuhi karena buffer RGB kita rapat (Width * 3 byte per baris).
        using FaceImage image = FaceRecognition.LoadImage(rgb.Pixels, rgb.Height, rgb.Width, rgb.Width * 3, ImageMode.Rgb);
        return DetectAndEncode(faceRecognition, image, cancellationToken);
    }

    /// <summary>
    /// Memperkecil gambar bila sisi terpanjangnya melebihi <c>FaceRecognition:MaxEdge</c>, lalu
    /// menyalinnya menjadi byte RGB rapat.
    /// <para>
    /// Pembatasan ini adalah tuas kinerja terpenting. Biaya detektor CNN tumbuh sebanding jumlah
    /// pixel: pengukuran di mesin pengembangan (lihat README) menunjukkan foto 910x1137 memakan
    /// 14.3 detik pada resolusi asli tetapi hanya 7.3 detik setelah dibatasi ke 800 px, dengan
    /// hasil deteksi yang sama. Aman karena dlib menormalkan chip wajah ke 150x150 sebelum
    /// memasukkannya ke ResNet, jadi detail di atas itu praktis tidak menambah akurasi.
    /// </para>
    /// </summary>
    /// <param name="rgb">Mat 3-channel dengan urutan warna RGB.</param>
    private RgbImage Prepare(Mat rgb)
    {
        var longest = Math.Max(rgb.Cols, rgb.Rows);
        if (_maxEdge <= 0 || longest <= _maxEdge)
            return new RgbImage(CopyToRgbBytes(rgb), rgb.Cols, rgb.Rows);

        var scale = _maxEdge / (double)longest;
        var target = new Size(
            Math.Max(1, (int)Math.Round(rgb.Cols * scale)),
            Math.Max(1, (int)Math.Round(rgb.Rows * scale)));

        using var resized = new Mat();

        // InterpolationFlags.Area adalah pilihan yang benar untuk MEMPERKECIL gambar;
        // interpolasi linear/bilinear menghasilkan aliasing yang bisa mengaburkan fitur wajah.
        Cv2.Resize(rgb, resized, target, 0, 0, InterpolationFlags.Area);

        return new RgbImage(CopyToRgbBytes(resized), resized.Cols, resized.Rows);
    }

    /// <summary>
    /// Deteksi wajah lalu ambil encoding-nya. Di sinilah aturan "tepat satu wajah" ditegakkan.
    /// </summary>
    private List<double> DetectAndEncode(FaceRecognition faceRecognition, FaceImage image, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Location adalah struct murni (Left/Top/Right/Bottom/Confidence) dan TIDAK
        // mengimplementasikan IDisposable - compiler menolaknya lewat CS0039 - jadi tidak ada
        // resource native yang perlu dilepas di sini. Yang wajib di-dispose hanya Image dan
        // FaceEncoding.
        var locations = faceRecognition.FaceLocations(image, _upSample, _detector).ToList();

        if (locations.Count == 0)
            throw new NoFaceDetectedExceptions(
                "Tidak ada wajah terdeteksi pada gambar. Pastikan wajah tampak jelas dan cukup terang.");

        // Sengaja TIDAK memilih wajah pertama secara diam-diam: encoding yang salah orang
        // jauh lebih berbahaya daripada permintaan ulang.
        if (locations.Count > 1)
            throw new MultipleFacesDetectedExceptions(
                $"Terdeteksi {locations.Count} wajah dalam satu gambar. Encoding wajah hanya valid untuk satu orang; " +
                "ulangi dengan satu wajah saja di dalam frame.")
            {
                FaceCount = locations.Count,
            };

        cancellationToken.ThrowIfCancellationRequested();

        // Bagian paling berat: ~200-500 ms dengan LandmarkModel.Small, bisa beberapa detik
        // dengan Large karena 68 landmark dikali numJitterings pengulangan.
        var encodings = faceRecognition
            .FaceEncodings(image, locations, _numJitterings, _landmarkModel, _detector)
            .ToList();

        try
        {
            if (encodings.Count == 0)
                throw new FaceEncodingExceptions("dlib tidak mengembalikan encoding walaupun satu wajah terdeteksi.");

            if (encodings.Count > 1)
                throw new FaceEncodingExceptions(
                    $"dlib mengembalikan {encodings.Count} encoding untuk satu wajah; diharapkan tepat satu.");

            return ValidateEncoding(encodings[0].GetRawEncoding());
        }
        finally
        {
            foreach (var encoding in encodings)
                encoding.Dispose();
        }
    }

    /// <summary>
    /// Penjaga terakhir kontrak publik: tepat 128 nilai dan semuanya finite. Tanpa ini, vektor
    /// rusak bisa lolos sampai ke API BPJS dan menghasilkan error yang mustahil dilacak.
    /// </summary>
    private static List<double> ValidateEncoding(double[]? raw)
    {
        if (raw is null)
            throw new FaceEncodingExceptions("Encoding null dikembalikan dari dlib.");

        if (raw.Length != EncodingLength)
            throw new FaceEncodingExceptions($"Panjang encoding {raw.Length}, diharapkan tepat {EncodingLength}.");

        for (var i = 0; i < raw.Length; i++)
        {
            if (double.IsNaN(raw[i]) || double.IsInfinity(raw[i]))
                throw new FaceEncodingExceptions(
                    $"Encoding tidak valid: indeks {i} berisi {raw[i]}. Semua nilai harus finite.");
        }

        // Disalin ke List baru supaya pemanggil bebas memodifikasi hasil tanpa menyentuh
        // memori native milik dlib.
        return new List<double>(raw);
    }

    // ------------------------------------------------------------------
    // Konversi pixel
    //
    // FaceRecognitionDotNet.LoadImage(byte[], ...) hanya menerima Mode.Rgb dan
    // Mode.Greyscale - TIDAK ada Bgr/BGRA - padahal kedua sumber gambar app ini
    // menghasilkan BGR: kamera memberi BGRA8888, Cv2.ImDecode memberi BGR.
    // Jadi konversi warna wajib dilakukan di sini.
    // ------------------------------------------------------------------

    /// <summary>
    /// Mengubah pixel BGRA8888 (format <c>CameraFrameEventArgs.Pixels</c>) menjadi
    /// <see cref="RgbImage"/>. Dipanggil SINKRON di thread pemanggil supaya buffer ArrayPool
    /// tidak perlu ditahan sampai deteksi selesai.
    /// </summary>
    private RgbImage BgraToRgb(byte[] bgraPixels, int width, int height, int stride)
    {
        var handle = GCHandle.Alloc(bgraPixels, GCHandleType.Pinned);
        try
        {
            // Mat ini membungkus memori pemanggil tanpa memilikinya, jadi Dispose()-nya
            // tidak akan menyentuh array aslinya. FromPixelData adalah pengganti ctor
            // IntPtr yang sudah obsolete sejak OpenCvSharp 4.13.
            using var source = Mat.FromPixelData(height, width, MatType.CV_8UC4, handle.AddrOfPinnedObject(), stride);
            using var rgb = new Mat();
            Cv2.CvtColor(source, rgb, ColorConversionCodes.BGRA2RGB);
            return Prepare(rgb);
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Menyalin data Mat 3-channel ke byte[] baru yang rapat (tanpa padding baris), format yang
    /// diharapkan <c>FaceRecognition.LoadImage</c>.
    /// </summary>
    private static byte[] CopyToRgbBytes(Mat mat)
    {
        if (mat.Channels() != 3)
            throw new FaceImageExceptions(
                $"Diharapkan gambar 3 channel (RGB), diterima {mat.Channels()} channel.");

        // Mat hasil CvtColor selalu rapat, tapi sub-matrix ROI tidak - Clone() menutup
        // keduanya sehingga Marshal.Copy tidak membaca padding baris.
        Mat source = mat;
        Mat? clone = null;
        if (!mat.IsContinuous())
        {
            clone = mat.Clone();
            source = clone;
        }

        try
        {
            var length = source.Rows * source.Cols * 3;
            var buffer = new byte[length];
            Marshal.Copy(source.Data, buffer, 0, length);
            return buffer;
        }
        finally
        {
            clone?.Dispose();
        }
    }

    /// <summary>
    /// Memvalidasi buffer BGRA sebelum di-pin. Pemeriksaan ini murah dan menangkap kesalahan
    /// paling umum dari pemanggil kamera: stride yang tidak cocok dengan lebar.
    /// </summary>
    private static void ValidateBgra(byte[] bgraPixels, int width, int height, int stride)
    {
        if (bgraPixels is null)
            throw new FaceImageExceptions(
                "Buffer pixel null. Frame kamera mungkin sudah dilepas sebelum encoding diminta.");

        if (width <= 0 || height <= 0)
            throw new FaceImageExceptions($"Dimensi gambar tidak valid: {width}x{height}.");

        if (stride < 0)
            throw new FaceImageExceptions($"Stride tidak boleh negatif: {stride}.");

        var effectiveStride = stride == 0 ? width * 4 : stride;

        if (effectiveStride < width * 4)
            throw new FaceImageExceptions(
                $"Stride {effectiveStride} terlalu kecil untuk lebar {width} pixel BGRA (butuh minimal {width * 4}).");

        var required = (long)effectiveStride * (height - 1) + (long)width * 4;
        if (bgraPixels.Length < required)
            throw new FaceImageExceptions(
                $"Buffer pixel terlalu pendek: {bgraPixels.Length:N0} byte, dibutuhkan {required:N0} " +
                $"untuk {width}x{height} dengan stride {effectiveStride}.");
    }

    /// <summary>
    /// Melepas handle native dlib. Aman dipanggil berkali-kali. Setelah ini semua method
    /// encoding melempar <see cref="ObjectDisposedException"/>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _faceRecognition?.Dispose();
        _faceRecognition = null;
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
