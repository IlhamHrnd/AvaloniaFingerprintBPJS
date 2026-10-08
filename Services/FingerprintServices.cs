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
            // The SDK probe is a method group on this instance, so the watcher has to be wired in the
            // constructor body: a field initializer cannot reference an instance member (CS0236).
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

        // Teks status dipakai bersama oleh indikator Header dan halaman fingerprint, sehingga
        // keduanya tidak mungkin menampilkan kondisi yang berbeda. Polanya sama persis dengan
        // CameraServices.ResolveStatus()/ResolveDetail().
        public const string SearchingStatus = "Mencari perangkat...";
        public const string ConnectedStatus = "Perangkat terhubung";
        public const string DetectedStatus = "Perangkat terdeteksi";
        public const string NotReadyStatus = "Perangkat tidak siap";
        public const string DisconnectedStatus = "Perangkat terputus";
        public const string UnavailableStatus = "Perangkat tidak tersedia";

        /// <summary>Detail saat benar-benar tidak ada reader yang terlihat.</summary>
        public const string DetailDisconnected = "Hubungkan USB fingerprint reader DigitalPersona";

        /// <summary>
        /// Satu-satunya sumber kebenaran status reader: mengirim snapshot lengkap (state + nama +
        /// penyebab) sehingga UI tidak perlu merakit sendiri kesimpulan dari beberapa flag yang
        /// bisa saling bertentangan.
        /// </summary>
        public event EventHandler<FingerprintDeviceState>? DeviceStateChanged;

        /// <summary>Snapshot device terakhir yang dipublikasikan watcher.</summary>
        public FingerprintDeviceState DeviceState => watcher.Current;

        /// <summary>
        /// Benar hanya bila reader sudah dibuka SDK, berstatus <c>DP_STATUS_READY</c>, dan mampu
        /// melakukan capture. Tidak ada jalur lain yang bisa menyalakan flag ini.
        /// </summary>
        public bool IsDeviceConnected => watcher.IsConnected;

        public string? DeviceName => watcher.DeviceName;

        public string? DeviceId => watcher.DeviceId;

        /// <summary>
        /// Teks status ringkas untuk indikator Header. Hanya <see cref="ConnectedStatus"/> yang
        /// membuat indikator hijau; device yang dilihat Windows tetapi tidak bisa dibuka SDK
        /// dilaporkan apa adanya, bukan sebagai "terhubung".
        /// </summary>
        public string ResolveStatus() => DeviceState.State switch
        {
            FingerprintReaderState.Ready => ConnectedStatus,
            FingerprintReaderState.Initializing => SearchingStatus,
            FingerprintReaderState.Detected => DetectedStatus,
            FingerprintReaderState.NotReady => NotReadyStatus,
            _ => IsSdkAvailable ? DisconnectedStatus : UnavailableStatus,
        };

        /// <summary>
        /// Keterangan lengkap (nama device, atau penyebab kegagalan) untuk tooltip Header dan
        /// halaman fingerprint, sehingga keduanya selalu menampilkan teks yang identik.
        /// </summary>
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
                    // Alasan spesifik dari probe (gagal dibuka, tidak siap, tidak bisa capture,
                    // atau perangkat biometrik yang tidak dapat dipakai) jauh lebih berguna bagi
                    // petugas daripada teks generik.
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

        /// <summary>Capture budget in ms, surfaced for the UI layer (mirrors <see cref="BaseService.captureTimeoutMs"/>).</summary>
        public int CaptureTimeoutMs => captureTimeoutMs;

        /// <summary>Verify budget in ms, surfaced for the UI layer (mirrors <see cref="BaseService.verifyTimeoutMs"/>).</summary>
        public int VerifyTimeoutMs => verifyTimeoutMs;

        /// <summary>Scans one finger. Throws <see cref="FingerprintExceptions"/> on any reader problem.</summary>
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

        /// <summary>
        /// Benar selama pemindaian berlangsung. Watcher memeriksanya dan menunda probe validasi
        /// supaya polling tidak membuka/merebut reader di tengah capture milik pengguna.
        /// </summary>
        public bool IsCaptureInFlight => Volatile.Read(ref _capturing) != 0;

        #if HAS_DPURUNET
        /// <summary>
        /// Pembungkus <see cref="CaptureCore"/> yang menyalakan <see cref="IsCaptureInFlight"/>
        /// selama pemindaian. Tanpa penjaga ini, probe yang berjalan tiap beberapa detik bisa
        /// mendapat DP_DEVICE_BUSY dan membuat indikator berkedip "terputus" padahal pemindaian
        /// sedang berlangsung normal.
        /// </summary>
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

        /// <summary>
        /// Human-readable message shown when the DigitalPersona SDK is missing at build time.
        /// Single source of truth is <see cref="BaseService"/>; exposed for the UI layer.
        /// </summary>
        public string SdkMissing => SdkMissingMessage;

        /// <summary>
        /// Memvalidasi reader lewat SDK. Enumerasi saja TIDAK cukup: DpHost bisa mendaftarkan reader
        /// (termasuk sensor WBF bawaan laptop) yang tampak rapi di daftar tetapi tidak benar-benar
        /// bisa dipakai. Karena itu setiap kandidat harus lolos Open + GetStatus + Status +
        /// CanCapture sebelum dianggap <see cref="FingerprintReaderState.Ready"/>.
        /// </summary>
        /// <remarks>
        /// PENTING: <see cref="ReaderCollection"/> sengaja TIDAK di-dispose. Teruji di perangkat ini,
        /// <c>ReaderCollection.Dispose()</c> membuat <c>GetReaders()</c> mengembalikan 0 reader selama
        /// 1-2 menit berikutnya sehingga aplikasi kehilangan device sama sekali. Sebaliknya
        /// <c>Reader.Dispose()</c> aman dan tetap dipanggil.
        /// </remarks>
        /// <returns>
        /// <c>null</c> bila probe tidak memberi pendapat (capture sedang berjalan atau reader sedang
        /// dipakai proses lain); watcher akan mempertahankan state terakhir.
        /// </returns>
        private FingerprintDeviceState? ProbeSdkReader(Action<FingerprintDeviceState> publish)
        {
        #if HAS_DPURUNET
            // Jangan membuka/memvalidasi reader selagi pengguna sedang memindai.
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

                // null = reader sedang dipakai (DP_DEVICE_BUSY): pertahankan state terakhir.
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
        /// <summary>
        /// Membuka reader lalu memeriksa status dan kemampuannya. Mengembalikan <c>null</c> bila
        /// reader sedang dipakai proses lain (<c>DP_DEVICE_BUSY</c>): kondisi itu bukan "tidak
        /// terhubung", jadi state terakhir dipertahankan.
        /// </summary>
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

                // GetStatus() hanya mengembalikan ResultCode pemanggilan; status reader yang
                // sesungguhnya baru terbaca dari reader.Status setelah GetStatus() sukses.
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
                // Reader.Dispose() teruji aman dan tidak mengganggu enumerasi berikutnya.
                try { reader.Dispose(); } catch { /* reader sudah hilang */ }
            }
        }

        /// <summary>
        /// Mengumumkan <see cref="FingerprintReaderState.Initializing"/> hanya saat benar-benar ada
        /// perpindahan, supaya indikator tidak berkedip Ready -&gt; Mencari -&gt; Ready setiap poll.
        /// </summary>
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
        /// <summary>
        /// Membuka reader pertama yang benar-benar dapat dipakai. DpHost bisa mendaftarkan beberapa
        /// reader (misalnya sensor WBF bawaan laptop) yang tampak rapi di daftar tetapi gagal dibuka,
        /// jadi mengambil <c>readers[0]</c> saja tidak cukup dan bisa membuat capture memakai reader
        /// yang berbeda dari yang dinyatakan Ready oleh <see cref="ProbeSdkReader"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="ReaderCollection"/> sengaja tidak di-dispose (lihat keterangan pada
        /// <see cref="ProbeSdkReader"/>); hanya <see cref="Reader"/> yang dilepas.
        /// </remarks>
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
                    // Kandidat yang tidak terpakai dilepas di sini; reader yang dikembalikan
                    // dilepas oleh pemanggil setelah capture selesai.
                    if (!opened)
                    {
                        try { candidate.Dispose(); } catch { /* reader sudah hilang */ }
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

            // Hasil Open() dulu diabaikan sama sekali, sehingga capture tetap dijalankan pada reader
            // yang tidak pernah terbuka dan gagal dengan pesan yang menyesatkan.
            var reader = OpenFirstUsableReader(readers);
            try
            {
                var calibrateResult = reader.Calibrate();
                if (calibrateResult != Constants.ResultCode.DP_SUCCESS)
                    // Beberapa reader (mis. sensor WBF) melaporkan HasCalibration=false sehingga
                    // Calibrate() tidak mengembalikan DP_SUCCESS. Itu bukan alasan membatalkan
                    // pemindaian, jadi cukup dicatat.
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
                try { reader.Dispose(); } catch { /* reader already gone */ }
            }
        }
        #endif

        public void Dispose() => watcher.Dispose();
    }
}
