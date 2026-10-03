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
        private readonly CameraPreviewServices preview;

        public CameraServices(IConfiguration config, SessionService session) : base(config, session)
        {
            watcher = new CameraDeviceWatcherServices(config, session);
            watcher.StatusChanged += (_, e) => DeviceStatusChanged?.Invoke(this, e);

            preview = new CameraPreviewServices(config, session);

            // Status stream (terbuka / gagal dibuka / terputus) dilaporkan lewat event yang sama
            // dengan status deteksi supaya Header cukup berlangganan satu event, dan supaya
            // keduanya tidak pernah menyimpang.
            preview.StateChanged += (_, _) => RaiseStatus();
            preview.FrameReady += (_, e) => FrameReady?.Invoke(this, e);
        }

        // Teks status dipakai bersama oleh indikator di Header dan halaman Verifikasi Wajah,
        // sehingga keduanya tidak mungkin menampilkan status yang berbeda.
        public const string SearchingStatus = "Mencari kamera...";
        public const string ConnectedStatus = "Kamera terhubung";
        public const string ConnectedDetail = "Kamera siap digunakan";
        public const string DisconnectedStatus = "Kamera terputus";
        public const string DisconnectedDetail = "Hubungkan kamera";

        /// <summary>Device ada, tetapi stream tidak bisa dibuka.</summary>
        public const string OpenFailedStatus = "Kamera gagal dibuka";

        /// <summary>Detail saat device terdeteksi namun gagal dibuka.</summary>
        public const string DetailOpenFailed = "Kamera terdeteksi tetapi gagal dibuka";

        /// <summary>Detail saat stream yang sedang berjalan terputus.</summary>
        public const string LostDetail = "Kamera terputus saat menampilkan gambar";

        public event EventHandler<DeviceStatusEventArgs>? DeviceStatusChanged;

        /// <summary>Frame kamera siap ditampilkan. Dipanggil dari thread capture, bukan UI thread.</summary>
        public event EventHandler<CameraFrameEventArgs>? FrameReady;

        /// <summary>Deteksi device: benar bila Windows mengenali kamera (belum tentu bisa dibuka).</summary>
        public bool IsDeviceConnected => watcher.IsConnected;

        /// <summary>
        /// Benar hanya bila stream kamera benar-benar terbuka dan frame mengalir. Inilah yang
        /// dipakai indikator Header: device yang terdeteksi tetapi tidak bisa dibuka tetap
        /// dianggap belum terhubung.
        /// </summary>
        public bool IsConnected => preview.IsStreaming;

        /// <summary>Status stream saat ini (Idle/Opening/Streaming/OpenFailed/Lost).</summary>
        public CameraStreamState StreamState => preview.State;

        /// <summary>Keterangan singkat device yang sedang dipakai, mis. "index 0 / MSMF".</summary>
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
            // Status diturunkan dari hasil stream, bukan dari deteksi device: kamera yang dikenali
            // Windows tetapi tidak bisa dibuka harus terlihat sebagai gagal dibuka, bukan terhubung.
            switch (preview.State)
            {
                case CameraStreamState.Streaming:
                    // Index OpenCV disertakan karena hanya index itu yang bisa dipakai VideoCapture;
                    // berguna saat sebuah mesin punya lebih dari satu kamera.
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
                        return DisconnectedDetail;

                    return string.IsNullOrWhiteSpace(DeviceName) ? ConnectedDetail : DeviceName!;
            }
        }

        /// <summary>
        /// Teks status ringkas untuk indikator Header dan halaman Verifikasi Wajah. Selalu
        /// diturunkan dari <see cref="CameraPreviewServices.State"/> supaya indikator hanya hijau
        /// saat stream benar-benar terbuka.
        /// </summary>
        public string ResolveStatus() => preview.State switch
        {
            CameraStreamState.Streaming => ConnectedStatus,
            CameraStreamState.Opening => SearchingStatus,
            CameraStreamState.OpenFailed => OpenFailedStatus,
            _ => DisconnectedStatus,
        };

        public void Start() => watcher.Start();

        public Task RefreshDeviceAsync() => watcher.RefreshAsync();

        /// <summary>
        /// Membuka stream kamera memakai deteksi yang sudah ada. Aman dipanggil berkali-kali:
        /// <see cref="CameraPreviewServices"/> menolak start saat sudah streaming sehingga tidak
        /// pernah ada dua stream bersamaan.
        /// </summary>
        public Task<bool> StartPreviewAsync() =>
            IsDeviceConnected ? preview.StartAsync(AvailableCameras.Count) : Task.FromResult(false);

        /// <summary>Menutup stream dan melepaskan device.</summary>
        public Task StopPreviewAsync() => preview.StopAsync();

        /// <summary>
        /// Hasil satu siklus scan kamera. Dipakai halaman Verifikasi Wajah untuk memberi tahu
        /// pengguna apa yang terjadi setelah tombol "Hubungkan Kamera" ditekan.
        /// </summary>
        public readonly record struct CameraScanResult(bool Connected, string Detail);

        /// <summary>
        /// Scan ulang dan buka kamera memakai mekanisme deteksi yang sama dengan
        /// <see cref="RefreshDeviceAsync"/> (pencocokan <c>cameraKeywords</c>), kemudian laporkan
        /// hasilnya. Dipakai tombol "Hubungkan Kamera" — tidak membuat deteksi kedua.
        ///
        /// Urutannya selalu stop → lepas device → buka ulang → start, sehingga menekan tombol
        /// berulang tidak pernah menghasilkan dua stream.
        /// </summary>
        public async Task<CameraScanResult> ReconnectAsync()
        {
            await RefreshDeviceAsync();

            if (!IsDeviceConnected)
            {
                // Pastikan tidak ada stream lama yang tertinggal saat device sudah tidak ada.
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
