using System;
using System.Threading;
using DPUruNet;

namespace AvaloniaFingerprint.Services;

/// <summary>
/// Payload raised once a finger has been captured and turned into a result.
/// </summary>
public sealed class FingerprintCapturedEventArgs : EventArgs
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Finger { get; init; } = string.Empty;
    public string CardNumber { get; init; } = string.Empty;
}

/// <summary>
/// Thin wrapper over the DigitalPersona U.are.U SDK (<c>DPUruNet.dll</c>) that keeps the
/// reader in standby and raises <see cref="FingerprintCaptured"/> whenever a finger is
/// detected on its own.
///
/// The capture is event driven: <see cref="Reader.CaptureAsync"/> arms the device and the
/// SDK calls back on <see cref="Reader.On_Captured"/>. There is no polling loop and no
/// blocking wait, so the UI thread is never held up.
///
/// Callbacks arrive on an SDK background thread. This class therefore never touches UI
/// objects; subscribers are responsible for marshalling onto the UI thread.
/// </summary>
public sealed class FingerprintReaderService : IDisposable
{
    /// <summary>Fallback resolution when the reader reports none.</summary>
    private const int DefaultCaptureResolution = 500;

    private readonly object _sync = new();

    private Reader? _reader;
    private bool _capturedSubscribed;
    private bool _isProcessingFingerprint;
    private bool _disposed;

    /// <summary>
    /// Resolution actually reported by <see cref="Reader.Capabilities"/>.
    /// </summary>
    private int _captureResolution = DefaultCaptureResolution;

    /// <summary>
    /// True while an asynchronous capture is outstanding. Guards against arming a second
    /// capture on top of a pending one: the SDK would answer DP_DEVICE_BUSY ("cannot
    /// capture, another operation is in progress") and the finger would never be seen.
    /// </summary>
    private bool _captureArmed;

    /// <summary>
    /// Staged diagnostics. Writes to stderr only, so the UI stays untouched.
    /// </summary>
    private static void Log(string message) =>
        Console.Error.WriteLine($"[FP] {DateTime.Now:HH:mm:ss.fff} {message}");

    /// <summary>Raised on the SDK thread after a capture completes successfully.</summary>
    public event EventHandler<FingerprintCapturedEventArgs>? FingerprintCaptured;

    /// <summary>Raised on the SDK thread whenever the human readable status changes.</summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>True once a reader was found and opened successfully.</summary>
    public bool IsDeviceAvailable { get; private set; }

    /// <summary>Friendly name of the connected reader, when one is available.</summary>
    public string DeviceName { get; private set; } = string.Empty;

    /// <summary>
    /// The value currently being searched for. Used only to echo the searched number back
    /// inside the dummy result; it is never sent anywhere.
    /// </summary>
    public string CurrentInput { get; set; } = string.Empty;

    /// <summary>
    /// Enumerates readers, connects to the first one that can actually stream and leaves it
    /// armed in standby. All SDK failures are converted into status messages, never
    /// exceptions.
    /// </summary>
    public void Start()
    {
        if (_disposed) return;

        // Menu Utama calls Start() on every keystroke. Arming again while a capture is
        // already pending makes the SDK answer DP_DEVICE_BUSY and no finger is ever
        // reported, so the device must be initialised once and then left in standby.
        lock (_sync)
        {
            if (_captureArmed) return;
        }

        try
        {
            if (IsDeviceAvailable)
            {
                Arm();
                return;
            }

            Log("Initialize");
            var readers = ReaderCollection.GetReaders();

            if (readers is null || readers.Count == 0)
            {
                IsDeviceAvailable = false;
                DeviceName = string.Empty;
                Log("Device detected: none");
                Report("Fingerprint device tidak tersedia");
                return;
            }

            Log($"Device detected: {readers.Count} reader(s)");

            // A successful Open() does not mean the handle can stream: on this hardware the
            // first reader (a WBF pseudo device) opens with DP_SUCCESS but CaptureAsync
            // answers DP_INVALID_DEVICE, so a finger would never be seen. Probe each reader
            // by really arming it and keep the first one that works.
            for (var i = 0; i < readers.Count; i++)
            {
                var candidate = readers[i];

                var opened = candidate.Open(Constants.CapturePriority.DP_PRIORITY_COOPERATIVE);
                if (opened != Constants.ResultCode.DP_SUCCESS)
                {
                    Log($"Reader[{i}] open failed: {opened}");
                    candidate.Dispose();
                    continue;
                }

                // The SDK expects the reader's own resolution; it is not always 500.
                var resolutions = candidate.Capabilities?.Resolutions;
                var resolution = DefaultCaptureResolution;
                if (resolutions is not null)
                {
                    foreach (var r in resolutions)
                    {
                        if (r != 0) { resolution = r; break; }
                    }
                }

                var armed = candidate.CaptureAsync(Constants.Formats.Fid.ANSI, Constants.CaptureProcessing.DP_IMG_PROC_DEFAULT, resolution);
                if (armed != Constants.ResultCode.DP_SUCCESS)
                {
                    Log($"Reader[{i}] arm failed: {armed}");
                    candidate.CancelCapture();
                    candidate.Dispose();
                    continue;
                }

                _reader = candidate;
                _captureResolution = resolution;
                _reader.On_Captured += OnCaptured;
                _capturedSubscribed = true;

                lock (_sync)
                {
                    _captureArmed = true;
                }

                IsDeviceAvailable = true;
                DeviceName = candidate.Description?.Name ?? "Fingerprint Reader";

                Log($"Device connected: {DeviceName}");
                Log("Callback registered");
                Log($"Capture started (res={resolution}) -> {armed}");
                Log("Waiting for finger");
                Report("Menunggu fingerprint...");
                return;
            }

            IsDeviceAvailable = false;
            DeviceName = string.Empty;
            Log("Device detected: no usable reader");
            Report("Fingerprint device tidak tersedia");
        }
        catch (Exception ex)
        {
            IsDeviceAvailable = false;
            Log($"Initialize failed: {ex.Message}");
            Report($"Fingerprint device gagal diinisialisasi: {ex.Message}");
        }
    }

    /// <summary>
    /// Puts the reader back into standby so the next finger is detected automatically.
    /// </summary>
    private void Arm()
    {
        var reader = _reader;
        if (reader is null) return;

        lock (_sync)
        {
            // Never stack a second capture on top of a pending one.
            if (_captureArmed) return;
            _captureArmed = true;
        }

        try
        {
            // The SDK requires the reader to be checked before every capture. A reader that
            // needs calibration accepts an arm but then never delivers an image, which is
            // exactly the "device is active but the finger is never seen" symptom.
            var status = reader.GetStatus();
            var state = reader.Status.Status;
            var fingerDetected = reader.Status.FingerDetected;
            Log($"GetStatus -> {status}, Status={state}, FingerDetected={fingerDetected}");

            if (status != Constants.ResultCode.DP_SUCCESS)
            {
                Log($"Status check failed: {status}");
            }
            else if (state == Constants.ReaderStatuses.DP_STATUS_NEED_CALIBRATION)
            {
                Log("Reader needs calibration, calibrating...");
                var calibrated = reader.Calibrate();
                Log($"Calibrate -> {calibrated}");
            }
            else if (state == Constants.ReaderStatuses.DP_STATUS_BUSY)
            {
                Log("Reader busy, waiting before arming...");
                Thread.Sleep(50);
            }
            else if (state != Constants.ReaderStatuses.DP_STATUS_READY)
            {
                Log($"Reader not ready: {state}");
                lock (_sync) { _captureArmed = false; }
                return;
            }

            // Handlers must be attached before the capture starts, as in the SDK sample.
            if (!_capturedSubscribed)
            {
                reader.On_Captured += OnCaptured;
                _capturedSubscribed = true;
                Log("Callback registered");
            }

            var result = reader.CaptureAsync(Constants.Formats.Fid.ANSI, Constants.CaptureProcessing.DP_IMG_PROC_DEFAULT, _captureResolution);
            Log($"Capture started (res={_captureResolution}) -> {result}");

            if (result == Constants.ResultCode.DP_SUCCESS)
            {
                Log("Waiting for finger");
            }
            else
            {
                // A capture already in flight is the desired state, so only a real failure is
                // reported.
                if (result != Constants.ResultCode.DP_DEVICE_BUSY)
                {
                    Report($"Menunggu fingerprint... ({result})");
                }

                lock (_sync) { _captureArmed = false; }
            }
        }
        catch (Exception ex)
        {
            Log($"Arm failed: {ex.Message}");
            Report($"Fingerprint reader error: {ex.Message}");
            lock (_sync) { _captureArmed = false; }
        }
    }

    /// <summary>
    /// SDK callback. Runs on an SDK background thread, so it only builds the result and
    /// raises events; it never touches controls.
    /// </summary>
    private void OnCaptured(CaptureResult result)
    {
        // A single finger can produce more than one callback. Ignore everything that
        // arrives while the previous one is still being handled.
        lock (_sync)
        {
            if (_isProcessingFingerprint) return;
            _isProcessingFingerprint = true;
        }

        try
        {
            if (_disposed) return;

            Log("Finger event received");

            var code = result?.ResultCode ?? Constants.ResultCode.DP_FAILURE;

            if (code != Constants.ResultCode.DP_SUCCESS)
            {
                // No finger present is the normal idle state, not an error.
                if (result?.Quality == Constants.CaptureQuality.DP_QUALITY_NO_FINGER)
                {
                    Log($"Capture idle: code={code}, quality={result?.Quality}");
                    Report("Menunggu fingerprint...");
                }
                else
                {
                    Log($"Capture failed: code={code}, quality={result?.Quality}");
                    Report("Finger gagal dibaca, silakan coba lagi");
                }

                return;
            }

            Log($"Capture success, quality={result?.Quality}");
            Report("Mendeteksi fingerprint...");

            // Dummy result for this development stage. Nothing is sent to Tarakan
            // Bridging and the captured template is deliberately never persisted.
            var payload = new FingerprintCapturedEventArgs
            {
                Success = true,
                Message = "Fingerprint berhasil terdeteksi",
                Finger = "Dummy Fingerprint",
                CardNumber = CurrentInput
            };

            FingerprintCaptured?.Invoke(this, payload);

            Report("Fingerprint terdeteksi");
        }
        catch (Exception ex)
        {
            Log($"Callback error: {ex.Message}");
            Report($"Finger gagal dibaca: {ex.Message}");
        }
        finally
        {
            lock (_sync)
            {
                _isProcessingFingerprint = false;

                // The pending capture has completed, so standby may now be restored.
                _captureArmed = false;
            }

            // Back to standby for the next finger, unless we are shutting down.
            if (!_disposed) Arm();
        }
    }

    private void Report(string status)
    {
        try
        {
            StatusChanged?.Invoke(this, status);
        }
        catch
        {
            // A subscriber fault must never escape into the SDK callback.
        }
    }

    /// <summary>Releases the reader. Safe to call more than once.</summary>
    public void Stop()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_reader is not null)
            {
                if (_capturedSubscribed)
                {
                    _reader.On_Captured -= OnCaptured;
                    _capturedSubscribed = false;
                }

                _reader.CancelCapture();
                _reader.Dispose();
                _reader = null;
            }
        }
        catch
        {
            // Releasing the device is best effort.
        }
        finally
        {
            lock (_sync)
            {
                _captureArmed = false;
            }

            IsDeviceAvailable = false;
        }
    }

    public void Dispose() => Stop();
}
