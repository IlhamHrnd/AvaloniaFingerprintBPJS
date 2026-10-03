using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.Services
{
    /// <summary>
    /// Deteksi keberadaan kamera memakai mekanisme yang sama dengan
    /// <see cref="UsbDeviceWatcherServices"/>: WMI event (plug/unplug) sebagai jalur utama dan
    /// poll timer sebagai cadangan. Pemilihan kamera murni berbasis
    /// <see cref="BaseService.cameraKeywords"/>: device dianggap kamera bila namanya mengandung
    /// salah satu keyword. Tahap ini hanya memeriksa apakah device kamera tersedia;
    /// membuka stream, capture foto, dan pemrosesan wajah belum termasuk.
    /// </summary>
    public class CameraDeviceWatcherServices(IConfiguration config, SessionService session) : BaseService(config, session), IDisposable
    {
        /// <summary>
        /// Kueri kandidat kamera: device yang namanya mengandung salah satu
        /// <see cref="BaseService.cameraKeywords"/>. Menambah keyword otomatis memperluas
        /// pencarian tanpa mengubah kode lain — mis. "Windows Virtual Camera" untuk kamera HP
        /// yang diteruskan Link to Windows / Phone Link.
        /// </summary>
        private string DeviceQuery
        {
            get
            {
                var nameClauses = cameraKeywords
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Select(k => $"Name LIKE '%{k.Replace("'", "''")}%'")
                    .ToList();

                // Tanpa keyword tidak ada yang bisa dicocokkan; kembalikan hasil kosong
                // supaya kueri tetap valid.
                if (nameClauses.Count == 0)
                    return "SELECT Name, DeviceID, Status FROM Win32_PnPEntity WHERE 1 = 0";

                return "SELECT Name, DeviceID, Status FROM Win32_PnPEntity WHERE " +
                    string.Join(" OR ", nameClauses);
            }
        }

        private readonly CancellationTokenSource _cts = new();
        private ManagementEventWatcher? _arrivalWatcher;
        private Timer? _pollTimer;
        private int _scanning;
        private bool _disposed;

        public bool IsConnected { get; private set; }
        public string? DeviceName { get; private set; }
        public string? DeviceId { get; private set; }

        /// <summary>
        /// Nama semua kamera yang cocok keyword pada scan terakhir. Dipakai UI untuk menampilkan
        /// daftar kamera sehingga pengguna/developer bisa melihat device apa saja yang terdeteksi.
        /// </summary>
        public IReadOnlyList<string> AvailableCameras { get; private set; } = Array.Empty<string>();

        public event EventHandler<DeviceStatusEventArgs>? StatusChanged;

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

        /// <summary>Forces an immediate re-scan.</summary>
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

                var changed = result.Connected != IsConnected ||
                              !string.Equals(result.Name, DeviceName, StringComparison.Ordinal);

                IsConnected = result.Connected;
                DeviceName = result.Name;
                DeviceId = result.Id;
                AvailableCameras = result.Available;

                if (changed)
                {
                    System.Diagnostics.Trace.WriteLine($"[CameraDeviceWatcher] StatusChanged raised: connected={result.Connected}, name={result.Name}");

                    StatusChanged?.Invoke(this, new DeviceStatusEventArgs
                    {
                        Connected = result.Connected,
                        DeviceName = result.Name,
                        DeviceId = result.Id
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
            finally
            {
                Interlocked.Exchange(ref _scanning, 0);
            }
        }

        /// <summary>
        /// Hasil satu siklus deteksi: kamera terpilih (bila ada) beserta daftar nama kamera yang
        /// terdeteksi, supaya UI bisa menampilkan device mana saja yang dikenali Windows.
        /// </summary>
        private readonly record struct ProbeResult(
            bool Connected,
            string? Name,
            string? Id,
            IReadOnlyList<string> Available);

        private ProbeResult Probe()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(new ObjectQuery(DeviceQuery));
                using var collection = searcher.Get();

                var candidates = new List<(string name, string id, string status)>();
                foreach (var o in collection)
                {
                    using var mo = (ManagementObject)o;
                    var name = mo["Name"] as string ?? string.Empty;
                    var id = mo["DeviceID"] as string ?? string.Empty;
                    var status = mo["Status"] as string ?? string.Empty;
                    candidates.Add((name, id, status));
                }

                return Select(candidates);
            }
            catch (Exception)
            {
                return new ProbeResult(false, null, null, Array.Empty<string>());
            }
        }

        /// <summary>
        /// True bila device benar-benar hadir (status kosong atau "OK"). Device yang hilang/error
        /// tetap muncul di WMI sesaat setelah dicabut, sehingga tidak boleh dianggap kamera aktif.
        /// </summary>
        private static bool IsPresent(string status) =>
            string.IsNullOrEmpty(status) || status.Equals("OK", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Memilih kamera pertama yang namanya cocok dengan keyword, diuji berurutan sesuai urutan
        /// <see cref="BaseService.cameraKeywords"/> (urutan array = prioritas). Bila tidak ada
        /// keyword yang cocok, kamera dianggap tidak ditemukan.
        /// </summary>
        private ProbeResult Select(List<(string name, string id, string status)> candidates)
        {
            var present = candidates
                .Where(c => !string.IsNullOrWhiteSpace(c.name) && IsPresent(c.status))
                .ToList();

            var available = present
                .Select(c => c.name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var keyword in cameraKeywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                var match = present.FirstOrDefault(c =>
                    c.name.Contains(keyword, StringComparison.OrdinalIgnoreCase));

                if (match.name is not null)
                    return new ProbeResult(true, match.name, match.id, available);
            }

            return new ProbeResult(false, null, null, available);
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
                // ignore teardown failures
            }
            _cts.Dispose();
        }
    }
}
