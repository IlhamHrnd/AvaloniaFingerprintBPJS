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

        /// <summary>Cara kamera aktif dipilih (keyword atau fallback) — dipakai Header untuk detail.</summary>
        public CameraMatchType MatchType => watcher.MatchType;

        /// <summary>True jika kamera aktif dipakai sebagai cadangan, bukan hasil pencocokan keyword.</summary>
        public bool IsFallback => watcher.MatchType == CameraMatchType.Fallback;

        /// <summary>
        /// Detail status yang dipakai indikator Header sekaligus halaman Verifikasi Wajah, sehingga
        /// keduanya selalu menampilkan teks yang sama. Kamera hasil keyword menampilkan pesan umum,
        /// sedangkan kamera fallback menampilkan nama device-nya (mis. "Logitech C920").
        /// </summary>
        public string ResolveDetail()
        {
            if (!IsDeviceConnected)
                return DisconnectedDetail;

            return IsFallback && !string.IsNullOrWhiteSpace(DeviceName)
                ? DeviceName!
                : ConnectedDetail;
        }

        public void Start() => watcher.Start();

        public Task RefreshDeviceAsync() => watcher.RefreshAsync();

        public void Dispose() => watcher.Dispose();
    }
}
