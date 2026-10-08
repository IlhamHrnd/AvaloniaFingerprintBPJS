using Microsoft.Extensions.Configuration;
using OpenCvSharp;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.Services
{
    public enum CameraStreamState
    {
        Idle,

        Opening,

        Streaming,

        OpenFailed,

        Lost,
    }

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

    public sealed class CameraPreviewServices : BaseService, IDisposable
    {
        private static readonly (string Name, VideoCaptureAPIs Api)[] Backends =
        [
            ("MSMF", VideoCaptureAPIs.MSMF),
            ("DSHOW", VideoCaptureAPIs.DSHOW),
        ];

        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

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

        public event EventHandler<CameraFrameEventArgs>? FrameReady;

        public event EventHandler<CameraStreamStateEventArgs>? StateChanged;

        public CameraStreamState State => _state;

        public bool IsStreaming => _state == CameraStreamState.Streaming;

        public int CurrentIndex { get; private set; } = -1;

        public string? CurrentBackend { get; private set; }

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

        public async Task<bool> RestartAsync(int candidateCount = 0)
        {
            await StopAsync().ConfigureAwait(false);
            return await StartAsync(candidateCount).ConfigureAwait(false);
        }

        private async Task StopCoreAsync()
        {
            var cts = _cts;
            var loop = _loop;
            var capture = _capture;

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
                    await Task.WhenAny(loop, Task.Delay(StopTimeout)).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }

            cts?.Dispose();

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
            }

            try
            {
                capture.Dispose();
            }
            catch (Exception)
            {
            }
        }

        private (VideoCapture Capture, int Index, string Backend)? OpenCapture(int candidateCount)
        {
            var configured = ConfiguredIndex;
            int[] indices;

            if (configured is not null)
            {
                indices = [configured.Value];
            }
            else
            {
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

                try
                {
                    capture.Set(VideoCaptureProperties.BufferSize, 1);
                }
                catch (Exception)
                {
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

        private async Task CaptureLoopAsync(VideoCapture capture, CancellationToken token)
        {
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
                        if (!token.IsCancellationRequested)
                            OnStreamLost();
                        return;
                    }

                    Cv2.CvtColor(source, converted, ColorConversionCodes.BGR2BGRA);

                    var width = converted.Width;
                    var height = converted.Height;

                    var stride = (int)converted.Step();
                    if (width <= 0 || height <= 0 || stride <= 0)
                        continue;

                    var buffer = FrameBufferPool.Rent(stride * height);
                    try
                    {
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

                    if (!token.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(1000.0 / TargetFps), token)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
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

        private void OnStreamLost() => SetState(CameraStreamState.Lost);

        private void SetState(CameraStreamState state)
        {
            _state = state;
            StateChanged?.Invoke(this, new CameraStreamStateEventArgs { State = state });
        }

        private static void Log(string message) =>
            System.Diagnostics.Trace.WriteLine($"[CameraPreview] {message}");

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

                if (Pool.Count < MaxPooled)
                    Pool.Add(buffer);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            _gate.Wait();
            try
            {
                StopCoreAsync().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            }
            finally
            {
                _gate.Release();
                _gate.Dispose();
            }
        }
    }
}
