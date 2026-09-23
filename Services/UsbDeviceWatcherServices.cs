using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.Services
{
    public class UsbDeviceWatcherServices(IConfiguration config, SessionService session, Func<(bool connected, string? name)>? sdkProbe = null) : BaseService(config, session), IDisposable
    {
        private const string DeviceQuery = "SELECT Name, DeviceID, PNPClass, Status FROM Win32_PnPEntity " + 
            "WHERE PNPClass = 'Biometric' OR DeviceID LIKE '%VID_05BA%' " +
            "OR Name LIKE '%Fingerprint%' OR Name LIKE '%U.are.U%' OR Name LIKE '%Biometric%'";
        private readonly Func<(bool connected, string? name)>? _sdkProbe = sdkProbe;
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

                var changed = result.connected != IsConnected ||
                              !string.Equals(result.name, DeviceName, StringComparison.Ordinal);

                IsConnected = result.connected;
                DeviceName = result.name;
                DeviceId = result.id;

                if (changed)
                {
                    System.Diagnostics.Trace.WriteLine($"[UsbDeviceWatcher] StatusChanged raised: connected={result.connected}, name={result.name}");
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
            // The SDK's own reader list is the authoritative answer when it is available:
            // it only reports readers the DigitalPersona driver has actually claimed.
            if (_sdkProbe is not null)
            {
                try
                {
                    var (connected, name) = _sdkProbe();
                    if (connected)
                        return (true, name, null);
                }
                catch (Exception)
                {
                    // fall through to WMI
                }
            }

            return ProbeWmi();
        }

        private (bool connected, string? name, string? id) ProbeWmi()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(new ObjectQuery(DeviceQuery));
                using var collection = searcher.Get();

                var candidates = new List<(string name, string id, string pnpClass, string status)>();
                foreach (var o in collection)
                {
                    using var mo = (ManagementObject)o;
                    var name = mo["Name"] as string ?? string.Empty;
                    var id = mo["DeviceID"] as string ?? string.Empty;
                    var pnpClass = mo["PNPClass"] as string ?? string.Empty;
                    var status = mo["Status"] as string ?? string.Empty;
                    candidates.Add((name, id, pnpClass, status));
                }

                var match = candidates.FirstOrDefault(c => IsPresent(c.status) && Matches(c.name, c.id, c.pnpClass));
                return match.name is null ? (false, null, null) : (true, match.name, match.id);
            }
            catch (Exception)
            {
                return (false, null, null);
            }
        }

        private static bool IsPresent(string status) =>
            string.IsNullOrEmpty(status) || status.Equals("OK", StringComparison.OrdinalIgnoreCase);

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
                // ignore teardown failures
            }
            _cts.Dispose();
        }
    }

    public sealed class DeviceStatusEventArgs : EventArgs
    {
        public bool Connected { get; init; }
        public string? DeviceName { get; init; }
        public string? DeviceId { get; init; }
    }
}
