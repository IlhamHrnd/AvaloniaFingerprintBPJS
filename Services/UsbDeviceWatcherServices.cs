using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.Services
{
    /// <summary>
    /// Watcher device sidik jari. Jalur otoritatifnya adalah probe SDK (<see cref="FingerprintSdkProbe"/>):
    /// hanya SDK yang dapat membuktikan sebuah reader benar-benar bisa dibuka dan dipakai.
    /// Query WMI dipertahankan SEMATA-MATA sebagai diagnostik — hasilnya maksimal
    /// <see cref="FingerprintReaderState.Detected"/> dan tidak pernah dianggap terhubung.
    /// </summary>
    public class UsbDeviceWatcherServices(
        IConfiguration config,
        SessionService session,
        FingerprintSdkProbe? sdkProbe = null) : BaseService(config, session), IDisposable
    {
        private const string DeviceQuery = "SELECT Name, DeviceID, PNPClass, Status, Present, ConfigManagerErrorCode FROM Win32_PnPEntity " +
            "WHERE PNPClass = 'Biometric' OR DeviceID LIKE '%VID_05BA%' " +
            "OR Name LIKE '%Fingerprint%' OR Name LIKE '%U.are.U%' OR Name LIKE '%Biometric%'";
        /// <summary>
        /// Detail untuk perangkat biometrik yang dilihat Windows tetapi tidak bisa dibuka SDK.
        /// State ini sengaja BUKAN "terhubung": sensor WBF bawaan laptop, kamera IR Windows Hello,
        /// dan entri ghost sama-sama cocok dengan kata kunci di <see cref="Matches"/>.
        /// </summary>
        public const string DetailDetectedButUnusable =
            "Perangkat biometrik terlihat di Windows tetapi tidak dapat dibuka oleh SDK DigitalPersona.";

        private readonly FingerprintSdkProbe? _sdkProbe = sdkProbe;
        private readonly CancellationTokenSource _cts = new();
        private readonly object _stateLock = new();
        private ManagementEventWatcher? _arrivalWatcher;
        private Timer? _pollTimer;
        private int _scanning;
        private int _noReaderMisses;
        private bool _disposed;

        /// <summary>
        /// Jumlah probe berturut-turut yang harus melaporkan "tidak ada reader" sebelum status
        /// diubah menjadi terputus. <c>ReaderCollection.GetReaders()</c> teruji bisa sesekali
        /// mengembalikan 0 reader tepat setelah handle reader dilepas; tanpa debounce indikator
        /// akan berkedip Terhubung -&gt; Terputus -&gt; Terhubung.
        /// </summary>
        private const int NoReaderMissThreshold = 2;

        /// <summary>Snapshot device terakhir. Selalu diganti utuh, tidak pernah dimutasi sebagian.</summary>
        public FingerprintDeviceState Current { get; private set; } = FingerprintDeviceState.None;

        /// <summary>Benar hanya bila reader tervalidasi SDK dan berstatus siap.</summary>
        public bool IsConnected => Current.IsConnected;

        public string? DeviceName => Current.DeviceName;

        public string? DeviceId => Current.DeviceId;

        /// <summary>Dipicu dari thread probe; konsumen wajib marshal ke UI thread sendiri.</summary>
        public event EventHandler<FingerprintDeviceState>? StatusChanged;
        public void Start()
        {
            TryStartEventWatcher();

            var interval = TimeSpan.FromSeconds(Math.Max(1, pollIntervalSeconds));
            _pollTimer = new Timer(_ => _ = ScanAsync(), null, TimeSpan.Zero, interval);
        }

        private void TryStartEventWatcher()
        {
            try
            {
                // EventType 2 = device arrival, 3 = device removal.
                var query = new WqlEventQuery(
                    "SELECT * FROM Win32_DeviceChangeEvent WITHIN 1 WHERE EventType = 2 OR EventType = 3");
                _arrivalWatcher = new ManagementEventWatcher(query);
                _arrivalWatcher.EventArrived += (_, _) => _ = ScanAsync();
                _arrivalWatcher.Start();
            }
            catch (Exception)
            {
                // WMI eventing can be blocked by policy or unavailable in a session; the
                // poll timer below still gives us connect/disconnect detection.
                _arrivalWatcher = null;
            }
        }

        /// <summary>Forces an immediate re-scan (used after a capture error, for example).</summary>
        public Task RefreshAsync() => ScanAsync();

        private async Task ScanAsync()
        {
            if (_disposed || Interlocked.Exchange(ref _scanning, 1) == 1)
                return;

            try
            {
                var result = await Task.Run(Probe, _cts.Token).ConfigureAwait(false);
                if (_disposed)
                    return;

                // null = probe tidak memberi pendapat (capture sedang berjalan atau reader sedang
                // dipakai proses lain). State terakhir dipertahankan supaya header tidak berkedip
                // menjadi "terputus" di tengah pemindaian.
                if (result is not null)
                    Publish(ApplyNoReaderDebounce(result));
            }
            catch (OperationCanceledException)
            {
                // Pembatalan normal: aplikasi sedang ditutup, bukan kegagalan probe.
            }
            finally
            {
                Interlocked.Exchange(ref _scanning, 0);
            }
        }

        /// <summary>
        /// Mengganti snapshot device dan memicu <see cref="StatusChanged"/> bila berubah.
        /// Perbandingan memakai kesetaraan record, jadi perubahan alasan/detail (misalnya
        /// Ready -&gt; NotReady dengan nama device yang sama) tetap terlihat oleh UI.
        /// </summary>
        private void Publish(FingerprintDeviceState state)
        {
            FingerprintDeviceState previous;

            lock (_stateLock)
            {
                previous = Current;
                if (state.Equals(previous))
                    return;

                // Snapshot lama (beserta DeviceName/DeviceId/Detail-nya) dibuang utuh di sini,
                // jadi tidak ada sisa state yang bisa bertahan setelah reader dicabut.
                Current = state;
            }

            Trace.WriteLine($"[UsbDeviceWatcher] {previous.State} -> {state.State}. Connected={state.IsConnected}, Name={state.DeviceName ?? "-"}, Id={state.DeviceId ?? "-"}, Detail={state.Detail ?? "-"}");

            StatusChanged?.Invoke(this, state);
        }

        /// <summary>
        /// Menunda pengumuman "tidak ada reader" sampai terlihat <see cref="NoReaderMissThreshold"/>
        /// kali berturut-turut. Saat masih ditunda, state sebelumnya dikembalikan apa adanya sehingga
        /// <see cref="Publish"/> tidak melihat perubahan dan tidak memicu event.
        /// </summary>
        private FingerprintDeviceState ApplyNoReaderDebounce(FingerprintDeviceState result)
        {
            if (result.State != FingerprintReaderState.NoDevice)
            {
                Interlocked.Exchange(ref _noReaderMisses, 0);
                return result;
            }

            var misses = Interlocked.Increment(ref _noReaderMisses);

            // Hanya status "terhubung" yang perlu dikonfirmasi ulang. Saat memang belum pernah
            // terhubung, tidak ada yang perlu ditunda sehingga deteksi tetap langsung.
            if (Current.IsConnected && misses < NoReaderMissThreshold)
            {
                Trace.WriteLine($"[UsbDeviceWatcher] SDK tidak melihat reader (konfirmasi {misses}/{NoReaderMissThreshold}); status terhubung dipertahankan sementara.");
                return Current;
            }

            return result;
        }

        private FingerprintDeviceState? Probe()
        {
            // Daftar reader SDK adalah jawaban otoritatif: hanya SDK yang tahu reader mana yang
            // benar-benar bisa dibuka, berstatus siap, dan mampu melakukan capture.
            if (_sdkProbe is not null)
            {
                try
                {
                    var sdk = _sdkProbe(Publish);

                    // null = probe tidak memberi pendapat (capture sedang berjalan atau reader
                    // sedang dipakai proses lain). State terakhir dipertahankan.
                    if (sdk is null)
                    {
                        Trace.WriteLine("[UsbDeviceWatcher] Probe SDK menunda (reader sedang dipakai); state terakhir dipertahankan.");
                        return null;
                    }

                    // Jawaban SDK final selama bukan NoDevice. Sebelumnya jalur ini jatuh ke WMI
                    // dan WMI bisa menimpanya menjadi "terhubung" hanya karena melihat perangkat
                    // biometrik apa pun — itulah sumber false positive-nya.
                    if (sdk.State != FingerprintReaderState.NoDevice)
                        return sdk;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[UsbDeviceWatcher] Probe SDK gagal: {ex.GetType().Name}: {ex.Message}");
                }
            }

            // SDK tidak melihat reader (atau SDK memang tidak tersedia saat build). WMI hanya
            // dipakai untuk menjelaskan kondisi ke pengguna, tidak untuk menyatakan terhubung.
            return ProbeWmi();
        }

        private FingerprintDeviceState ProbeWmi()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(new ObjectQuery(DeviceQuery));
                using var collection = searcher.Get();

                var candidates = new List<(string name, string id, string pnpClass, string status, bool present, int errorCode)>();
                foreach (var o in collection)
                {
                    using var mo = (ManagementObject)o;
                    var name = mo["Name"] as string ?? string.Empty;
                    var id = mo["DeviceID"] as string ?? string.Empty;
                    var pnpClass = mo["PNPClass"] as string ?? string.Empty;
                    var status = mo["Status"] as string ?? string.Empty;
                    var present = mo["Present"] as bool? ?? false;
                    var errorCode = mo["ConfigManagerErrorCode"] is null ? 0 : Convert.ToInt32(mo["ConfigManagerErrorCode"]);
                    candidates.Add((name, id, pnpClass, status, present, errorCode));
                }

                foreach (var candidate in candidates)
                {
                    if (!IsPresent(candidate.present, candidate.errorCode, candidate.status))
                        continue;
                    if (!Matches(candidate.name, candidate.id, candidate.pnpClass))
                        continue;

                    Trace.WriteLine($"[UsbDeviceWatcher] Kandidat WMI '{candidate.name}' ({candidate.id}, PNPClass={candidate.pnpClass}) terlihat, tetapi SDK tidak melaporkan reader yang bisa dipakai. TIDAK dianggap terhubung.");

                    return FingerprintDeviceState.AsDetected(candidate.name, candidate.id, DetailDetectedButUnusable);
                }

                Trace.WriteLine("[UsbDeviceWatcher] Tidak ada reader SDK dan tidak ada kandidat WMI yang benar-benar terpasang.");
                return FingerprintDeviceState.AsNoDevice();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[UsbDeviceWatcher] Probe WMI gagal: {ex.GetType().Name}: {ex.Message}");
                return FingerprintDeviceState.AsNoDevice();
            }
        }

        /// <summary>
        /// Hanya perangkat yang benar-benar terpasang yang dihitung. Entri ghost tetap muncul di
        /// <c>Win32_PnPEntity</c> setelah device dicabut (<c>Present=false</c>,
        /// <c>ConfigManagerErrorCode=45</c>); dulu entri seperti itu tetap membuat status bertahan
        /// "terhubung" karena yang diperiksa hanya kolom <c>Status</c>.
        /// </summary>
        private static bool IsPresent(bool present, int configManagerErrorCode, string status) =>
            present
            && configManagerErrorCode == 0
            && (string.IsNullOrEmpty(status) || status.Equals("OK", StringComparison.OrdinalIgnoreCase));

        private bool Matches(string name, string id, string pnpClass)
        {
            if (pnpClass.Equals("Biometric", StringComparison.OrdinalIgnoreCase))
                return true;
            if (id.Contains("VID_05BA", StringComparison.OrdinalIgnoreCase))
                return true;

            return matchKeywords.Any(k => !string.IsNullOrWhiteSpace(k) && (name.Contains(k, StringComparison.OrdinalIgnoreCase) || id.Contains(k, StringComparison.OrdinalIgnoreCase)));
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _cts.Cancel();
            _pollTimer?.Dispose();
            try
            {
                _arrivalWatcher?.Stop();
                _arrivalWatcher?.Dispose();
            }
            catch (Exception)
            {
                // Teardown tidak boleh melempar keluar Dispose(): langkah pembersihan berikutnya
                // (_cts.Dispose) tetap harus dijalankan.
            }
            _cts.Dispose();
        }
    }

    /// <summary>
    /// Argumen event deteksi device. Kelas ini dipakai bersama oleh watcher fingerprint dan
    /// <see cref="CameraDeviceWatcherServices"/>, jadi bentuk serta lokasinya tidak boleh diubah
    /// sepihak. Status fingerprint sendiri sekarang dibawa oleh <see cref="FingerprintDeviceState"/>,
    /// yang jauh lebih kaya daripada sekadar flag "connected".
    /// </summary>
    public sealed class DeviceStatusEventArgs : EventArgs
    {
        public bool Connected { get; init; }
        public string? DeviceName { get; init; }
        public string? DeviceId { get; init; }
    }
}
