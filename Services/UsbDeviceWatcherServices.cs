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
    public class UsbDeviceWatcherServices(
        IConfiguration config,
        SessionService session,
        FingerprintSdkProbe? sdkProbe = null) : BaseService(config, session), IDisposable
    {
        private const string DeviceQuery = "SELECT Name, DeviceID, PNPClass, Status, Present, ConfigManagerErrorCode FROM Win32_PnPEntity " +
            "WHERE PNPClass = 'Biometric' OR DeviceID LIKE '%VID_05BA%' " +
            "OR Name LIKE '%Fingerprint%' OR Name LIKE '%U.are.U%' OR Name LIKE '%Biometric%'";
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

        private const int NoReaderMissThreshold = 2;

        public FingerprintDeviceState Current { get; private set; } = FingerprintDeviceState.None;

        public bool IsConnected => Current.IsConnected;

        public string? DeviceName => Current.DeviceName;

        public string? DeviceId => Current.DeviceId;

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

                if (result is not null)
                    Publish(ApplyNoReaderDebounce(result));
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Interlocked.Exchange(ref _scanning, 0);
            }
        }

        private void Publish(FingerprintDeviceState state)
        {
            FingerprintDeviceState previous;

            lock (_stateLock)
            {
                previous = Current;
                if (state.Equals(previous))
                    return;

                Current = state;
            }

            Trace.WriteLine($"[UsbDeviceWatcher] {previous.State} -> {state.State}. Connected={state.IsConnected}, Name={state.DeviceName ?? "-"}, Id={state.DeviceId ?? "-"}, Detail={state.Detail ?? "-"}");

            StatusChanged?.Invoke(this, state);
        }

        private FingerprintDeviceState ApplyNoReaderDebounce(FingerprintDeviceState result)
        {
            if (result.State != FingerprintReaderState.NoDevice)
            {
                Interlocked.Exchange(ref _noReaderMisses, 0);
                return result;
            }

            var misses = Interlocked.Increment(ref _noReaderMisses);

            if (Current.IsConnected && misses < NoReaderMissThreshold)
            {
                Trace.WriteLine($"[UsbDeviceWatcher] SDK tidak melihat reader (konfirmasi {misses}/{NoReaderMissThreshold}); status terhubung dipertahankan sementara.");
                return Current;
            }

            return result;
        }

        private FingerprintDeviceState? Probe()
        {
            if (_sdkProbe is not null)
            {
                try
                {
                    var sdk = _sdkProbe(Publish);

                    if (sdk is null)
                    {
                        Trace.WriteLine("[UsbDeviceWatcher] Probe SDK menunda (reader sedang dipakai); state terakhir dipertahankan.");
                        return null;
                    }

                    if (sdk.State != FingerprintReaderState.NoDevice)
                        return sdk;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[UsbDeviceWatcher] Probe SDK gagal: {ex.GetType().Name}: {ex.Message}");
                }
            }

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
