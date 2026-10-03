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
        /// <see cref="Matches"/>), dan Service 'usbvideo' adalah driver UVC.
        /// </summary>
        private const string DeviceQuery = "SELECT Name, DeviceID, PNPClass, Status, Service FROM Win32_PnPEntity " +
            "WHERE PNPClass = 'Camera' OR PNPClass = 'Image' OR Service = 'usbvideo' " +
            "OR Name LIKE '%Webcam%' OR Name LIKE '%Camera%'";

        private readonly CancellationTokenSource _cts = new();
        private ManagementEventWatcher? _arrivalWatcher;
        private Timer? _pollTimer;
        private int _scanning;
        private bool _disposed;
        public bool IsConnected { get; private set; }
        public string? DeviceName { get; private set; }
        public string? DeviceId { get; private set; }
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
                              !string.Equals(result.name, DeviceName, StringComparison.Ordinal);

                IsConnected = result.connected;
                DeviceName = result.name;
                DeviceId = result.id;

                if (changed)
                {
                    System.Diagnostics.Trace.WriteLine($"[CameraDeviceWatcher] StatusChanged raised: connected={result.connected}, name={result.name}");
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

        private (bool connected, string? name, string? id) Probe()
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

                var match = candidates.FirstOrDefault(c =>
                    IsPresent(c.status) && Matches(c.name, c.id, c.pnpClass, c.service));
                return match.name is null ? (false, null, null) : (true, match.name, match.id);
            }
            catch (Exception)
            {
                return (false, null, null);
            }
        }

        private static bool IsPresent(string status) =>
            string.IsNullOrEmpty(status) || status.Equals("OK", StringComparison.OrdinalIgnoreCase);

        private bool Matches(string name, string id, string pnpClass, string service)
        {
            // Windows 10+ exposes UVC webcams under the dedicated 'Camera' class.
            if (pnpClass.Equals("Camera", StringComparison.OrdinalIgnoreCase))
                return true;

            // The UVC driver is the strongest signal for a usable video capture device.
            if (service.Equals("usbvideo", StringComparison.OrdinalIgnoreCase))
                return true;

            // 'Image' is shared with scanners/printers, so a camera-ish name is required here.
            return cameraKeywords.Any(k => !string.IsNullOrWhiteSpace(k) &&
                (name.Contains(k, StringComparison.OrdinalIgnoreCase) || id.Contains(k, StringComparison.OrdinalIgnoreCase)));
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
