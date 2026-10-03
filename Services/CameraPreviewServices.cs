using Microsoft.Extensions.Configuration;
using OpenCvSharp;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.Services
{
    /// <summary>
    /// Status stream kamera. Dipakai bersama indikator Header dan halaman Verifikasi Wajah
    /// supaya keduanya tidak pernah menampilkan kondisi berbeda.
    /// </summary>
    public enum CameraStreamState
    {
        /// <summary>Belum ada stream (belum dimulai, atau sudah dihentikan).</summary>
        Idle,

        /// <summary>Sedang membuka device.</summary>
        Opening,

        /// <summary><see cref="VideoCapture"/> benar-benar terbuka dan frame mengalir.</summary>
        Streaming,

        /// <summary>Device ada, tetapi gagal dibuka (mis. sedang dipakai aplikasi lain).</summary>
        OpenFailed,

        /// <summary>Stream berjalan lalu terputus (kamera dicabut / device hilang).</summary>
        Lost,
    }

    /// <summary>
    /// Satu frame kamera. <see cref="Pixels"/> berisi data BGRA (urutan byte B, G, R, A) siap blit
    /// ke <c>WriteableBitmap</c> dengan format BGRA8888. Pixel data di-copy oleh penerima sebelum
    /// buffer ini dikembalikan ke pool, jadi referensinya tidak boleh disimpan.
    /// </summary>
    public sealed class CameraFrameEventArgs : EventArgs
    {
        public byte[] Pixels { get; init; } = Array.Empty<byte>();
        public int Width { get; init; }
        public int Height { get; init; }
        public int Stride { get; init; }
    }

    public sealed class CameraStreamStateEventArgs : EventArgs
    {
        public CameraStreamState State { get; init; }
    }

    /// <summary>
    /// Stream kamera memakai OpenCV. Berbeda dari <see cref="CameraDeviceWatcherServices"/> yang
    /// hanya mendeteksi keberadaan device, kelas ini yang benar-benar membuka device dan
    /// mengalirkan frame.
    ///
    /// Kelas ini sengaja hanya melaporkan <b>kondisi</b> (lihat <see cref="CameraStreamState"/>)
    /// dan tidak menyusun teks status, karena teks status adalah tanggung jawab
    /// <see cref="CameraServices"/> supaya Header dan halaman Verifikasi Wajah selalu memakai
    /// kalimat yang sama.
    ///
    /// Tiga hal yang menentukan bentuk kelas ini:
    /// 1. <see cref="VideoCapture"/> hanya bisa dibuka lewat <b>index</b>, sedangkan deteksi
    ///    menghasilkan <b>nama</b> device, dan urutan index OpenCV tidak sama dengan urutan WMI.
    ///    Karena itu index dipilih dengan mencoba satu per satu (lihat <see cref="StartAsync"/>),
    ///    dengan <c>Apps:CameraIndex</c> sebagai override manual.
    /// 2. Backend <c>MSMF</c> dipakai lebih dulu daripada <c>DSHOW</c> karena kamera virtual
    ///    (Link to Windows / Phone Link) diekspos lewat Media Foundation.
    /// 3. Pembacaan frame berjalan di thread background, bukan UI thread.
    ///
    /// Device dianggap benar-benar terbuka hanya bila ada frame nyata yang keluar — bukan sekadar
    /// <see cref="VideoCapture.IsOpened"/>.
    /// </summary>
    public sealed class CameraPreviewServices : BaseService, IDisposable
    {
        // Backend yang dicoba berurutan.
        private static readonly (string Name, VideoCaptureAPIs Api)[] Backends =
        [
            ("MSMF", VideoCaptureAPIs.MSMF),
            ("DSHOW", VideoCaptureAPIs.DSHOW),
        ];

        // Batas waktu menunggu loop berhenti sebelum capture dilepas paksa. Kamera yang dicabut
        // saat Read() menggantung tidak boleh membuat Stop() ikut menggantung.
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

        // Dipakai sebagai target frame rate. Penahanan laju dilakukan dengan menunggu waktu
        // tersisa setelah Read(), bukan delay tetap, supaya frame tidak menumpuk saat kamera
        // lebih lambat dari nilai ini.
        private const double TargetFps = 20;

        private readonly SemaphoreSlim _gate = new(1, 1);

        private VideoCapture? _capture;
        private CancellationTokenSource? _cts;
        private Task? _loop;
        private int _disposed;

        private CameraStreamState _state = CameraStreamState.Idle;

        public CameraPreviewServices(IConfiguration config, SessionService session) : base(config, session)
        {
        }

        /// <summary>Frame siap dirender. Dipanggil dari thread capture, bukan UI thread.</summary>
        public event EventHandler<CameraFrameEventArgs>? FrameReady;

        /// <summary>Perubahan status stream (dipakai Header dan halaman Verifikasi Wajah).</summary>
        public event EventHandler<CameraStreamStateEventArgs>? StateChanged;

        public CameraStreamState State => _state;

        /// <summary>True hanya saat frame benar-benar mengalir.</summary>
        public bool IsStreaming => _state == CameraStreamState.Streaming;

        /// <summary>Index OpenCV yang sedang dipakai, -1 bila tidak ada.</summary>
        public int CurrentIndex { get; private set; } = -1;

        /// <summary>Backend yang berhasil dipakai ("MSMF"/"DSHOW"), null bila belum ada.</summary>
        public string? CurrentBackend { get; private set; }

        /// <summary>
        /// Index kamera dari <c>Apps:CameraIndex</c> (opsional). Bila diisi, probing dilewati dan
        /// index tersebut dipakai langsung — jalan keluar untuk mesin dengan banyak kamera, karena
        /// urutan index OpenCV tidak bisa dipetakan dari nama device.
        /// </summary>
        private int? ConfiguredIndex
        {
            get
            {
                var raw = config["Apps:CameraIndex"];
                if (string.IsNullOrWhiteSpace(raw))
                    return null;

                return int.TryParse(raw, out var index) && index >= 0 ? index : null;
            }
        }

        /// <summary>
        /// Buka device dan mulai mengalirkan frame. Aman dipanggil berulang: bila sudah streaming,
        /// panggilan ini tidak melakukan apa-apa sehingga tidak pernah ada dua stream bersamaan.
        /// </summary>
        /// <param name="candidateCount">
        /// Jumlah kamera terdeteksi dari <see cref="CameraServices.AvailableCameras"/>. Dipakai
        /// sebagai batas atas probing index.
        /// </param>
        public async Task<bool> StartAsync(int candidateCount = 0)
        {
            if (Volatile.Read(ref _disposed) == 1)
                return false;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsStreaming)
                    return true;

                SetState(CameraStreamState.Opening);

                // Membuka kamera adalah operasi blocking (bisa ratusan ms sampai beberapa detik),
                // jadi dijalankan di thread pool supaya UI thread tidak ikut menunggu.
                var opened = await Task.Run(() => OpenCapture(candidateCount)).ConfigureAwait(false);

                if (opened is null)
                {
                    SetState(CameraStreamState.OpenFailed);
                    return false;
                }

                _capture = opened.Value.Capture;
                CurrentIndex = opened.Value.Index;
                CurrentBackend = opened.Value.Backend;

                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                var capture = _capture;

                _loop = Task.Run(() => CaptureLoopAsync(capture, token), CancellationToken.None);

                SetState(CameraStreamState.Streaming);
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Hentikan stream dan lepaskan device. Aman dipanggil saat tidak streaming dan aman
        /// dipanggil berulang.
        /// </summary>
        public async Task StopAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Hentikan lalu buka ulang — dipakai tombol "Hubungkan Kamera" dan saat kamera diganti.
        /// Urutannya selalu stop → lepas device → buka ulang → start.
        /// </summary>
        public async Task<bool> RestartAsync(int candidateCount = 0)
        {
            await StopAsync().ConfigureAwait(false);
            return await StartAsync(candidateCount).ConfigureAwait(false);
        }

        /// <summary>
        /// Inti penghentian stream. Dipanggil dari <see cref="StopAsync"/> (dengan gate) maupun
        /// <see cref="Dispose"/> (tanpa menunggu Task).
        /// </summary>
        private async Task StopCoreAsync()
        {
            var cts = _cts;
            var loop = _loop;
            var capture = _capture;

            // Semua referensi dilepas lebih dulu supaya panggilan Start berikutnya tidak melihat
            // sisa stream lama, dan supaya loop yang masih berjalan tidak menahan penutupan.
            _cts = null;
            _loop = null;
            _capture = null;
            CurrentIndex = -1;
            CurrentBackend = null;

            cts?.Cancel();

            if (loop is not null)
            {
                try
                {
                    // Loop yang menggantung di Read() tidak boleh menahan penutupan aplikasi.
                    await Task.WhenAny(loop, Task.Delay(StopTimeout)).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Kegagalan loop saat berhenti tidak perlu dilaporkan.
                }
            }

            cts?.Dispose();

            // Release + Dispose selalu dijalankan, termasuk saat loop masih menggantung, supaya
            // device tidak pernah tertinggal terkunci.
            ReleaseCapture(capture);

            SetState(CameraStreamState.Idle);
        }

        private static void ReleaseCapture(VideoCapture? capture)
        {
            if (capture is null)
                return;

            try
            {
                if (capture.IsOpened())
                    capture.Release();
            }
            catch (Exception)
            {
                // ignore
            }

            try
            {
                capture.Dispose();
            }
            catch (Exception)
            {
                // ignore
            }
        }

        /// <summary>
        /// Mencari device yang benar-benar bisa mengalirkan frame. Berjalan di thread pool dan tidak
        /// menyentuh state kelas ini selain lewat nilai kembalian.
        /// </summary>
        private (VideoCapture Capture, int Index, string Backend)? OpenCapture(int candidateCount)
        {
            var configured = ConfiguredIndex;
            int[] indices;

            if (configured is not null)
            {
                // Override manual: hanya index ini yang dicoba.
                indices = [configured.Value];
            }
            else
            {
                // Batas atas = jumlah kamera terdeteksi, ditambah satu sebagai toleransi karena
                // urutan index OpenCV tidak dijamin sama dengan urutan WMI. Probing berhenti di
                // frame pertama yang berhasil, jadi percobaan berlebih hanya terjadi saat tidak
                // ada kamera yang bisa dibuka.
                var upper = Math.Max(candidateCount, 1) + 1;
                indices = new int[upper];
                for (var i = 0; i < upper; i++)
                    indices[i] = i;
            }

            foreach (var index in indices)
            {
                foreach (var (name, api) in Backends)
                {
                    if (Volatile.Read(ref _disposed) == 1)
                        return null;

                    var capture = TryOpen(index, api);
                    if (capture is not null)
                    {
                        Log($"Kamera dibuka pada index={index}, backend={name}.");
                        return (capture, index, name);
                    }
                }
            }

            Log($"Tidak ada kamera yang bisa dibuka (override={configured?.ToString() ?? "tidak ada"}, kandidat={candidateCount}).");
            return null;
        }

        /// <summary>
        /// Satu percobaan buka. Device dianggap berhasil hanya bila ada frame nyata yang keluar:
        /// <see cref="VideoCapture.IsOpened"/> saja bisa true untuk device yang sudah hilang
        /// (kamera virtual) sehingga tidak cukup untuk menyimpulkan "terhubung".
        /// </summary>
        private static VideoCapture? TryOpen(int index, VideoCaptureAPIs api)
        {
            VideoCapture? capture = null;
            try
            {
                capture = new VideoCapture(index, api);
                if (!capture.IsOpened())
                {
                    ReleaseCapture(capture);
                    return null;
                }

                // Buffer 1 frame: frame yang dibaca selalu frame terbaru, penting untuk live view.
                try
                {
                    capture.Set(VideoCaptureProperties.BufferSize, 1);
                }
                catch (Exception)
                {
                    // Tidak semua backend mendukung; abaikan.
                }

                using var probe = new Mat();
                if (!capture.Read(probe) || probe.Empty())
                {
                    ReleaseCapture(capture);
                    return null;
                }

                return capture;
            }
            catch (Exception)
            {
                ReleaseCapture(capture);
                return null;
            }
        }

        /// <summary>
        /// Loop pembacaan frame. Berjalan sampai dibatalkan atau device terputus. Semua kegagalan
        /// baca diperlakukan sebagai kamera terputus, bukan crash.
        /// </summary>
        private async Task CaptureLoopAsync(VideoCapture capture, CancellationToken token)
        {
            // Kedua Mat dipakai ulang setiap frame (alokasi native mahal). Dibuat dan hanya
            // disentuh thread ini, jadi tidak perlu penguncian.
            Mat? source = null;
            Mat? converted = null;

            try
            {
                source = new Mat();
                converted = new Mat();

                while (!token.IsCancellationRequested)
                {
                    if (!capture.Read(source) || source.Empty())
                    {
                        // Kamera dicabut / device hilang. Penghentian normal tidak dianggap
                        // terputus supaya berhenti tidak menampilkan status error.
                        if (!token.IsCancellationRequested)
                            OnStreamLost();
                        return;
                    }

                    // OpenCV menghasilkan BGR; Avalonia merender BGRA8888. Konversi dilakukan di
                    // sini supaya lapisan atas tidak perlu tahu soal urutan kanal.
                    Cv2.CvtColor(source, converted, ColorConversionCodes.BGR2BGRA);

                    var width = converted.Width;
                    var height = converted.Height;

                    // Step() = jarak antar baris dalam byte, jadi tidak perlu mengasumsikan 4 byte
                    // per piksel. Barisnya disalin per baris saat digambar karena lebar baris
                    // bitmap tujuan bisa berbeda.
                    var stride = (int)converted.Step();
                    if (width <= 0 || height <= 0 || stride <= 0)
                        continue;

                    var buffer = FrameBufferPool.Rent(stride * height);
                    try
                    {
                        // Data disalin dari memori native OpenCV ke array terkelola. Mat.Data
                        // adalah IntPtr, jadi Marshal.Copy cukup dan AllowUnsafeBlocks tidak perlu.
                        // Catatan: GetArray<T> tidak bisa dipakai di sini — OpenCvSharp menolaknya
                        // untuk Mat 4 kanal (CV_8UC4) dengan "Mat data type is not compatible".
                        Marshal.Copy(converted.Data, buffer, 0, buffer.Length);
                    }
                    catch (Exception ex)
                    {
                        FrameBufferPool.Return(buffer);
                        if (!token.IsCancellationRequested)
                        {
                            Log($"Gagal menyalin frame: {ex.GetType().Name}: {ex.Message}");
                            OnStreamLost();
                        }
                        return;
                    }

                    FrameReady?.Invoke(this, new CameraFrameEventArgs
                    {
                        Pixels = buffer,
                        Width = width,
                        Height = height,
                        Stride = stride,
                    });

                    // Penahanan laju: menunggu sisa waktu setelah Read(), bukan delay tetap pada
                    // awal loop. Dengan cara ini frame tidak menumpuk dan latensi tetap rendah;
                    // delay dibatalkan langsung saat stream dihentikan.
                    if (!token.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(1000.0 / TargetFps), token)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Penghentian normal.
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Log($"Loop kamera berhenti karena error: {ex.Message}");
                    OnStreamLost();
                }
            }
            finally
            {
                source?.Dispose();
                converted?.Dispose();
            }
        }

        /// <summary>
        /// Device terputus saat streaming. Capture tidak dilepas di sini; pelepasan tetap dilakukan
        /// <see cref="StopAsync"/> supaya hanya ada satu jalur yang menyentuh resource.
        /// </summary>
        private void OnStreamLost() => SetState(CameraStreamState.Lost);

        private void SetState(CameraStreamState state)
        {
            _state = state;
            StateChanged?.Invoke(this, new CameraStreamStateEventArgs { State = state });
        }

        private static void Log(string message) =>
            System.Diagnostics.Trace.WriteLine($"[CameraPreview] {message}");

        /// <summary>
        /// Pool buffer frame sederhana untuk mengurangi alokasi besar (1280x720 BGRA ~3,5 MB) pada
        /// setiap frame. Buffer dikembalikan penerima lewat <see cref="FrameBufferPool.Return"/>
        /// setelah di-copy.
        /// </summary>
        private static class FrameBufferPool
        {
            private const int MaxPooled = 4;

            private static readonly System.Collections.Concurrent.ConcurrentBag<byte[]> Pool = new();

            public static byte[] Rent(int size)
            {
                if (Pool.TryTake(out var buffer) && buffer.Length == size)
                    return buffer;

                return new byte[size];
            }

            public static void Return(byte[] buffer)
            {
                if (buffer.Length == 0)
                    return;

                // Batasi jumlah buffer tersimpan supaya memori tidak tumbuh tanpa batas.
                if (Pool.Count < MaxPooled)
                    Pool.Add(buffer);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            // Wait() disengaja: Dispose dipanggil dari jalur penutupan aplikasi yang tidak bisa
            // menunggu Task. StopCoreAsync selalu melepaskan device walau loop menggantung.
            _gate.Wait();
            try
            {
                StopCoreAsync().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // Teardown tidak boleh melempar saat aplikasi ditutup.
            }
            finally
            {
                _gate.Release();
                _gate.Dispose();
            }
        }
    }
}
