using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.Services
{
    public sealed class CameraServices : BaseService, IDisposable
    {
        private readonly CameraDeviceWatcherServices watcher;
        private readonly CameraPreviewServices preview;

        public CameraServices(IConfiguration config, SessionService session) : base(config, session)
        {
            watcher = new CameraDeviceWatcherServices(config, session);
            watcher.StatusChanged += (_, e) => DeviceStatusChanged?.Invoke(this, e);

            preview = new CameraPreviewServices(config, session);

            preview.StateChanged += (_, _) => RaiseStatus();
            preview.FrameReady += (_, e) => FrameReady?.Invoke(this, e);
        }

        public const string SearchingStatus = "Mencari kamera...";
        public const string ConnectedStatus = "Kamera terhubung";
        public const string ConnectedDetail = "Kamera siap digunakan";
        public const string DisconnectedStatus = "Kamera terputus";
        public const string DisconnectedDetail = "Hubungkan kamera";

        public const string OpenFailedStatus = "Kamera gagal dibuka";

        public const string DetailOpenFailed =
            "Kamera terdeteksi tetapi tidak dapat digunakan. " +
            "Pastikan kamera tidak sedang digunakan oleh aplikasi lain.";

        public const string DetailDeviceNotFound =
            "Kamera internal laptop tidak ditemukan. " +
            "Pastikan kamera tersedia dan tidak sedang digunakan oleh aplikasi lain.";

        public const string LostDetail = "Kamera terputus saat menampilkan gambar";

        public event EventHandler<DeviceStatusEventArgs>? DeviceStatusChanged;

        public event EventHandler<CameraFrameEventArgs>? FrameReady;

        public bool IsDeviceConnected => watcher.IsConnected;

        public bool IsConnected => preview.IsStreaming;

        public CameraStreamState StreamState => preview.State;

        private string StreamSuffix =>
            preview.CurrentIndex >= 0
                ? $"index {preview.CurrentIndex} / {preview.CurrentBackend ?? "backend tidak diketahui"}"
                : "index tidak diketahui";

        public string? DeviceName => watcher.DeviceName;

        private void RaiseStatus() =>
            DeviceStatusChanged?.Invoke(this, new DeviceStatusEventArgs
            {
                Connected = IsConnected,
                DeviceName = DeviceName,
                DeviceId = null,
            });

        public IReadOnlyList<string> AvailableCameras => watcher.AvailableCameras;

        public string ResolveDetail()
        {
            switch (preview.State)
            {
                case CameraStreamState.Streaming:
                    return string.IsNullOrWhiteSpace(DeviceName)
                        ? $"Kamera aktif ({StreamSuffix})"
                        : $"{DeviceName} ({StreamSuffix})";

                case CameraStreamState.Opening:
                    return "Membuka kamera...";

                case CameraStreamState.OpenFailed:
                    return DetailOpenFailed;

                case CameraStreamState.Lost:
                    return LostDetail;

                default:
                    if (!IsDeviceConnected)
                        return DetailDeviceNotFound;

                    return string.IsNullOrWhiteSpace(DeviceName) ? ConnectedDetail : DeviceName!;
            }
        }

        public string ResolveStatus() => preview.State switch
        {
            CameraStreamState.Streaming => ConnectedStatus,
            CameraStreamState.Opening => SearchingStatus,
            CameraStreamState.OpenFailed => OpenFailedStatus,
            _ => DisconnectedStatus,
        };

        public void Start() => watcher.Start();

        public Task RefreshDeviceAsync() => watcher.RefreshAsync();

        public Task<bool> StartPreviewAsync() =>
            IsDeviceConnected ? preview.StartAsync(AvailableCameras.Count) : Task.FromResult(false);

        public Task StopPreviewAsync() => preview.StopAsync();

        public readonly record struct CameraScanResult(bool Connected, string Detail);

        public async Task<CameraScanResult> ReconnectAsync()
        {
            await RefreshDeviceAsync();

            if (!IsDeviceConnected)
            {
                await StopPreviewAsync();
                return new CameraScanResult(false, ResolveDetail());
            }

            var streamed = await preview.RestartAsync(AvailableCameras.Count);
            return new CameraScanResult(streamed, ResolveDetail());
        }

        public void Dispose()
        {
            preview.Dispose();
            watcher.Dispose();
        }
    }
}
