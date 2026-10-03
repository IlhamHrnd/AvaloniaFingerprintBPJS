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
    /// poll timer sebagai cadangan. Tahap ini hanya memeriksa apakah device kamera tersedia;
    /// membuka stream, capture foto, dan pemrosesan wajah belum termasuk.
    /// </summary>
    public class CameraDeviceWatcherServices(IConfiguration config, SessionService session) : BaseService(config, session), IDisposable
    {
        /// <summary>
        /// Kandidat device kamera. PNPClass 'Camera' adalah class resmi Windows 10+, 'Image'
        /// dipakai driver lama (class ini juga dipakai scanner/printer sehingga disaring lagi oleh
        /// <see cref="Select"/>), dan Service 'usbvideo' adalah driver UVC. Klausa nama dibangun
        /// dari <see cref="cameraKeywords"/> supaya menambah keyword otomatis memperluas
        /// pencarian tanpa mengubah logic di tempat lain.
        /// </summary>
        private string DeviceQuery
        {
            get
            {
                var nameClauses = cameraKeywords
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Select(k => $"Name LIKE '%{k.Replace("'", "''")}%'");

                return "SELECT Name, DeviceID, PNPClass, Status, Service FROM Win32_PnPEntity WHERE " +
                    "PNPClass = 'Camera' OR PNPClass = 'Image' OR Service = 'usbvideo' OR " +
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
        /// Menyimpan cara kamera aktif dipilih (keyword atau fallback), sehingga Header bisa
        /// menampilkan detail yang sesuai tanpa menebak ulang.
        /// </summary>
        public CameraMatchType MatchType { get; private set; }

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

                var changed = result.connected != IsConnected ||
                              result.matchType != MatchType ||
                              !string.Equals(result.name, DeviceName, StringComparison.Ordinal);

                IsConnected = result.connected;
                DeviceName = result.name;
                DeviceId = result.id;
                MatchType = result.matchType;

                if (changed)
                {
                    System.Diagnostics.Trace.WriteLine($"[CameraDeviceWatcher] StatusChanged raised: connected={result.connected}, name={result.name}, matchType={result.matchType}");
                    StatusChanged?.Invoke(this, new DeviceStatusEventArgs
                    {
                        Connected = result.connected,
                        DeviceName = result.name,
                        DeviceId = result.id
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

        private (bool connected, string? name, string? id, CameraMatchType matchType) Probe()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(new ObjectQuery(DeviceQuery));
                using var collection = searcher.Get();

                var candidates = new List<(string name, string id, string pnpClass, string status, string service)>();
                foreach (var o in collection)
                {
                    using var mo = (ManagementObject)o;
                    var name = mo["Name"] as string ?? string.Empty;
                    var id = mo["DeviceID"] as string ?? string.Empty;
                    var pnpClass = mo["PNPClass"] as string ?? string.Empty;
                    var status = mo["Status"] as string ?? string.Empty;
                    var service = mo["Service"] as string ?? string.Empty;
                    candidates.Add((name, id, pnpClass, status, service));
                }

                return Select(candidates);
            }
            catch (Exception)
            {
                return (false, null, null, CameraMatchType.None);
            }
        }

        private static bool IsPresent(string status) =>
            string.IsNullOrEmpty(status) || status.Equals("OK", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Memilih kamera aktif dari daftar kandidat dengan dua tahap:
        /// <list type="number">
        /// <item>Tahap 1 — prioritas <see cref="cameraKeywords"/>. Keyword diuji berurutan sesuai
        /// urutan array (Webcam → Facecam → Integrated Camera → USB Video Device → UVC), jadi
        /// device pertama yang cocok dengan keyword berprioritas tertinggi yang dipakai.</item>
        /// <item>Tahap 2 — fallback. Bila tidak ada keyword yang cocok, kamera lain yang tersedia
        /// tetap dipakai asalkan terdeteksi sebagai video capture device yang valid.</item>
        /// </list>
        /// Pencocokan tidak case-sensitive, sehingga "Integrated Camera", "Integrated Camera HD",
        /// dan "Integrated CAMERA" sama-sama dianggap cocok.
        /// </summary>
        private (bool connected, string? name, string? id, CameraMatchType matchType) Select(
            List<(string name, string id, string pnpClass, string status, string service)> candidates)
        {
            // Hanya device yang benar-benar tersedia yang ikut dipertimbangkan.
            var available = candidates.Where(c => IsPresent(c.status) && !IsExcluded(c.name, c.pnpClass, c.service)).ToList();

            // Tahap 1: keyword diurutkan sebagai prioritas (loop luar = prioritas).
            foreach (var keyword in cameraKeywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                var byKeyword = available.FirstOrDefault(c => ContainsKeyword(c.name, c.id, keyword));
                if (byKeyword.name is not null)
                    return (true, byKeyword.name, byKeyword.id, CameraMatchType.Keyword);
            }

            // Tahap 2: fallback ke camera device lain yang valid, bukan device 'Image' generik.
            var fallback = available.FirstOrDefault(c => IsVideoCaptureDevice(c.pnpClass, c.service));
            return fallback.name is null
                ? (false, null, null, CameraMatchType.None)
                : (true, fallback.name, fallback.id, CameraMatchType.Fallback);
        }

        private static bool ContainsKeyword(string name, string id, string keyword) =>
            name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            id.Contains(keyword, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Sinyal positif bahwa sebuah device adalah video capture device: class 'Camera' resmi
        /// Windows 10+ atau driver UVC 'usbvideo'. Class 'Image' sengaja tidak dihitung di sini
        /// karena dipakai bersama scanner/printer.
        /// </summary>
        private static bool IsVideoCaptureDevice(string pnpClass, string service) =>
            pnpClass.Equals("Camera", StringComparison.OrdinalIgnoreCase) ||
            service.Equals("usbvideo", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Menyaring device yang bukan kamera meski namanya mengandung kata "camera". Ini daftar
        /// penanda jenis device (bukan daftar merek/model), jadi tetap tidak ada device yang
        /// di-hard-code.
        /// </summary>
        private static bool IsExcluded(string name, string pnpClass, string service)
        {
            if (service.Equals("usbscan", StringComparison.OrdinalIgnoreCase))
                return true;

            if (pnpClass.Equals("SoftwareDevice", StringComparison.OrdinalIgnoreCase) ||
                pnpClass.Equals("MEDIA", StringComparison.OrdinalIgnoreCase))
                return true;

            return name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Barcode", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Scanner", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Scan", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Printer", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// Menjelaskan bagaimana kamera aktif dipilih oleh <see cref="CameraDeviceWatcherServices.Select"/>.
    /// </summary>
    public enum CameraMatchType
    {
        /// <summary>Tidak ada kamera yang terdeteksi.</summary>
        None,

        /// <summary>Cocok dengan salah satu keyword di <c>cameraKeywords</c>.</summary>
        Keyword,

        /// <summary>Kamera valid yang tidak cocok keyword apa pun, dipakai sebagai cadangan.</summary>
        Fallback
    }
}
