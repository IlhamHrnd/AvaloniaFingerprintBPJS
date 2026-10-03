using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Nama semua kamera yang terdeteksi (cocok salah satu <c>cameraKeywords</c>) pada scan
        /// terakhir. Dipakai halaman Verifikasi Wajah untuk menampilkan daftar kamera sehingga
        /// pengguna/developer bisa memastikan device mana yang dikenali Windows.
        /// </summary>
        public IReadOnlyList<string> AvailableCameras => watcher.AvailableCameras;

        /// <summary>
        /// Detail status yang dipakai indikator Header sekaligus halaman Verifikasi Wajah, sehingga
        /// keduanya selalu menampilkan teks yang sama. Saat kamera terhubung, nama device selalu
        /// ditampilkan supaya pengguna bisa memastikan kamera mana yang dipakai — penting untuk
        /// kamera virtual seperti Windows Virtual Camera (Link to Windows / Phone Link).
        /// </summary>
        public string ResolveDetail()
        {
            if (!IsDeviceConnected)
                return DisconnectedDetail;

            return string.IsNullOrWhiteSpace(DeviceName) ? ConnectedDetail : DeviceName!;
        }

        public void Start() => watcher.Start();

        public Task RefreshDeviceAsync() => watcher.RefreshAsync();

        /// <summary>
        /// Hasil satu siklus scan kamera. Dipakai halaman Verifikasi Wajah untuk memberi tahu
        /// pengguna apa yang terjadi setelah tombol "Hubungkan Kamera" ditekan.
        /// </summary>
        public readonly record struct CameraScanResult(bool Connected, string Detail);

        /// <summary>
        /// Scan ulang dan pilih kamera memakai mekanisme deteksi yang sama dengan
        /// <see cref="RefreshDeviceAsync"/> (pencocokan <c>cameraKeywords</c>), kemudian laporkan
        /// hasilnya. Dipakai tombol "Hubungkan Kamera" — tidak membuat deteksi kedua.
        /// Aman dipanggil saat kamera sudah terhubung: <see cref="CameraDeviceWatcherServices"/>
        /// memakai penjaga <c>_scanning</c> sehingga scan ganda tidak menumpuk.
        /// </summary>
        public async Task<CameraScanResult> ReconnectAsync()
        {
            await RefreshDeviceAsync();
            return new CameraScanResult(IsDeviceConnected, ResolveDetail());
        }

        public void Dispose() => watcher.Dispose();
    }
}
