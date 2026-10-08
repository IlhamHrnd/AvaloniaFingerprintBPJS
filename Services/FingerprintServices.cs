using AvaloniaFingerprint.Exceptions;
using DPUruNet;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.Services
{
    public class FingerprintServices : BaseService, IDisposable
    {
        private readonly UsbDeviceWatcherServices watcher;

        public FingerprintServices(IConfiguration config, SessionService session) : base(config, session)
        {
            watcher = new UsbDeviceWatcherServices(config, session, ProbeSdkReader);
            watcher.StatusChanged += (_, state) => DeviceStateChanged?.Invoke(this, state);
        }

        public Root<Models.Fingerprint.Verify.VerifyRoot> Verify(Filters.Fingerprint.Verify verify)
        {
            var result = new Root<Models.Fingerprint.Verify.VerifyRoot>();
            var header = headers(Session.UserId);

            var response = RestResponse(url, "Finger/Verify", timeout, header, 1, 2, verify);
            try
            {
                if (response.IsSuccessStatusCode) result = JsonConvert.DeserializeObject<Root<Models.Fingerprint.Verify.VerifyRoot>>(response.Content ?? string.Empty) ?? new Root<Models.Fingerprint.Verify.VerifyRoot>();
                else result = JsonConvert.DeserializeObject<Root<Models.Fingerprint.Verify.VerifyRoot>>(response.Content ?? string.Empty) ?? new Root<Models.Fingerprint.Verify.VerifyRoot>();
            }
            catch (Exception e)
            {
                return result = new Root<Models.Fingerprint.Verify.VerifyRoot>
                {
                    errors = [e.Message, e.InnerException?.Message ?? string.Empty]
                };
            }
            return result;
        }

        public const string SearchingStatus = "Mencari perangkat...";
        public const string ConnectedStatus = "Perangkat terhubung";
        public const string DetectedStatus = "Perangkat terdeteksi";
        public const string NotReadyStatus = "Perangkat tidak siap";
        public const string DisconnectedStatus = "Perangkat terputus";
        public const string UnavailableStatus = "Perangkat tidak tersedia";

        public const string DetailDisconnected = "Hubungkan USB fingerprint reader DigitalPersona";

        public event EventHandler<FingerprintDeviceState>? DeviceStateChanged;

        public FingerprintDeviceState DeviceState => watcher.Current;

        public bool IsDeviceConnected => watcher.IsConnected;

        public string? DeviceName => watcher.DeviceName;

        public string? DeviceId => watcher.DeviceId;

        public string ResolveStatus() => DeviceState.State switch
        {
            FingerprintReaderState.Ready => ConnectedStatus,
            FingerprintReaderState.Initializing => SearchingStatus,
            FingerprintReaderState.Detected => DetectedStatus,
            FingerprintReaderState.NotReady => NotReadyStatus,
            _ => IsSdkAvailable ? DisconnectedStatus : UnavailableStatus,
        };

        public string ResolveDetail()
        {
            var state = DeviceState;

            switch (state.State)
            {
                case FingerprintReaderState.Ready:
                    return string.IsNullOrWhiteSpace(state.DeviceName) ? ConnectedStatus : state.DeviceName!;

                case FingerprintReaderState.Initializing:
                    return string.IsNullOrWhiteSpace(state.Detail) ? SearchingStatus : state.Detail!;

                default:
                    if (!string.IsNullOrWhiteSpace(state.Detail))
                        return state.Detail!;

                    return IsSdkAvailable ? DetailDisconnected : SdkMissing;
            }
        }
        public bool IsSdkAvailable =>
        #if HAS_DPURUNET
            true;
        #else
            false;
        #endif

        public void Start() => watcher.Start();

        public Task RefreshDeviceAsync() => watcher.RefreshAsync();

        public int CaptureTimeoutMs => captureTimeoutMs;

        public int VerifyTimeoutMs => verifyTimeoutMs;

        public Task<Models.Fingerprint.Capture> CaptureAsync(int? timeoutMs = null, CancellationToken ct = default)
        {
            var timeout = timeoutMs ?? captureTimeoutMs;

        #if HAS_DPURUNET
            return CaptureGuardedAsync(timeout, ct);
        #else
            _ = timeout;
            _ = ct;
            throw new FingerprintExceptions(SdkMissingMessage);
        #endif
        }

        private int _capturing;

        public bool IsCaptureInFlight => Volatile.Read(ref _capturing) != 0;

        #if HAS_DPURUNET
        private async Task<Models.Fingerprint.Capture> CaptureGuardedAsync(int timeoutMs, CancellationToken ct)
        {
            Interlocked.Increment(ref _capturing);
            try
            {
                return await Task.Run(() => CaptureCore(timeoutMs), ct).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _capturing);
            }
        }
        #endif

        public string SdkMissing => SdkMissingMessage;

        private FingerprintDeviceState? ProbeSdkReader(Action<FingerprintDeviceState> publish)
        {
        #if HAS_DPURUNET
            if (IsCaptureInFlight)
                return null;

            ReaderCollection? readers;
            try
            {
                readers = ReaderCollection.GetReaders();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[FingerprintServices] GetReaders gagal: {ex.GetType().Name}: {ex.Message}");
                return FingerprintDeviceState.AsNotReady(null, null, $"Gagal membaca daftar reader dari SDK: {ex.Message}");
            }

            if (readers is null || readers.Count == 0)
            {
                Trace.WriteLine("[FingerprintServices] SDK tidak melaporkan reader.");
                return FingerprintDeviceState.AsNoDevice();
            }

            Trace.WriteLine($"[FingerprintServices] SDK melaporkan {readers.Count} kandidat reader; memvalidasi...");

            FingerprintDeviceState? lastFailure = null;

            for (var i = 0; i < readers.Count; i++)
            {
                var reader = readers[i];
                var deviceId = reader.Description?.Name;
                var deviceName = DescribeReader(reader);

                PublishInitializing(publish, deviceName, deviceId);

                var validation = ValidateReader(reader, deviceName, deviceId);

                if (validation is null)
                    return null;

                if (validation.State == FingerprintReaderState.Ready)
                    return validation;

                lastFailure = validation;
            }

            Trace.WriteLine("[FingerprintServices] Tidak ada kandidat reader yang lolos validasi.");
            return lastFailure ?? FingerprintDeviceState.AsNoDevice();
        #else
            _ = publish;
            Trace.WriteLine("[FingerprintServices] DPUruNet tidak tersedia saat build; tidak ada reader yang bisa divalidasi.");
            return FingerprintDeviceState.AsNoDevice(SdkMissingMessage);
        #endif
        }

        #if HAS_DPURUNET
        private FingerprintDeviceState? ValidateReader(Reader reader, string? deviceName, string? deviceId)
        {
            try
            {
                Constants.ResultCode open;
                try
                {
                    open = reader.Open(Constants.CapturePriority.DP_PRIORITY_COOPERATIVE);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[FingerprintServices] Open('{deviceId}') melempar {ex.GetType().Name}: {ex.Message}");
                    return FingerprintDeviceState.AsNotReady(deviceName, deviceId, $"Reader gagal dibuka: {ex.Message}");
                }

                if (open == Constants.ResultCode.DP_DEVICE_BUSY)
                {
                    Trace.WriteLine($"[FingerprintServices] Reader '{deviceId}' sedang dipakai (DP_DEVICE_BUSY); state dipertahankan.");
                    return null;
                }

                if (open != Constants.ResultCode.DP_SUCCESS)
                {
                    Trace.WriteLine($"[FingerprintServices] Open('{deviceId}') = {open}; reader tidak dapat dipakai.");
                    return FingerprintDeviceState.AsNotReady(deviceName, deviceId, $"Reader gagal dibuka ({open}).");
                }

                var statusResult = reader.GetStatus();
                if (statusResult != Constants.ResultCode.DP_SUCCESS)
                {
                    Trace.WriteLine($"[FingerprintServices] GetStatus('{deviceId}') = {statusResult}.");
                    return FingerprintDeviceState.AsNotReady(deviceName, deviceId, $"Status reader gagal dibaca ({statusResult}).");
                }

                var status = reader.Status.Status;
                if (status != Constants.ReaderStatuses.DP_STATUS_READY)
                {
                    Trace.WriteLine($"[FingerprintServices] Reader '{deviceId}' berstatus {status} (bukan DP_STATUS_READY).");
                    return FingerprintDeviceState.AsNotReady(deviceName, deviceId, DescribeReaderStatus(status));
                }

                if (!reader.Capabilities.CanCapture)
                {
                    Trace.WriteLine($"[FingerprintServices] Reader '{deviceId}' CanCapture=false; tidak bisa memindai.");
                    return FingerprintDeviceState.AsNotReady(deviceName, deviceId, "Reader tidak mendukung pemindaian sidik jari (CanCapture=false).");
                }

                Trace.WriteLine($"[FingerprintServices] Reader '{deviceId}' lolos validasi: Open=DP_SUCCESS, GetStatus=DP_SUCCESS, Status=DP_STATUS_READY, CanCapture=True -> READY.");
                return FingerprintDeviceState.AsReady(deviceName, deviceId, "Reader siap digunakan");
            }
            finally
            {
                try { reader.Dispose(); } catch { }
            }
        }

        private void PublishInitializing(Action<FingerprintDeviceState> publish, string? deviceName, string? deviceId)
        {
            var current = watcher.Current;
            if (current.State is FingerprintReaderState.Ready or FingerprintReaderState.Initializing
                && string.Equals(current.DeviceId, deviceId, StringComparison.Ordinal))
                return;

            Trace.WriteLine($"[FingerprintServices] Kandidat reader '{deviceId}' ditemukan; memvalidasi...");
            publish(FingerprintDeviceState.AsInitializing(deviceName, deviceId, "Membuka dan memvalidasi reader..."));
        }

        private static string DescribeReader(Reader reader)
        {
            var serial = reader.Description?.SerialNumber;
            if (!string.IsNullOrWhiteSpace(serial))
                return $"DigitalPersona Reader ({serial})";

            var name = reader.Description?.Name;
            return string.IsNullOrWhiteSpace(name) ? "DigitalPersona Reader" : name!;
        }

        private static string DescribeReaderStatus(Constants.ReaderStatuses status) => status switch
        {
            Constants.ReaderStatuses.DP_STATUS_BUSY => "Reader sedang digunakan oleh proses lain.",
            Constants.ReaderStatuses.DP_STATUS_NEED_CALIBRATION => "Reader perlu dikalibrasi.",
            Constants.ReaderStatuses.DP_STATUS_FAILURE => "Reader melaporkan kegagalan perangkat.",
            _ => $"Reader tidak siap ({status}).",
        };
        #endif

        #if HAS_DPURUNET
        private static Reader OpenFirstUsableReader(ReaderCollection readers)
        {
            var lastOpenError = Constants.ResultCode.DP_FAILURE;

            for (var i = 0; i < readers.Count; i++)
            {
                var candidate = readers[i];
                var opened = false;
                try
                {
                    lastOpenError = candidate.Open(Constants.CapturePriority.DP_PRIORITY_COOPERATIVE);
                    opened = lastOpenError == Constants.ResultCode.DP_SUCCESS;
                    if (opened)
                        return candidate;

                    Trace.WriteLine($"[FingerprintServices] Open('{candidate.Description?.Name}') = {lastOpenError}; mencoba reader berikutnya.");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[FingerprintServices] Open('{candidate.Description?.Name}') melempar {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    if (!opened)
                    {
                        try { candidate.Dispose(); } catch { }
                    }
                }
            }

            throw new FingerprintExceptions($"Perangkat fingerprint tidak dapat dibuka ({lastOpenError}). Hubungkan ulang perangkat lalu coba lagi.");
        }

        private Models.Fingerprint.Capture CaptureCore(int timeoutMs)
        {
            ReaderCollection? readers;
            try
            {
                readers = ReaderCollection.GetReaders();
            }
            catch (Exception ex)
            {
                throw new FingerprintExceptions($"Gagal mengakses perangkat: {ex.Message}");
            }

            if (readers is null || readers.Count == 0)
                throw new FingerprintExceptions("Perangkat Fingerprint Tidak Terdeteksi");

            var reader = OpenFirstUsableReader(readers);
            try
            {
                var calibrateResult = reader.Calibrate();
                if (calibrateResult != Constants.ResultCode.DP_SUCCESS)
                    Trace.WriteLine($"[FingerprintServices] Calibrate mengembalikan {calibrateResult}; capture tetap dilanjutkan.");

                var result = reader.Capture(
                    Constants.Formats.Fid.ANSI,
                    Constants.CaptureProcessing.DP_IMG_PROC_DEFAULT,
                    timeoutMs,
                    reader.Capabilities.Resolutions.First());

                if (result is null || result.ResultCode != Constants.ResultCode.DP_SUCCESS || result.Data is null)
                    throw new FingerprintExceptions(result is null
                        ? "Pemindaian gagal."
                        : $"Pemindaian gagal ({result.ResultCode}).");

                var verification = FeatureExtraction.CreateFmdFromFid(result.Data, Constants.Formats.Fmd.DP_VERIFICATION);
                if (verification.ResultCode != Constants.ResultCode.DP_SUCCESS || verification.Data is null)
                    throw new FingerprintExceptions($"Gagal mengekstrak sidik jari ({verification.ResultCode}).");

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

                return new Models.Fingerprint.Capture
                {
                    ImageBase64 = raw is null ? string.Empty : BaseService.GrayscaleToJpegBase64(raw, width, height),
                    VerificationTemplate = Convert.ToBase64String(verification.Data.Bytes),
                    RawImage = raw,
                    Width = width,
                    Height = height
                };
            }
            finally
            {
                try { reader.Dispose(); } catch { }
            }
        }
        #endif

        public void Dispose() => watcher.Dispose();
    }
}
