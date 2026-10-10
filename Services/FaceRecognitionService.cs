using AvaloniaFingerprint.Exceptions;
using FaceRecognitionDotNet;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DetectorModel = FaceRecognitionDotNet.Model;
using FaceEncoding = FaceRecognitionDotNet.FaceEncoding;
using FaceImage = FaceRecognitionDotNet.Image;
using ImageMode = FaceRecognitionDotNet.Mode;

namespace AvaloniaFingerprint.Services;

public sealed class FaceRecognitionService : BaseService, IDisposable
{
    public const int EncodingLength = 128;

    private static readonly string[] RequiredModelFiles =
    [
        "mmod_human_face_detector.dat",
        "shape_predictor_5_face_landmarks.dat",
        "shape_predictor_68_face_landmarks.dat",
        "dlib_face_recognition_resnet_model_v1.dat",
    ];

    private const string DownloadHint =
        "Jalankan: powershell -ExecutionPolicy Bypass -File tools\\download-face-assets.ps1";

    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly string _modelDirectory;
    private readonly DetectorModel _detector;
    private readonly int _upSample;
    private readonly PredictorModel _landmarkModel;
    private readonly int _numJitterings;
    private readonly int _maxEdge;

    private FaceRecognition? _faceRecognition;
    private bool _disposed;

    public FaceRecognitionService(IConfiguration configuration, SessionService session)
        : base(configuration, session)
    {
        _modelDirectory = ResolveModelDirectory(configuration["FaceRecognition:ModelDirectory"]);
        _detector = ParseDetector(configuration["FaceRecognition:Detector"]);
        _upSample = ParsePositiveInt(configuration["FaceRecognition:Upsample"], 0, 0, 4);
        _landmarkModel = ParseLandmarkModel(configuration["FaceRecognition:LandmarkModel"]);
        _numJitterings = ParsePositiveInt(configuration["FaceRecognition:NumJitterings"], 1, 0, 8);
        _maxEdge = ParsePositiveInt(configuration["FaceRecognition:MaxEdge"], 800, 0, 8192);
    }

    public string ModelDirectory => _modelDirectory;

    public bool IsReady =>
        _faceRecognition is not null
        || (Directory.Exists(_modelDirectory)
            && RequiredModelFiles.All(f => File.Exists(Path.Combine(_modelDirectory, f))));

    private static string ResolveModelDirectory(string? configured)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? "FaceModels" : configured.Trim();
        return Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, value));
    }

    private static DetectorModel ParseDetector(string? configured) =>
        string.Equals(configured?.Trim(), "Hog", StringComparison.OrdinalIgnoreCase)
            ? DetectorModel.Hog
            : DetectorModel.Cnn;

    private static PredictorModel ParseLandmarkModel(string? configured) =>
        string.Equals(configured?.Trim(), "Large", StringComparison.OrdinalIgnoreCase)
            ? PredictorModel.Large
            : PredictorModel.Small;

    private static int ParsePositiveInt(string? configured, int fallback, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return fallback;

        if (!int.TryParse(configured.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return fallback;

        return Math.Clamp(value, min, max);
    }

    public Task<List<double>> GetFaceEncodingAsync(byte[] encodedImage, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (encodedImage is null)
            throw new FaceImageExceptions("Byte gambar null. Pastikan file berhasil dibaca sebelum meminta encoding wajah.");
        if (encodedImage.Length == 0)
            throw new FaceImageExceptions("Byte gambar kosong (0 byte).");

        return EncodeEncodedAsync(encodedImage, $"gambar {encodedImage.Length:N0} byte", cancellationToken);
    }

    public Task<List<double>> GetFaceEncodingAsync(byte[] bgraPixels, int width, int height, int stride, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateBgra(bgraPixels!, width, height, stride);

        var effectiveStride = stride == 0 ? width * 4 : stride;

        var rgb = BgraToRgb(bgraPixels!, width, height, effectiveStride);

        return WithGateAsync(faceRecognition => Task.Run(
            () => Encode(faceRecognition, rgb, cancellationToken),
            cancellationToken), cancellationToken);
    }

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

    private Task<List<double>> EncodeEncodedAsync(byte[] encodedImage, string source, CancellationToken cancellationToken)
    {
        return WithGateAsync(faceRecognition => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using Mat decoded = Cv2.ImDecode(encodedImage, ImreadModes.Color);
            if (decoded is null || decoded.Empty())
                throw new FaceImageExceptions(
                    $"'{source}' tidak bisa didekode sebagai gambar. Format yang didukung: JPEG, PNG, BMP, TIFF, WebP.");

            using Mat rgb = new();
            Cv2.CvtColor(decoded, rgb, ColorConversionCodes.BGR2RGB);

            return Encode(faceRecognition, Prepare(rgb), cancellationToken);
        }, cancellationToken), cancellationToken);
    }

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
            throw new FaceModelMissingExceptions(
                $"Gagal menginisialisasi FaceRecognitionDotNet dari '{_modelDirectory}': {ex.GetType().Name}: {ex.Message}. " +
                $"Pastikan proses berjalan sebagai x64 karena native package hanya menyediakan win-x64. {DownloadHint}",
                ex);
        }

        return _faceRecognition;
    }

    private sealed record RgbImage(byte[] Pixels, int Width, int Height);

    private List<double> Encode(FaceRecognition faceRecognition, RgbImage rgb, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using FaceImage image = FaceRecognition.LoadImage(rgb.Pixels, rgb.Height, rgb.Width, rgb.Width * 3, ImageMode.Rgb);
        return DetectAndEncode(faceRecognition, image, cancellationToken);
    }

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

        Cv2.Resize(rgb, resized, target, 0, 0, InterpolationFlags.Area);

        return new RgbImage(CopyToRgbBytes(resized), resized.Cols, resized.Rows);
    }

    private List<double> DetectAndEncode(FaceRecognition faceRecognition, FaceImage image, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var locations = faceRecognition.FaceLocations(image, _upSample, _detector).ToList();

        if (locations.Count == 0)
            throw new NoFaceDetectedExceptions(
                "Tidak ada wajah terdeteksi pada gambar. Pastikan wajah tampak jelas dan cukup terang.");

        if (locations.Count > 1)
            throw new MultipleFacesDetectedExceptions(
                $"Terdeteksi {locations.Count} wajah dalam satu gambar. Encoding wajah hanya valid untuk satu orang; " +
                "ulangi dengan satu wajah saja di dalam frame.")
            {
                FaceCount = locations.Count,
            };

        cancellationToken.ThrowIfCancellationRequested();

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

        return new List<double>(raw);
    }

    private RgbImage BgraToRgb(byte[] bgraPixels, int width, int height, int stride)
    {
        var handle = GCHandle.Alloc(bgraPixels, GCHandleType.Pinned);
        try
        {
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

    private static byte[] CopyToRgbBytes(Mat mat)
    {
        if (mat.Channels() != 3)
            throw new FaceImageExceptions(
                $"Diharapkan gambar 3 channel (RGB), diterima {mat.Channels()} channel.");

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

    public Root<Models.FaceRecognition.Verify.Root> Verify(Filters.FaceRecognition.Verify verify)
    {
        var result = new Root<Models.FaceRecognition.Verify.Root>();
        var header = headers(Session.UserId);

        var response = RestResponse(url, "Frista/Verify", timeout, header, 1, 2, verify);
        try
        {
            if (response.IsSuccessStatusCode) result = JsonConvert.DeserializeObject<Root<Models.FaceRecognition.Verify.Root>>(response.Content ?? string.Empty) ?? new Root<Models.FaceRecognition.Verify.Root>();
            else result = JsonConvert.DeserializeObject<Root<Models.FaceRecognition.Verify.Root>>(response.Content ?? string.Empty) ?? new Root<Models.FaceRecognition.Verify.Root>();
        }
        catch (Exception e)
        {
            return result = new Root<Models.FaceRecognition.Verify.Root>
            {
                errors = [e.Message, e.InnerException?.Message ?? string.Empty]
            };
        }
        return result;
    }

    public Root<Models.FaceRecognition.NIK.Root> Nik(Filters.FaceRecognition.NIK nik)
    {
        var result = new Root<Models.FaceRecognition.NIK.Root>();
        var header = headers(Session.UserId);

        var response = RestResponse(url, "Frista/Nik", timeout, header, 1, 2, nik);
        try
        {
            if (response.IsSuccessStatusCode) result = JsonConvert.DeserializeObject<Root<Models.FaceRecognition.NIK.Root>>(response.Content ?? string.Empty) ?? new Root<Models.FaceRecognition.NIK.Root>();
            else result = JsonConvert.DeserializeObject<Root<Models.FaceRecognition.NIK.Root>>(response.Content ?? string.Empty) ?? new Root<Models.FaceRecognition.NIK.Root>();
        }
        catch (Exception e)
        {
            return result = new Root<Models.FaceRecognition.NIK.Root>
            {
                errors = [e.Message, e.InnerException?.Message ?? string.Empty]
            };
        }
        return result;
    }
}
