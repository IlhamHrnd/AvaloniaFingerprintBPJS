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
        /// Kandidat device kamera. Ini sengaja dibuat luas: kueri hanya menyaring "kandidat",
        /// sedangkan keputusan device benar-benar kamera atau tidak diambil oleh
        /// <see cref="IsUsableCamera"/> berdasarkan bukti positif (bukan nama).
        /// <list type="bullet">
        /// <item>'Camera' — class kamera resmi Windows 10+.</item>
        /// <item>'Image' — driver lama; class ini juga dipakai scanner/printer, sehingga
        /// disaring lagi lewat Service 'usbscan'.</item>
        /// <item>'SoftwareDevice' — di sinilah kamera virtual Windows berada, termasuk kamera HP
        /// yang diteruskan Link to Windows / Phone Link (mis. "Windows Virtual Camera Device"
        /// dengan DeviceID <c>SWD\VCAMDEVAPI\...</c>). Tanpa class ini kamera HP tidak pernah
        /// masuk daftar kandidat.</item>
        /// <item>'usbvideo' — driver UVC.</item>
        /// <item>Klausa nama dibangun dari <see cref="cameraKeywords"/> supaya menambah keyword
        /// otomatis memperluas pencarian.</item>
        /// </list>
        /// </summary>
        private string DeviceQuery
        {
            get
            {
                var nameClauses = cameraKeywords
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Select(k => $"Name LIKE '%{k.Replace("'", "''")}%'");

                return "SELECT Name, DeviceID, PNPClass, Status, Service FROM Win32_PnPEntity WHERE " +
                    "PNPClass = 'Camera' OR PNPClass = 'Image' OR PNPClass = 'SoftwareDevice' OR " +
                    "Service = 'usbvideo' OR " +
                    string.Join(" OR ", nameClauses);
            }
        }

        /// <summary>
        /// DeviceID kamera virtual Windows (Windows Virtual Camera API). Ini jalur yang dipakai
        /// Link to Windows / Phone Link untuk mengekspos kamera HP sebagai camera device Windows.
        /// </summary>
        private const string VirtualCameraIdPrefix = @"SWD\VCAMDEVAPI\";

        /// <summary>
        /// GUID device-class DirectShow untuk video capture device. Isi key ini adalah device
        /// yang benar-benar terdaftar sebagai kamera di Windows, apa pun nama/class-nya. Dipakai
        /// sebagai bukti positif tambahan supaya kamera virtual pihak ketiga (mis. OBS) ikut
        /// terdeteksi tanpa bergantung pada nama device.
        /// </summary>
        private static readonly string[] CaptureDeviceClassKeys =
        {
            @"SYSTEM\CurrentControlSet\Control\DeviceClasses\{e5323777-f976-4f5b-9b55-b94699c46e44}",
            @"SYSTEM\CurrentControlSet\Control\DeviceClasses\{65e8773d-8f56-11d0-a3b9-00a0c9223196}"
        };

        private readonly CancellationTokenSource _cts = new();
        private ManagementEventWatcher? _arrivalWatcher;
        private Timer? _pollTimer;
        private int _scanning;
        private bool _disposed;
        public bool IsConnected { get; private set; }
        public string? DeviceName { get; private set; }
        public string? DeviceId { get; private set; }

        /// <summary>
        /// Nama semua device yang lolos bukti positif sebagai kamera (bukan hanya yang terpilih).
        /// Dipakai UI untuk menampilkan daftar kamera sehingga pengguna/developer bisa melihat
        /// device apa saja yang terdeteksi.
        /// </summary>
        public IReadOnlyList<string> AvailableCameras { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// Nama semua device kandidat yang hadir pada scan terakhir — termasuk yang akhirnya tidak
        /// bisa dipakai. Dipakai untuk membedakan "kamera tidak ditemukan" dari "kamera ditemukan,
        /// tetapi tidak dapat digunakan".
        /// </summary>
        public IReadOnlyList<string> DetectedDevices { get; private set; } = Array.Empty<string>();

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

                var changed = result.Connected != IsConnected ||
                              result.MatchType != MatchType ||
                              !string.Equals(result.Name, DeviceName, StringComparison.Ordinal);

                IsConnected = result.Connected;
                DeviceName = result.Name;
                DeviceId = result.Id;
                MatchType = result.MatchType;
                AvailableCameras = result.Available;
                DetectedDevices = result.Detected;

                if (changed)
                {
                    System.Diagnostics.Trace.WriteLine($"[CameraDeviceWatcher] StatusChanged raised: connected={result.Connected}, name={result.Name}, matchType={result.MatchType}, available=[{string.Join(", ", result.Available)}]");
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
        /// Hasil satu siklus deteksi. Selain kamera terpilih, hasil ini membawa daftar device yang
        /// ditemukan supaya UI bisa menampilkan nama kamera (memudahkan pengecekan) dan bisa
        /// membedakan "tidak ada kamera" dari "ada device tapi tidak dapat digunakan".
        /// </summary>
        private readonly record struct ProbeResult(
            bool Connected,
            string? Name,
            string? Id,
            CameraMatchType MatchType,
            IReadOnlyList<string> Detected,
            IReadOnlyList<string> Available);

        private ProbeResult Probe()
        {
            try
            {
                // Bukti positif dari registry DirectShow dibaca sekali per scan.
                var registeredCaptureIds = ReadRegisteredCaptureDeviceIds();

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

                return Select(candidates, registeredCaptureIds);
            }
            catch (Exception)
            {
                return new ProbeResult(
                    false, null, null, CameraMatchType.None, Array.Empty<string>(), Array.Empty<string>());
            }
        }

        /// <summary>
        /// True bila device benar-benar hadir (status kosong atau "OK"). Device yang hilang/error
        /// tetap muncul di WMI sesaat setelah dicabut, sehingga tidak boleh dianggap kamera aktif.
        /// </summary>
        private static bool IsPresent(string status) =>
            string.IsNullOrEmpty(status) || status.Equals("OK", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Memilih kamera terbaik dari semua kandidat.
        /// Tahap 1: keyword (<see cref="cameraKeywords"/>) hanya sebagai <b>prioritas</b> — bukan
        /// filter, sehingga device yang namanya tidak dikenal tetap bisa terpilih.
        /// Tahap 2: fallback ke kamera valid pertama, apa pun nama/class-nya. Ini yang membuat
        /// kamera HP / kamera virtual (Phone Link, OBS) tetap terdeteksi tanpa menambah keyword.
        /// </summary>
        private ProbeResult Select(
            List<(string name, string id, string pnpClass, string status, string service)> candidates,
            HashSet<string> registeredCaptureIds)
        {
            var present = candidates
                .Where(c => !string.IsNullOrWhiteSpace(c.name) && IsPresent(c.status))
                .ToList();

            var detected = present
                .Select(c => c.name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var available = present
                .Where(c => IsUsableCamera(c.pnpClass, c.service, c.id, registeredCaptureIds))
                .ToList();

            var availableNames = available
                .Select(c => c.name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Tahap 1: keyword diurutkan sebagai prioritas (loop luar = prioritas).
            foreach (var keyword in cameraKeywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                var byKeyword = available.FirstOrDefault(c => ContainsKeyword(c.name, c.id, keyword));
                if (byKeyword.name is not null)
                    return new ProbeResult(
                        true, byKeyword.name, byKeyword.id, CameraMatchType.Keyword, detected, availableNames);
            }

            // Tahap 2: fallback ke kamera valid pertama.
            var fallback = available.FirstOrDefault();
            return fallback.name is null
                ? new ProbeResult(false, null, null, CameraMatchType.None, detected, availableNames)
                : new ProbeResult(
                    true, fallback.name, fallback.id, CameraMatchType.Fallback, detected, availableNames);
        }

        private static bool ContainsKeyword(string name, string id, string keyword) =>
            name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            id.Contains(keyword, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Membaca daftar instance ID kamera yang terdaftar pada device-class DirectShow. Registry
        /// ini adalah sumber paling netral: apa pun vendor/nama device-nya, kalau Windows
        /// mendaftarkannya sebagai video capture device maka ia muncul di sini.
        /// Nilainya berbentuk <c>##?#SWD#VCAMDEVAPI#...</c> sehingga dipisahkan oleh '#'.
        /// </summary>
        private static HashSet<string> ReadRegisteredCaptureDeviceIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var keyPath in CaptureDeviceClassKeys)
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
                    if (key is null)
                        continue;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        var normalized = NormalizeDeviceInstanceId(subKeyName);
                        if (!string.IsNullOrWhiteSpace(normalized))
                            ids.Add(normalized);
                    }
                }
                catch (Exception)
                {
                    // Registry bisa diblokir policy; deteksi tetap jalan lewat jalur lain.
                }
            }

            return ids;
        }

        /// <summary>
        /// Mengubah nama subkey device-class (<c>##?#SWD#VCAMDEVAPI#ABC#{"Guid"}</c>) menjadi
        /// instance ID PNP (<c>SWD\VCAMDEVAPI\ABC</c>) supaya bisa dicocokkan dengan DeviceID WMI.
        /// </summary>
        private static string NormalizeDeviceInstanceId(string subKeyName)
        {
            var value = subKeyName;
            if (value.StartsWith("##?#", StringComparison.Ordinal))
                value = value[4..];

            // Buang bagian GUID interface di belakang (mulai '{').
            var braceIndex = value.IndexOf('{');
            if (braceIndex >= 0)
                value = value[..braceIndex];

            return value.Replace('#', '\\').TrimEnd('\\');
        }

        /// <summary>
        /// Menentukan apakah device benar-benar kamera memakai <b>bukti positif</b>, bukan nama.
        /// Kamera HP lewat Phone Link tampil sebagai SoftwareDevice 'Windows Virtual Camera Device'
        /// yang namanya tidak mengandung kata "camera", jadi penyaringan berbasis nama
        /// (mis. <c>Contains("USB")</c> / <c>Contains("Webcam")</c>) tidak boleh dipakai.
        /// </summary>
        private static bool IsUsableCamera(
            string pnpClass, string service, string id, HashSet<string> registeredCaptureIds)
        {
            if (service.Equals("usbscan", StringComparison.OrdinalIgnoreCase))
                return false; // scanner/printer memakai class 'Image'

            if (pnpClass.Equals("Camera", StringComparison.OrdinalIgnoreCase) ||
                service.Equals("usbvideo", StringComparison.OrdinalIgnoreCase))
                return true;

            if (id.StartsWith(VirtualCameraIdPrefix, StringComparison.OrdinalIgnoreCase))
                return true;

            return IsRegisteredCaptureDevice(id, registeredCaptureIds);
        }

        /// <summary>
        /// Mencocokkan DeviceID WMI dengan daftar device-class DirectShow secara longgar, supaya
        /// perbedaan prefix tidak membuat device valid terlewat.
        /// </summary>
        private static bool IsRegisteredCaptureDevice(string id, HashSet<string> registeredCaptureIds)
        {
            if (string.IsNullOrWhiteSpace(id) || registeredCaptureIds.Count == 0)
                return false;

            var normalized = id.Replace('#', '\\');

            foreach (var registered in registeredCaptureIds)
            {
                if (registered.Length == 0)
                    continue;

                if (normalized.StartsWith(registered, StringComparison.OrdinalIgnoreCase) ||
                    registered.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains(registered, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
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
