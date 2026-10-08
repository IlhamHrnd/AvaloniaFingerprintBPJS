using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.Services
{
    public class CameraDeviceWatcherServices(IConfiguration config, SessionService session) : BaseService(config, session), IDisposable
    {
        private const string CameraPnpClass = "Camera";

        private string DeviceQuery
        {
            get
            {
                var nameClauses = cameraKeywords
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Select(k => $"Name LIKE '%{k.Replace("'", "''")}%'")
                    .ToList();

                nameClauses.Add($"PNPClass = '{CameraPnpClass}'");

                return "SELECT Name, DeviceID, Status, PNPClass FROM Win32_PnPEntity WHERE " +
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
                var query = new WqlEventQuery(
                    "SELECT * FROM Win32_DeviceChangeEvent WITHIN 1 WHERE EventType = 2 OR EventType = 3");
                _arrivalWatcher = new ManagementEventWatcher(query);
                _arrivalWatcher.EventArrived += (_, _) => _ = ScanAsync();
                _arrivalWatcher.Start();
            }
            catch (Exception)
            {
                _arrivalWatcher = null;
            }
        }

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
            }
            finally
            {
                Interlocked.Exchange(ref _scanning, 0);
            }
        }

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

                var candidates = new List<(string name, string id, string status, string pnpClass)>();
                foreach (var o in collection)
                {
                    using var mo = (ManagementObject)o;
                    var name = mo["Name"] as string ?? string.Empty;
                    var id = mo["DeviceID"] as string ?? string.Empty;
                    var status = mo["Status"] as string ?? string.Empty;
                    var pnpClass = mo["PNPClass"] as string ?? string.Empty;
                    candidates.Add((name, id, status, pnpClass));
                }

                return Select(candidates);
            }
            catch (Exception)
            {
                return new ProbeResult(false, null, null, Array.Empty<string>());
            }
        }

        private static bool IsPresent(string status) =>
            string.IsNullOrEmpty(status) || status.Equals("OK", StringComparison.OrdinalIgnoreCase);

        private ProbeResult Select(List<(string name, string id, string status, string pnpClass)> candidates)
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

            var byClass = present.FirstOrDefault(c =>
                c.pnpClass.Equals(CameraPnpClass, StringComparison.OrdinalIgnoreCase));

            if (byClass.name is not null)
                return new ProbeResult(true, byClass.name, byClass.id, available);

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
            }
            _cts.Dispose();
        }
    }
}
