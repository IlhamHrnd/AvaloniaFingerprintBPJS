using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

#if HAS_DPURUNET
using System.Linq;
using DPUruNet;
#endif

namespace AvaloniaFingerprint.Services;

public class FingerprintException : Exception
{
    public FingerprintException(string message) : base(message) { }
}

/// <summary>One scan: the picture to show, the template to send, and the raw FMD for enrollment.</summary>
public sealed class FingerCapture
{
    /// <summary>Base64 JPEG of the scanned finger.</summary>
    public string ImageBase64 { get; init; } = string.Empty;

    /// <summary>Base64 DP_VERIFICATION template — what the Verify endpoint expects.</summary>
    public string VerificationTemplate { get; init; } = string.Empty;

    /// <summary>
    /// Opaque DP_PRE_REGISTRATION FMD. Collected across several scans and handed back to
    /// <see cref="FingerprintService.CreateEnrollmentTemplate"/>; the concrete DPUruNet type
    /// is kept out of the public surface so the project still compiles without the SDK.
    /// </summary>
    public object? PreRegistrationFmd { get; init; }

    /// <summary>Raw 8-bit grayscale image plus its dimensions, for on-screen preview.</summary>
    public byte[]? RawImage { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

/// <summary>
/// Wraps the DigitalPersona U.are.U SDK and the USB presence watcher.
/// </summary>
public sealed class FingerprintService : IDisposable
{
    private readonly FingerprintOptions _options;
    private readonly UsbDeviceWatcher _watcher;

    public FingerprintService(FingerprintOptions options, DeviceOptions deviceOptions)
    {
        _options = options;
        _watcher = new UsbDeviceWatcher(deviceOptions, ProbeSdkReader);
        _watcher.StatusChanged += (s, e) => DeviceStatusChanged?.Invoke(this, e);
    }

    public event EventHandler<DeviceStatusEventArgs>? DeviceStatusChanged;

    public bool IsDeviceConnected => _watcher.IsConnected;
    public string? DeviceName => _watcher.DeviceName;

    public bool IsSdkAvailable =>
#if HAS_DPURUNET
        true;
#else
        false;
#endif

    public const string SdkMissingMessage =
        "SDK DigitalPersona (DPUruNet.dll) tidak ditemukan saat build. " +
        "Install U.are.U RTE atau salin DPUruNet.dll ke folder lib\\, lalu build ulang.";

    public void Start() => _watcher.Start();

    public Task RefreshDeviceAsync() => _watcher.RefreshAsync();

    /// <summary>Scans one finger. Throws <see cref="FingerprintException"/> on any reader problem.</summary>
    public Task<FingerCapture> CaptureAsync(int? timeoutMs = null, CancellationToken ct = default)
    {
        var timeout = timeoutMs ?? _options.CaptureTimeoutMs;

#if HAS_DPURUNET
        return Task.Run(() => CaptureCore(timeout), ct);
#else
        _ = timeout;
        _ = ct;
        throw new FingerprintException(SdkMissingMessage);
#endif
    }

    /// <summary>Merges the pre-registration FMDs of one hand into a single registration template.</summary>
    public string CreateEnrollmentTemplate(IReadOnlyList<object> preRegistrationFmds)
    {
#if HAS_DPURUNET
        var fmds = preRegistrationFmds.OfType<Fmd>().ToList();
        if (fmds.Count == 0)
            throw new FingerprintException("Belum ada hasil pindai untuk didaftarkan.");

        var result = Enrollment.CreateEnrollmentFmd(Constants.Formats.Fmd.DP_REGISTRATION, fmds);
        if (result.ResultCode != Constants.ResultCode.DP_SUCCESS || result.Data is null)
            throw new FingerprintException($"Gagal membuat template registrasi ({result.ResultCode}).");

        return Convert.ToBase64String(result.Data.Bytes);
#else
        _ = preRegistrationFmds;
        throw new FingerprintException(SdkMissingMessage);
#endif
    }

    private (bool connected, string? name) ProbeSdkReader()
    {
#if HAS_DPURUNET
        var readers = ReaderCollection.GetReaders();
        if (readers is null || readers.Count == 0)
            return (false, null);

        var serial = readers[0].Description.SerialNumber;
        return (true, string.IsNullOrWhiteSpace(serial)
            ? "DigitalPersona Reader"
            : $"DigitalPersona Reader ({serial})");
#else
        return (false, null);
#endif
    }

#if HAS_DPURUNET
    private FingerCapture CaptureCore(int timeoutMs)
    {
        ReaderCollection? readers;
        try
        {
            readers = ReaderCollection.GetReaders();
        }
        catch (Exception ex)
        {
            throw new FingerprintException($"Gagal mengakses perangkat: {ex.Message}");
        }

        if (readers is null || readers.Count == 0)
            throw new FingerprintException("Perangkat Fingerprint Tidak Terdeteksi");

        var reader = readers[0];
        try
        {
            reader.Open(Constants.CapturePriority.DP_PRIORITY_COOPERATIVE);
            reader.Calibrate();

            var result = reader.Capture(
                Constants.Formats.Fid.ANSI,
                Constants.CaptureProcessing.DP_IMG_PROC_DEFAULT,
                timeoutMs,
                reader.Capabilities.Resolutions.First());

            if (result is null || result.ResultCode != Constants.ResultCode.DP_SUCCESS || result.Data is null)
                throw new FingerprintException(result is null
                    ? "Pemindaian gagal."
                    : $"Pemindaian gagal ({result.ResultCode}).");

            var verification = FeatureExtraction.CreateFmdFromFid(result.Data, Constants.Formats.Fmd.DP_VERIFICATION);
            if (verification.ResultCode != Constants.ResultCode.DP_SUCCESS || verification.Data is null)
                throw new FingerprintException($"Gagal mengekstrak sidik jari ({verification.ResultCode}).");

            var preRegistration = FeatureExtraction.CreateFmdFromFid(result.Data, Constants.Formats.Fmd.DP_PRE_REGISTRATION);
            if (preRegistration.ResultCode != Constants.ResultCode.DP_SUCCESS || preRegistration.Data is null)
                throw new FingerprintException($"Gagal mengekstrak sidik jari ({preRegistration.ResultCode}).");

            byte[]? raw = null;
            int width = 0, height = 0;
            using (var views = result.Data.Views.GetEnumerator())
            {
                if (views.MoveNext())
                {
                    var fiv = views.Current;
                    raw = fiv.RawImage;
                    width = fiv.Width;
                    height = fiv.Height;
                }
            }

            return new FingerCapture
            {
                ImageBase64 = raw is null ? string.Empty : BaseService.GrayscaleToJpegBase64(raw, width, height),
                VerificationTemplate = Convert.ToBase64String(verification.Data.Bytes),
                PreRegistrationFmd = preRegistration.Data,
                RawImage = raw,
                Width = width,
                Height = height
            };
        }
        finally
        {
            try { reader.Dispose(); } catch { /* reader already gone */ }
        }
    }
#endif

    public void Dispose() => _watcher.Dispose();
}
