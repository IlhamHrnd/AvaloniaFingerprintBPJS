using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaFingerprint.Exceptions;
using AvaloniaFingerprint.Services;
using Microsoft.Extensions.Configuration;
using OpenCvSharp;

namespace FaceEncodingProbe;

internal static class Program
{
    private const string ObamaFile = "obama.jpg";
    private const string TwoPeopleFile = "two_people.jpg";
    private const string BidenFile = "biden.jpg";

    private const double SamePersonThreshold = 0.6;

    private static readonly string[] RequiredModels =
    [
        "mmod_human_face_detector.dat",
        "shape_predictor_5_face_landmarks.dat",
        "shape_predictor_68_face_landmarks.dat",
        "dlib_face_recognition_resnet_model_v1.dat",
    ];

    private static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"FATAL (di luar kasus uji): {ex}");
            return 2;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var repoRoot = FindRepoRoot();
        var modelsDir = Path.Combine(repoRoot, "FaceModels");
        var testDir = Path.Combine(repoRoot, "tools", "FaceEncodingProbe", "testdata");
        var settingsPath = Path.Combine(repoRoot, "appsettings.json");

        Probe.Banner("FACE ENCODING PROBE - validasi IFaceRecognitionService");
        Probe.Info("proses", $"{RuntimeInformation.OSDescription} | arch={RuntimeInformation.ProcessArchitecture} | 64-bit={Environment.Is64BitProcess}");
        Probe.Info("repo", repoRoot);
        Probe.Info("model", modelsDir);
        Probe.Info("testdata", testDir);

        if (!CheckPrerequisites(modelsDir, testDir))
            return 2;

        if (args.Any(arg => string.Equals(arg.TrimStart('-'), "benchmark", StringComparison.OrdinalIgnoreCase)))
        {
            await RunBenchmarkAsync(settingsPath, modelsDir, testDir).ConfigureAwait(false);
            return Probe.Summarize();
        }

        var configuration = BuildConfiguration(settingsPath, modelsDir);

        using (var service = new FaceRecognitionService(configuration, new SessionService()))
        {
            Probe.Info("IsReady", service.IsReady.ToString(CultureInfo.InvariantCulture));
            Probe.Info("resolved", service.ModelDirectory);
            Probe.Check("prasyarat", service.IsReady, "IsReady == true sebelum pemanggilan pertama");

            await RunInitializationCasesAsync(service).ConfigureAwait(false);
            var inputs = await RunInputPathCasesAsync(service, testDir).ConfigureAwait(false);
            await RunRejectionCasesAsync(service, testDir).ConfigureAwait(false);
            await RunIdentityCasesAsync(service, testDir, inputs).ConfigureAwait(false);
            await RunLifecycleCasesAsync(service, Path.Combine(testDir, ObamaFile), configuration).ConfigureAwait(false);
            await RunMaxEdgeCaseAsync(settingsPath, modelsDir, testDir, inputs).ConfigureAwait(false);
        }

        return Probe.Summarize();
    }

    private static bool CheckPrerequisites(string modelsDir, string testDir)
    {
        var missingModels = RequiredModels
            .Where(model => !File.Exists(Path.Combine(modelsDir, model)))
            .ToArray();

        var missingImages = new[] { ObamaFile, TwoPeopleFile, BidenFile }
            .Where(image => !File.Exists(Path.Combine(testDir, image)))
            .ToArray();

        if (missingModels.Length == 0 && missingImages.Length == 0)
            return true;

        Console.WriteLine();
        if (missingModels.Length > 0)
            Console.WriteLine($"PRASYARAT: model dlib belum ada: {string.Join(", ", missingModels)}");
        if (missingImages.Length > 0)
            Console.WriteLine($"PRASYARAT: gambar uji belum ada: {string.Join(", ", missingImages)}");

        Console.WriteLine("Jalankan dulu: powershell -ExecutionPolicy Bypass -File tools\\download-face-assets.ps1");
        return false;
    }

    private static IConfiguration BuildConfiguration(string settingsPath, string modelsDir, Dictionary<string, string?>? overrides = null, bool logSource = true)
    {
        var builder = new ConfigurationBuilder();

        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Apps:Timeout"] = "30",
            ["Apps:CaptureTimeOut"] = "7000",
            ["Apps:VerifyTimeoutMs"] = "5000",
        });

        if (File.Exists(settingsPath))
        {
            builder.AddJsonFile(settingsPath, optional: false, reloadOnChange: false);
            if (logSource)
                Probe.Info("appsettings", settingsPath);
        }
        else if (logSource)
        {
            Probe.Warn("appsettings", $"'{settingsPath}' tidak ditemukan; memakai nilai fallback");
        }

        var last = new Dictionary<string, string?>
        {
            ["FaceRecognition:ModelDirectory"] = modelsDir,
        };

        if (overrides is not null)
        {
            foreach (var pair in overrides)
                last[pair.Key] = pair.Value;
        }

        builder.AddInMemoryCollection(last);

        return builder.Build();
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AvaloniaFingerprint.csproj")))
                return dir.FullName;
        }

        throw new DirectoryNotFoundException(
            $"AvaloniaFingerprint.csproj tidak ditemukan dari '{AppContext.BaseDirectory}'. " +
            "Jalankan harness ini dari dalam repo: dotnet run --project tools\\FaceEncodingProbe\\FaceEncodingProbe.csproj");
    }

    private sealed record BgraFrame(byte[] Pixels, int Width, int Height, int Stride);

    private sealed record InputResults(
        List<double>? FromFile,
        List<double>? FromBytes,
        List<double>? FromBgra,
        List<double>? FromPaddedBgra,
        BgraFrame Frame);

    private static BgraFrame ToBgraFrame(string imagePath, int paddingBytes)
    {
        using var bgr = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (bgr.Empty())
            throw new InvalidOperationException($"OpenCV gagal membaca '{imagePath}'.");

        using var bgra = new Mat();
        Cv2.CvtColor(bgr, bgra, ColorConversionCodes.BGR2BGRA);

        var width = bgra.Cols;
        var height = bgra.Rows;
        var stride = (width * 4) + paddingBytes;

        var pixels = new byte[stride * height];
        Array.Fill(pixels, (byte)0xFF);

        for (var y = 0; y < height; y++)
            Marshal.Copy(bgra.Ptr(y), pixels, y * stride, width * 4);

        return new BgraFrame(pixels, width, height, stride);
    }
    private static BgraFrame MatToBgraFrame(Mat bgra)
    {
        var width = bgra.Cols;
        var height = bgra.Rows;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        Marshal.Copy(bgra.Data, pixels, 0, pixels.Length);
        return new BgraFrame(pixels, width, height, stride);
    }

    private static BgraFrame ResizeToBgra(string imagePath, int width, int height)
    {
        using var bgr = Cv2.ImRead(imagePath, ImreadModes.Color);
        using var resized = new Mat();
        Cv2.Resize(bgr, resized, new Size(width, height), 0, 0, InterpolationFlags.Area);
        using var bgra = new Mat();
        Cv2.CvtColor(resized, bgra, ColorConversionCodes.BGR2BGRA);
        return MatToBgraFrame(bgra);
    }

    private static BgraFrame ResizeToMaxEdgeBgra(string imagePath, int maxEdge)
    {
        using var bgr = Cv2.ImRead(imagePath, ImreadModes.Color);

        var scale = Math.Min(1d, maxEdge / (double)Math.Max(bgr.Cols, bgr.Rows));
        var width = Math.Max(1, (int)Math.Round(bgr.Cols * scale));
        var height = Math.Max(1, (int)Math.Round(bgr.Rows * scale));

        using var resized = new Mat();
        Cv2.Resize(bgr, resized, new Size(width, height), 0, 0, InterpolationFlags.Area);
        using var bgra = new Mat();
        Cv2.CvtColor(resized, bgra, ColorConversionCodes.BGR2BGRA);
        return MatToBgraFrame(bgra);
    }

    private static BgraFrame CreateNoFaceFrame(int width = 480, int height = 360)
    {
        var random = new Random(20260410);
        var pixels = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                pixels[offset + 0] = (byte)((x * 255) / Math.Max(1, width - 1));
                pixels[offset + 1] = (byte)((y * 255) / Math.Max(1, height - 1));
                pixels[offset + 2] = (byte)random.Next(70, 90);
                pixels[offset + 3] = 255;
            }
        }

        return new BgraFrame(pixels, width, height, width * 4);
    }

    private static async Task RunInitializationCasesAsync(FaceRecognitionService service)
    {
        Probe.Banner("KASUS 0: BIAYA INISIALISASI MODEL (pemanggilan pertama)");

        Probe.Case("Kasus 0: muat model dlib lewat gambar 64x64 (diharapkan tanpa wajah)");
        var tiny = CreateNoFaceFrame(64, 64);

        var stopwatch = Stopwatch.StartNew();
        var thrown = await Probe.ExpectThrowsAsync<NoFaceDetectedExceptions>("kasus0",
            () => service.GetFaceEncodingAsync(tiny.Pixels, tiny.Width, tiny.Height, tiny.Stride)).ConfigureAwait(false);
        stopwatch.Stop();

        Probe.Info("total", $"{stopwatch.ElapsedMilliseconds} ms = muat 126 MB model + deteksi 64x64");
        if (thrown is null)
            Probe.Warn("kasus0", "gambar 64x64 ternyata terdeteksi sebagai wajah; biaya inisialisasi tidak terisolasi");
    }

    private static async Task<InputResults> RunInputPathCasesAsync(FaceRecognitionService service, string testDir)
    {
        var obama = Path.Combine(testDir, ObamaFile);
        Probe.Banner("KASUS 1-3: JALUR MASUKAN (satu wajah)");

        Probe.Case("Kasus 1: path file - dibaca lalu didekode OpenCV (jalur yang sama dengan byte)");
        Probe.Info("file", $"{obama} ({new FileInfo(obama).Length:N0} byte)");
        var fromFile = await Probe.ExpectEncodingAsync("kasus1",
            () => service.GetFaceEncodingFromFileAsync(obama)).ConfigureAwait(false);

        Probe.Case("Kasus 2: byte JPEG di memori - jalur unggah file (Cv2.ImDecode)");
        var obamaBytes = await File.ReadAllBytesAsync(obama).ConfigureAwait(false);
        Probe.Info("ukuran", $"{obamaBytes.Length:N0} byte");
        var fromBytes = await Probe.ExpectEncodingAsync("kasus2",
            () => service.GetFaceEncodingAsync(obamaBytes)).ConfigureAwait(false);

        Probe.Case("Kasus 3: pixel BGRA8888 - jalur tangkapan kamera, stride rapat");
        var frame = ToBgraFrame(obama, paddingBytes: 0);
        Probe.Info("frame", $"{frame.Width}x{frame.Height} stride={frame.Stride} buffer={frame.Pixels.Length:N0} byte");
        var fromBgra = await Probe.ExpectEncodingAsync("kasus3",
            () => service.GetFaceEncodingAsync(frame.Pixels, frame.Width, frame.Height, frame.Stride)).ConfigureAwait(false);

        Probe.Case("Kasus 3b: pixel BGRA dengan padding stride 64 byte/baris (diisi 0xFF)");
        var padded = ToBgraFrame(obama, paddingBytes: 64);
        Probe.Info("frame", $"{padded.Width}x{padded.Height} stride={padded.Stride} buffer={padded.Pixels.Length:N0} byte");
        var fromPadded = await Probe.ExpectEncodingAsync("kasus3b",
            () => service.GetFaceEncodingAsync(padded.Pixels, padded.Width, padded.Height, padded.Stride)).ConfigureAwait(false);

        if (fromBgra is not null && fromPadded is not null)
        {
            var distance = FaceRecognitionService.EuclideanDistance(fromBgra, fromPadded);
            Probe.Check("kasus3b-stride", distance < 1e-12,
                $"padding stride tidak mengubah hasil (jarak {distance.ToString("E3", CultureInfo.InvariantCulture)})");
        }

        if (fromFile is not null && fromBgra is not null)
        {
            var distance = FaceRecognitionService.EuclideanDistance(fromFile, fromBgra);
            Probe.Info("jarak", $"jalur file vs jalur pixel BGRA = {Probe.Inv(distance)}");
            Probe.Check("kasus3-silang", distance < 0.01,
                $"kedua jalur sepakat (jarak {Probe.Inv(distance, 6)} < 0.01)");
        }

        return new InputResults(fromFile, fromBytes, fromBgra, fromPadded, frame);
    }

    private static async Task RunRejectionCasesAsync(FaceRecognitionService service, string testDir)
    {
        Probe.Banner("KASUS 4-6: PENOLAKAN (0 wajah, banyak wajah, masukan rusak)");

        Probe.Case("Kasus 4: gambar tanpa wajah (gradien sintetis, jalur pixel BGRA)");
        var noFace = CreateNoFaceFrame();
        Probe.Info("frame", $"{noFace.Width}x{noFace.Height} gradien sintetis, buffer={noFace.Pixels.Length:N0} byte");
        await Probe.ExpectThrowsAsync<NoFaceDetectedExceptions>("kasus4",
            () => service.GetFaceEncodingAsync(noFace.Pixels, noFace.Width, noFace.Height, noFace.Stride)).ConfigureAwait(false);

        Probe.Case("Kasus 5: lebih dari satu wajah (two_people.jpg)");
        var twoPeople = Path.Combine(testDir, TwoPeopleFile);
        Probe.Info("file", $"{twoPeople} ({new FileInfo(twoPeople).Length:N0} byte)");
        var multiple = await Probe.ExpectThrowsAsync<MultipleFacesDetectedExceptions>("kasus5",
            () => service.GetFaceEncodingFromFileAsync(twoPeople)).ConfigureAwait(false);

        if (multiple is not null)
            Probe.Check("kasus5-count", multiple.FaceCount >= 2, $"FaceCount == {multiple.FaceCount}");

        Probe.Case("Kasus 6: masukan tidak valid");
        await Probe.ExpectThrowsAsync<FaceImageExceptions>("6a byte null",
            () => service.GetFaceEncodingAsync((byte[])null!)).ConfigureAwait(false);

        await Probe.ExpectThrowsAsync<FaceImageExceptions>("6b byte kosong",
            () => service.GetFaceEncodingAsync(Array.Empty<byte>())).ConfigureAwait(false);

        await Probe.ExpectThrowsAsync<FaceImageExceptions>("6c byte bukan gambar",
            () => service.GetFaceEncodingAsync([0x50, 0x4B, 0x03, 0x04, 0xDE, 0xAD, 0xBE, 0xEF])).ConfigureAwait(false);

        await Probe.ExpectThrowsAsync<FaceImageExceptions>("6d path tidak ada",
            () => service.GetFaceEncodingFromFileAsync(Path.Combine(testDir, "tidak-ada.jpg"))).ConfigureAwait(false);

        await Probe.ExpectThrowsAsync<FaceImageExceptions>("6e path kosong",
            () => service.GetFaceEncodingFromFileAsync("   ")).ConfigureAwait(false);

        await Probe.ExpectThrowsAsync<FaceImageExceptions>("6f dimensi nol",
            () => service.GetFaceEncodingAsync(new byte[16], 0, 0, 0)).ConfigureAwait(false);

        await Probe.ExpectThrowsAsync<FaceImageExceptions>("6g buffer terlalu pendek",
            () => service.GetFaceEncodingAsync(new byte[100], 64, 64, 0)).ConfigureAwait(false);

        await Probe.ExpectThrowsAsync<FaceImageExceptions>("6h buffer pixel null",
            () => service.GetFaceEncodingAsync(null!, 8, 8, 0)).ConfigureAwait(false);

        await Probe.ExpectThrowsAsync<FaceImageExceptions>("6i stride terlalu kecil",
            () => service.GetFaceEncodingAsync(new byte[8 * 8 * 4], 8, 8, 16)).ConfigureAwait(false);
    }

    private static async Task RunIdentityCasesAsync(FaceRecognitionService service, string testDir, InputResults inputs)
    {
        Probe.Banner("KASUS 7: DETERMINISME + SANITY CHECK IDENTITAS");

        var obama = Path.Combine(testDir, ObamaFile);
        var biden = Path.Combine(testDir, BidenFile);

        Probe.Case("Kasus 7a: gambar yang sama dienkode dua kali harus identik bit-per-bit");
        var repeat = await Probe.ExpectEncodingAsync("kasus7a",
            () => service.GetFaceEncodingFromFileAsync(obama)).ConfigureAwait(false);

        if (inputs.FromFile is not null && repeat is not null)
        {
            var distance = FaceRecognitionService.EuclideanDistance(inputs.FromFile, repeat);
            Probe.Check("kasus7a-deterministik", distance == 0d,
                $"jarak == 0 (aktual {distance.ToString("E3", CultureInfo.InvariantCulture)}) - pemanggilan ulang stabil");
        }

        Probe.Case("Kasus 7b: orang berbeda (obama.jpg vs biden.jpg) harus berjauhan");
        var other = await Probe.ExpectEncodingAsync("kasus7b",
            () => service.GetFaceEncodingFromFileAsync(biden)).ConfigureAwait(false);

        if (inputs.FromFile is not null && other is not null)
        {
            var distance = FaceRecognitionService.EuclideanDistance(inputs.FromFile, other);
            Probe.Info("jarak", $"{Probe.Inv(distance)} (ambang dlib untuk orang sama: {Probe.Inv(SamePersonThreshold, 1)})");
            Probe.Check("kasus7b-berbeda", distance > SamePersonThreshold,
                $"jarak {Probe.Inv(distance, 4)} > {SamePersonThreshold} - vektor benar-benar membedakan orang");
        }

        Probe.Case("Kasus 7c: orang sama lewat jalur berbeda (path file vs byte JPEG) harus identik");
        if (inputs.FromFile is not null && inputs.FromBytes is not null)
        {
            var distance = FaceRecognitionService.EuclideanDistance(inputs.FromFile, inputs.FromBytes);
            Probe.Info("jarak", Probe.Inv(distance));
            Probe.Check("kasus7c-sama", distance == 0d,
                "jarak == 0 - kedua jalur memakai satu decoder sehingga vektornya identik bit-per-bit");
        }
    }

    private static async Task RunLifecycleCasesAsync(FaceRecognitionService service, string obamaPath, IConfiguration configuration)
    {
        Probe.Banner("KASUS 8-12: SIKLUS HIDUP, KONKURENSI, BUFFER POOLED");

        Probe.Case("Kasus 8: model hilang memberi pesan yang bisa ditindaklanjuti");
        var emptyDir = Path.Combine(Path.GetTempPath(), $"faceprobe-kosong-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);
        try
        {
            var brokenConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Apps:Timeout"] = "30",
                ["FaceRecognition:ModelDirectory"] = emptyDir,
            }).Build();

            using var broken = new FaceRecognitionService(brokenConfig, new SessionService());
            Probe.Info("dir", emptyDir);
            Probe.Check("kasus8-IsReady", !broken.IsReady, "IsReady == false saat model belum diunduh");

            var missing = await Probe.ExpectThrowsAsync<FaceModelMissingExceptions>("kasus8",
                () => broken.GetFaceEncodingFromFileAsync(obamaPath)).ConfigureAwait(false);

            if (missing is not null)
                Probe.Check("kasus8-pesan", missing.Message.Contains("download-face-assets.ps1", StringComparison.Ordinal),
                    "pesan menyebut skrip unduh, jadi operator tahu cara memperbaikinya");
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }

        Probe.Case("Kasus 9: CancellationToken yang sudah dibatalkan dihormati");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await Probe.ExpectThrowsAsync<OperationCanceledException>("kasus9",
                () => service.GetFaceEncodingFromFileAsync(obamaPath, cancelled.Token)).ConfigureAwait(false);
        }

        Probe.Case("Kasus 10: empat pemanggilan paralel tidak merusak handle native");
        var parallel = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => service.GetFaceEncodingFromFileAsync(obamaPath))).ConfigureAwait(false);

        Probe.Check("kasus10-panjang", parallel.All(vector => vector.Count == 128 && vector.All(double.IsFinite)),
            $"{parallel.Length} hasil paralel semuanya 128 nilai finite");
        Probe.Check("kasus10-identik",
            parallel.All(vector => FaceRecognitionService.EuclideanDistance(parallel[0], vector) == 0d),
            "semua hasil paralel identik - gate serialisasi bekerja");

        Probe.Case("Kasus 11: buffer ArrayPool boleh dilepas pemanggil segera setelah Task dikembalikan");
        await RunPooledBufferCaseAsync(service, obamaPath).ConfigureAwait(false);

        Probe.Case("Kasus 12: setelah Dispose service menolak dipakai, dan Dispose idempotent");
        var disposable = new FaceRecognitionService(configuration, new SessionService());
        disposable.Dispose();
        disposable.Dispose();
        Probe.Check("kasus12-idempotent", true, "Dispose() dua kali berturut-turut tidak melempar");

        await Probe.ExpectThrowsAsync<ObjectDisposedException>("kasus12",
            () => disposable.GetFaceEncodingFromFileAsync(obamaPath)).ConfigureAwait(false);
    }

    private static async Task RunPooledBufferCaseAsync(FaceRecognitionService service, string obamaPath)
    {
        var frame = ToBgraFrame(obamaPath, paddingBytes: 0);
        var pooled = ArrayPool<byte>.Shared.Rent(frame.Pixels.Length);

        try
        {
            Array.Copy(frame.Pixels, pooled, frame.Pixels.Length);

            var pending = service.GetFaceEncodingAsync(pooled, frame.Width, frame.Height, frame.Stride);

            Array.Fill(pooled, (byte)0);

            var encoding = await pending.ConfigureAwait(false);
            Probe.Info("buffer", $"{frame.Pixels.Length:N0} byte disewa dari ArrayPool, di-nol-kan sebelum await");

            Probe.Check("kasus11-panjang", encoding.Count == 128 && encoding.All(double.IsFinite),
                $"encoding tetap valid ({encoding.Count} nilai finite) walau buffer sudah di-nol-kan");

            var intact = await service
                .GetFaceEncodingAsync(frame.Pixels, frame.Width, frame.Height, frame.Stride)
                .ConfigureAwait(false);

            var distance = FaceRecognitionService.EuclideanDistance(intact, encoding);
            Probe.Check("kasus11-stabil", distance < 1e-12,
                $"identik dengan hasil dari buffer utuh (jarak {distance.ToString("E3", CultureInfo.InvariantCulture)})");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pooled);
        }
    }

    private static async Task RunMaxEdgeCaseAsync(string settingsPath, string modelsDir, string testDir, InputResults inputs)
    {
        Probe.Banner("KASUS 13: MaxEdge (pembatasan ukuran) tidak mengubah identitas");

        var obama = Path.Combine(testDir, ObamaFile);

        Probe.Case("Kasus 13: foto asli dienkode dengan MaxEdge=0 (tanpa pembatasan)");
        Probe.Info("file", $"{obama} - resolusi asli 910x1137");

        var uncappedConfig = BuildConfiguration(settingsPath, modelsDir, new Dictionary<string, string?>
        {
            ["FaceRecognition:MaxEdge"] = "0",
        }, logSource: false);

        List<double>? uncapped;
        using (var uncappedService = new FaceRecognitionService(uncappedConfig, new SessionService()))
        {
            uncapped = await Probe.ExpectEncodingAsync("kasus13",
                () => uncappedService.GetFaceEncodingFromFileAsync(obama)).ConfigureAwait(false);
        }

        if (inputs.FromFile is not null && uncapped is not null)
        {
            var distance = FaceRecognitionService.EuclideanDistance(inputs.FromFile, uncapped);
            Probe.Info("jarak", $"MaxEdge=800 vs MaxEdge=0 = {Probe.Inv(distance)}");
            Probe.Check("kasus13-identitas", distance < SamePersonThreshold,
                $"jarak {Probe.Inv(distance, 4)} < {SamePersonThreshold} - pembatasan ukuran tidak mengubah identitas");
        }
    }

    private static async Task RunBenchmarkAsync(string settingsPath, string modelsDir, string testDir)
    {
        Probe.Banner("BENCHMARK DETEKTOR (informatif, tanpa assertion)");

        var obama = Path.Combine(testDir, ObamaFile);
        var variants = new (string Label, BgraFrame Frame)[]
        {
            ("kamera 640x480", ResizeToBgra(obama, 640, 480)),
            ("cap 800 px", ResizeToMaxEdgeBgra(obama, 800)),
            ("asli", ToBgraFrame(obama, paddingBytes: 0)),
        };

        foreach (var variant in variants)
            Probe.Info("varian", $"{variant.Label} = {variant.Frame.Width}x{variant.Frame.Height}");

        var configurations = new (string Detector, int Upsample)[]
        {
            ("Hog", 1),
            ("Hog", 0),
            ("Cnn", 0),
            ("Cnn", 1),
        };

        const string detectorHeader = "Detector";
        const string upsampleHeader = "Up";
        const string cameraHeader = "kamera 640x480";
        const string capHeader = "cap 800 px";
        const string nativeHeader = "asli";

        Console.WriteLine();
        Console.WriteLine($"    {detectorHeader,-8} {upsampleHeader,-3} {cameraHeader,17} {capHeader,17} {nativeHeader,17}");
        Console.WriteLine("    " + new string('-', 68));

        foreach (var (detector, upsample) in configurations)
        {
            var configuration = BuildConfiguration(settingsPath, modelsDir, new Dictionary<string, string?>
            {
                ["FaceRecognition:Detector"] = detector,
                ["FaceRecognition:Upsample"] = upsample.ToString(CultureInfo.InvariantCulture),
            }, logSource: false);

            using var service = new FaceRecognitionService(configuration, new SessionService());

            var cells = new List<string>();
            foreach (var variant in variants)
            {
                var frame = variant.Frame;
                var stopwatch = Stopwatch.StartNew();
                string cell;

                try
                {
                    var encoding = await service
                        .GetFaceEncodingAsync(frame.Pixels, frame.Width, frame.Height, frame.Stride)
                        .ConfigureAwait(false);
                    stopwatch.Stop();
                    cell = encoding.Count == 128 ? $"{stopwatch.ElapsedMilliseconds,10} ms OK" : $"{stopwatch.ElapsedMilliseconds,10} ms ??";
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    var name = ex.GetType().Name.Replace("Exceptions", string.Empty, StringComparison.Ordinal);
                    cell = $"{name,17}";
                }

                cells.Add(cell);
            }

            Console.WriteLine($"    {detector,-8} {upsample,-3} {cells[0],17} {cells[1],17} {cells[2],17}");
        }

        Console.WriteLine();
        Console.WriteLine("    'OK' = tepat satu wajah terdeteksi dan 128 nilai dihasilkan.");
        Console.WriteLine("    Nama exception = konfigurasi itu gagal pada ukuran gambar tersebut.");
        Console.WriteLine("    Baris pertama tiap konfigurasi mencakup biaya muat model (~1.4 s).");
    }
}
