using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.Services
{
    /// <summary>
    /// Sisi UI dari deteksi kamera. Bentuknya meniru <see cref="FingerprintServices"/> supaya
    /// lapisan ViewModel memakai pola yang sama untuk kedua device (status + event + Start).
    /// Hanya memeriksa keberadaan device; belum ada stream, capture, atau API.
    /// </summary>
    public sealed class CameraServices : BaseService, IDisposable
    {
        private readonly CameraDeviceWatcherServices watcher;

        public CameraServices(IConfiguration config, SessionService session) : base(config, session)
        {
            watcher = new CameraDeviceWatcherServices(config, session);
            watcher.StatusChanged += (_, e) => DeviceStatusChanged?.Invoke(this, e);
        }

        // Teks status dipakai bersama oleh indikator di Header dan halaman Verifikasi Wajah,
        // sehingga keduanya tidak mungkin menampilkan status yang berbeda.
        public const string SearchingStatus = "Mencari kamera...";
        public const string ConnectedStatus = "Kamera terhubung";
        public const string ConnectedDetail = "Kamera siap digunakan";
        public const string DisconnectedStatus = "Kamera terputus";
        public const string DisconnectedDetail = "Hubungkan kamera";

        public event EventHandler<DeviceStatusEventArgs>? DeviceStatusChanged;
        public bool IsDeviceConnected => watcher.IsConnected;
        public string? DeviceName => watcher.DeviceName;

        public void Start() => watcher.Start();

        public Task RefreshDeviceAsync() => watcher.RefreshAsync();

        public void Dispose() => watcher.Dispose();
    }
}
