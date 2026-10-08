using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaFingerprint.Converters;
using AvaloniaFingerprint.Exceptions;
using AvaloniaFingerprint.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Telerik.Reporting;

namespace AvaloniaFingerprint.ViewModels;

public sealed partial class VerifikasiViewModel : ViewModelBase
{
    private const int NikLength = 16;

    private const int BpjsLength = 13;

    private static readonly TimeSpan VerifyTimeoutMs = TimeSpan.FromSeconds(15);

    private readonly AppServices _services;
    private WriteableBitmap? _frameA;
    private WriteableBitmap? _frameB;
    private bool _useSecondBuffer;
    private readonly object _frameLock = new();
    private byte[] _staging = Array.Empty<byte>();
    private int _stagingWidth;
    private int _stagingHeight;
    private int _stagingStride;
    private int _renderPending;

    public VerifikasiViewModel(AppServices services)
    {
        _services = services;
        IsLoggedIn = services.Session.IsLoggedIn;
        DeviceConnected = services.Fingerprint.IsDeviceConnected;

        _services.Camera.FrameReady += OnFrameReady;

        OnCameraStatusChanged(
            _services.Camera.IsConnected,
            _services.Camera.ResolveStatus(),
            _services.Camera.ResolveDetail());
    }

    public event Action? CameraUsageChanged;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCameraCommand))]
    [NotifyPropertyChangedFor(nameof(ResultPlaceholderLabel))]
    private bool _isFingerprintMode = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCameraCommand))]
    [NotifyPropertyChangedFor(nameof(ResultPlaceholderLabel))]
    private bool _isWajahMode;

    partial void OnIsFingerprintModeChanged(bool value)
    {
        if (!value)
            return;

        if (IsWajahMode) IsWajahMode = false;
        EnterFingerprintMode();
    }

    partial void OnIsWajahModeChanged(bool value)
    {
        if (!value)
            return;

        if (IsFingerprintMode) IsFingerprintMode = false;
        EnterWajahMode();
    }

    private void EnterFingerprintMode()
    {
        FaceEncoding = null;
        CapturedPhoto = null;
        UploadedPhoto = null;
        _uploadedPhotoBytes = null;
        IsShowingUploadedPhoto = false;
        ClearFrames();

        ResetResult();
        SetStatus(string.Empty, false);

        CameraUsageChanged?.Invoke();
    }

    private void EnterWajahMode()
    {
        FingerImage = null;
        IsScanning = false;
        ScanStatus = string.Empty;

        ResetResult();
        SetStatus(string.Empty, false);

        CameraUsageChanged?.Invoke();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private bool _isLoggedIn;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private bool _deviceConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCameraCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _scanStatus = string.Empty;
    [ObservableProperty] private bool _isScanning;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasStatus;
    [ObservableProperty] private bool _statusIsError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private string _identityNumber = string.Empty;

    partial void OnIdentityNumberChanged(string value)
    {
        var cleaned = new string(value.Where(char.IsAsciiDigit).Take(NikLength).ToArray());
        if (cleaned != value)
            IdentityNumber = cleaned;
    }

    private string ResolvedJnsFilter => IdentityNumber.Length switch
    {
        NikLength => "nik",
        BpjsLength => "psnoka",
        _ => string.Empty
    };

    private bool IsIdentityValid => ResolvedJnsFilter.Length > 0;

    [ObservableProperty]
    private string _cameraTitle = "Live Camera View";

    [ObservableProperty]
    private string _cameraStatus = "Kamera belum diaktifkan";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImage))]
    private IImage? _cameraFrame;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewVisible))]
    [NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]
    private bool _isPreviewActive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCameraCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private bool _isConnecting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private bool _isVerifyingFace;

    [ObservableProperty]
    private string _cameraDeviceList = string.Empty;

    [ObservableProperty]
    private bool _hasCameraDeviceList;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImage))]
    [NotifyPropertyChangedFor(nameof(IsPreviewVisible))]
    [NotifyPropertyChangedFor(nameof(HasUploadedPhoto))]
    [NotifyPropertyChangedFor(nameof(HasAnyPhoto))]
    [NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]
    private IImage? _uploadedPhoto;

    private byte[]? _uploadedPhotoBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImage))]
    [NotifyPropertyChangedFor(nameof(IsPreviewVisible))]
    [NotifyPropertyChangedFor(nameof(PhotoSourceActionLabel))]
    [NotifyCanExecuteChangedFor(nameof(VerifyFaceCommand))]
    private bool _isShowingUploadedPhoto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCapturedPhoto))]
    [NotifyPropertyChangedFor(nameof(HasAnyPhoto))]
    [NotifyPropertyChangedFor(nameof(HasResultImage))]
    private IImage? _capturedPhoto;

    public bool HasCapturedPhoto => CapturedPhoto is not null;

    public bool HasUploadedPhoto => UploadedPhoto is not null;

    public bool HasAnyPhoto => HasCapturedPhoto || HasUploadedPhoto;

    public IImage? PreviewImage => IsShowingUploadedPhoto ? UploadedPhoto : CameraFrame;

    public bool IsPreviewVisible => IsPreviewActive || (IsShowingUploadedPhoto && UploadedPhoto is not null);

    public string PhotoSourceActionLabel =>
        IsShowingUploadedPhoto ? "Ganti ke Live Camera" : "Tampilkan Foto Upload";

    public TopLevel? TopLevel { get; set; }

    [ObservableProperty] private string _resultNama = "-";
    [ObservableProperty] private string _resultNoKartu = "-";
    [ObservableProperty] private string _resultUsia = "-";
    [ObservableProperty] private string _resultKeterangan = "-";
    [ObservableProperty] private bool _hasResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultImage))]
    private string? _fingerImage;

    public string ResultPlaceholderLabel => IsWajahMode ? "Wajah" : "Sidik jari";

    public bool HasResultImage => FingerImage is not null || CapturedPhoto is not null;

    private List<double>? FaceEncoding { get; set; }

    private void ResetResult()
    {
        ResultNama = "-";
        ResultNoKartu = "-";
        ResultUsia = "-";
        ResultKeterangan = "-";
        FingerImage = null;
        HasResult = false;
    }

    public void OnSessionChanged(bool loggedIn)
    {
        IsLoggedIn = loggedIn;
        if (loggedIn)
            return;

        ClearFrames();

        CapturedPhoto = null;
        UploadedPhoto = null;
        _uploadedPhotoBytes = null;
        IsShowingUploadedPhoto = false;

        FaceEncoding = null;

        FingerImage = null;
        ResetResult();
        IsScanning = false;
        ScanStatus = string.Empty;

        IdentityNumber = string.Empty;

        SetStatus(string.Empty, false);
    }

    public void OnCameraStatusChanged(bool connected, string? cameraStatus, string? cameraDetail)
    {
        CameraTitle = connected ? "Live Camera View" : "Kamera tidak tersedia";
        CameraStatus = connected
            ? cameraDetail ?? CameraServices.ConnectedDetail
            : cameraStatus ?? CameraServices.DisconnectedDetail;

        if (!connected)
        {
            ClearFrames();
        }

        RefreshCameraDeviceList();
    }

    private void OnFrameReady(object? sender, CameraFrameEventArgs e)
    {
        if (IsShowingUploadedPhoto)
            return;

        if (e.Width <= 0 || e.Height <= 0 || e.Stride <= 0)
            return;

        var needed = e.Stride * e.Height;
        if (needed <= 0 || e.Pixels.Length < needed)
            return;

        lock (_frameLock)
        {
            if (_staging.Length < needed)
                _staging = new byte[needed];

            Buffer.BlockCopy(e.Pixels, 0, _staging, 0, needed);
            _stagingWidth = e.Width;
            _stagingHeight = e.Height;
            _stagingStride = e.Stride;
        }

        if (Interlocked.Exchange(ref _renderPending, 1) == 1)
            return;

        Dispatcher.UIThread.Post(RenderFrame);
    }

    private void RenderFrame()
    {
        try
        {
            int width, height, stride;
            lock (_frameLock)
            {
                width = _stagingWidth;
                height = _stagingHeight;
                stride = _stagingStride;
            }

            if (width <= 0 || height <= 0 || stride <= 0)
                return;

            EnsureFrameBuffers(width, height);

            var target = _useSecondBuffer ? _frameA : _frameB;
            _useSecondBuffer = !_useSecondBuffer;

            if (target is null)
                return;

            using (var framebuffer = target.Lock())
            {
                var rowBytes = Math.Min(stride, framebuffer.RowBytes);

                lock (_frameLock)
                {
                    for (var y = 0; y < height; y++)
                    {
                        Marshal.Copy(
                            _staging,
                            y * stride,
                            IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes),
                            rowBytes);
                    }
                }
            }

            CameraFrame = target;
            IsPreviewActive = true;
        }
        finally
        {
            Interlocked.Exchange(ref _renderPending, 0);
        }
    }

    private void EnsureFrameBuffers(int width, int height)
    {
        if (_frameA is not null && _frameB is not null &&
            _frameA.PixelSize.Width == width && _frameA.PixelSize.Height == height)
        {
            return;
        }

        CameraFrame = null;
        IsPreviewActive = false;

        _frameA = CreateFrameBitmap(width, height);
        _frameB = CreateFrameBitmap(width, height);
        _useSecondBuffer = false;
    }

    private static WriteableBitmap CreateFrameBitmap(int width, int height) =>
        new(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

    private void ClearFrames()
    {
        CameraFrame = null;
        IsPreviewActive = false;

        lock (_frameLock)
        {
            _stagingWidth = 0;
            _stagingHeight = 0;
            _stagingStride = 0;
        }

        Interlocked.Exchange(ref _renderPending, 0);
    }
    private void RefreshCameraDeviceList()
    {
        var cameras = _services.Camera.AvailableCameras;
        HasCameraDeviceList = cameras.Count > 0;
        CameraDeviceList = cameras.Count > 0
            ? "Kamera terdeteksi:" + Environment.NewLine +
              string.Join(Environment.NewLine, cameras.Select(n => $"- {n}"))
            : string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanConnectCamera))]
    private async Task ConnectCameraAsync()
    {
        IsConnecting = true;
        SetStatus(string.Empty, false);

        try
        {
            var result = await _services.Camera.ReconnectAsync();

            OnCameraStatusChanged(result.Connected, _services.Camera.ResolveStatus(), result.Detail);

            if (result.Connected)
                SetStatus($"Kamera berhasil terhubung: {result.Detail}", false);
            else if (_services.Camera.IsDeviceConnected)
                SetStatus(CameraServices.DetailOpenFailed, true);
            else
                SetStatus(CameraServices.DetailDeviceNotFound, true);
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private bool CanConnectCamera() => IsWajahMode && !IsConnecting && !IsBusy;

    [RelayCommand]
    private async Task UploadPhotoAsync()
    {
        var topLevel = TopLevel;
        if (topLevel is null)
        {
            SetStatus("Jendela utama belum siap sehingga dialog pilih file tidak bisa dibuka.", true);
            return;
        }

        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Pilih Foto Wajah",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Foto (JPG, JPEG, PNG)")
                    {
                        Patterns = ["*.jpg", "*.jpeg", "*.png"],
                    },
                ],
            });
        }
        catch (Exception ex)
        {
            SetStatus($"Gagal membuka dialog pilih file: {ex.Message}", true);
            return;
        }

        if (files.Count == 0)
            return;

        var file = files[0];
        if (!IsSupportedPhotoFile(file.Name))
        {
            SetStatus("Format foto tidak didukung. Pilih foto JPG, JPEG, atau PNG.", true);
            return;
        }

        try
        {
            using var stream = await file.OpenReadAsync();
            using var buffer = new System.IO.MemoryStream();
            await stream.CopyToAsync(buffer);
            var bytes = buffer.ToArray();

            UploadedPhoto = new Bitmap(new System.IO.MemoryStream(bytes));
            _uploadedPhotoBytes = bytes;
            IsShowingUploadedPhoto = true;

            SetStatus($"Foto berhasil dipilih: {file.Name}", false);
        }
        catch (Exception ex)
        {
            UploadedPhoto = null;
            _uploadedPhotoBytes = null;
            IsShowingUploadedPhoto = false;

            SetStatus($"Gagal membaca file foto: {ex.Message}", true);
        }
    }

    private static bool IsSupportedPhotoFile(string fileName)
    {
        var extension = System.IO.Path.GetExtension(fileName);

        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void TogglePhotoSource() => IsShowingUploadedPhoto = !IsShowingUploadedPhoto;

    [RelayCommand]
    private void ClearPhoto()
    {
        CapturedPhoto = null;
        UploadedPhoto = null;
        _uploadedPhotoBytes = null;
        IsShowingUploadedPhoto = false;

        FaceEncoding = null;

        SetStatus("Foto dan hasil verifikasi dihapus.", false);
    }

    [RelayCommand(CanExecute = nameof(CanVerifyFace))]
    private async Task VerifyFaceAsync()
    {
        var identity = IdentityNumber.Trim();

        if (identity.Length == 0)
        {
            SetStatus("NIK / No. BPJS masih kosong. Silakan masukkan NIK atau No. BPJS terlebih dahulu.", true);
            return;
        }

        if (!identity.All(char.IsAsciiDigit))
        {
            SetStatus("NIK / No. BPJS hanya boleh berisi angka.", true);
            return;
        }

        if (identity.Length != NikLength && identity.Length != BpjsLength)
        {
            SetStatus(
                $"NIK / No. BPJS harus {NikLength} atau {BpjsLength} digit. " +
                $"Jumlah digit saat ini: {identity.Length}.", true);
            return;
        }

        var snapshot = CaptureCurrentImage();

        if (snapshot is null || !snapshot.HasInput)
        {
            SetStatus("Belum ada gambar untuk diverifikasi. Hubungkan kamera atau upload foto terlebih dahulu.", true);
            return;
        }

        CapturedPhoto = snapshot.Display;

        if (!_services.FaceRecognition.IsReady)
        {
            FaceEncoding = null;
            SetStatus(
                "Model wajah belum tersedia sehingga encoding tidak bisa diproses. " +
                $"Folder yang dicari: {_services.FaceRecognition.ModelDirectory}", true);
            return;
        }

        IsVerifyingFace = true;
        SetStatus("Memproses wajah...", false);

        try
        {
            var encoding = snapshot.EncodedBytes is { Length: > 0 }
                ? await _services.FaceRecognition.GetFaceEncodingAsync(snapshot.EncodedBytes)
                : await _services.FaceRecognition.GetFaceEncodingAsync(
                      snapshot.BgraPixels!, snapshot.Width, snapshot.Height, snapshot.Stride);

            if (encoding is null || encoding.Count != 128)
            {
                FaceEncoding = null;
                SetStatus("Encoding wajah tidak valid. Jumlah data harus 128.", true);
                return;
            }

            if (!IsWajahMode)
            {
                FaceEncoding = null;
                return;
            }

            FaceEncoding = encoding;
            SetStatus("Wajah berhasil diproses.", false);
        }
        catch (NoFaceDetectedExceptions)
        {
            FaceEncoding = null;
            SetStatus("Wajah tidak ditemukan pada foto.", true);
        }
        catch (MultipleFacesDetectedExceptions)
        {
            FaceEncoding = null;
            SetStatus("Terdapat lebih dari satu wajah pada foto. Pastikan hanya satu wajah yang terlihat.", true);
        }
        catch (FaceEncodingExceptions)
        {
            FaceEncoding = null;
            SetStatus("Encoding wajah gagal diproses.", true);
        }
        catch (OperationCanceledException)
        {
            FaceEncoding = null;
            SetStatus("Proses wajah dibatalkan.", true);
        }
        catch (FaceRecognitionExceptions ex)
        {
            FaceEncoding = null;
            SetStatus($"Pengenalan wajah gagal: {ex.Message}", true);
        }
        catch (Exception ex)
        {
            FaceEncoding = null;
            SetStatus($"Pengenalan wajah gagal: {ex.Message}", true);
        }
        finally
        {
            IsVerifyingFace = false;
        }
    }

    private bool CanVerifyFace() => IsWajahMode && IsPreviewVisible && !IsVerifyingFace && !IsBusy;

    private ImageSnapshot? CaptureCurrentImage()
    {
        if (IsShowingUploadedPhoto && UploadedPhoto is not null)
        {
            return new ImageSnapshot
            {
                Display = UploadedPhoto,
                EncodedBytes = _uploadedPhotoBytes,
            };
        }

        byte[] pixels;
        int width, height, stride;

        lock (_frameLock)
        {
            width = _stagingWidth;
            height = _stagingHeight;
            stride = _stagingStride;

            if (width <= 0 || height <= 0 || stride <= 0 || _staging.Length < stride * height)
                return null;

            pixels = new byte[stride * height];
            Buffer.BlockCopy(_staging, 0, pixels, 0, pixels.Length);
        }

        return new ImageSnapshot
        {
            Display = CreateStillBitmap(pixels, width, height, stride),
            BgraPixels = pixels,
            Width = width,
            Height = height,
            Stride = stride,
        };
    }

    private sealed record ImageSnapshot
    {
        public required IImage Display { get; init; }

        public byte[]? EncodedBytes { get; init; }

        public byte[]? BgraPixels { get; init; }

        public int Width { get; init; }

        public int Height { get; init; }

        public int Stride { get; init; }

        public bool HasInput =>
            EncodedBytes is { Length: > 0 } ||
            (BgraPixels is { Length: > 0 } && Width > 0 && Height > 0 && Stride >= Width * 4);
    }

    private static Bitmap CreateStillBitmap(byte[] pixels, int width, int height, int stride)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        using (var framebuffer = bitmap.Lock())
        {
            var rowBytes = Math.Min(stride, framebuffer.RowBytes);

            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(
                    pixels,
                    y * stride,
                    IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes),
                    rowBytes);
            }
        }

        return bitmap;
    }

    public void OnDeviceStatusChanged(bool connected)
    {
        DeviceConnected = connected;
        if (!connected && IsScanning)
        {
            IsScanning = false;
            ScanStatus = string.Empty;
            SetStatus("Perangkat fingerprint terputus saat memindai.", true);
        }
    }

    private bool CanVerify() =>
        IsFingerprintMode
        && IsLoggedIn
        && DeviceConnected
        && !IsBusy
        && !IsVerifyingFace
        && !IsConnecting
        && IsIdentityValid;

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync()
    {
        IsBusy = true;
        IsScanning = true;
        ScanStatus = "Letakkan jari pada perangkat...";
        SetStatus(string.Empty, false);

        try
        {
            var capture = await _services.Fingerprint.CaptureAsync(_services.Fingerprint.VerifyTimeoutMs);

            if (!IsFingerprintMode)
                return;

            ScanStatus = "Memverifikasi ke BPJS...";
            FingerImage = capture.ImageBase64;
            var result = await Task.Run(() => _services.Fingerprint.Verify(new Filters.Fingerprint.Verify
            {
                finger = capture.VerificationTemplate,
                keyword = IdentityNumber.Trim(),
                jnsFilter = ResolvedJnsFilter
            }));

            if (!result.succeeded)
            {
                ResetResult();
                SetStatus($"Verifikasi gagal: {string.Join(' ', result.errors)}", true);
                return;
            }

            if (!result.succeeded || (result.errors != null && result.errors.Length > 0))
            {
                ResetResult();
                string message = result.errors.Length > 0 ? string.Join(", ", result.errors) : result.message;
                SetStatus(message, true);
                return;
            }

            ScanStatus = "Memverifikasi kode booking pasien...";
            var validateApt = await Task.Run(() => _services.Appointment.AppointmentNoFromFinger(new Filters.Appointment.AppointmentNoFromFinger
            {
                dateTime = DateTime.Now,
                nik = result.data?.response.nik ?? string.Empty,
                noKartu = result.data?.response.noKartu ?? string.Empty,
                patientName = result.data?.response.nama ?? string.Empty
            }));

            if (!validateApt.succeeded || (validateApt.errors != null && validateApt.errors.Length > 0))
            {
                ResetResult();
                string message = validateApt.errors.Length > 0 ? string.Join(", ", validateApt.errors) : validateApt.message;
                SetStatus(message, true);
                return;
            }

            ScanStatus = "Memproses SEP ke BPJS...";
            var generateReg = await Task.Run(() => _services.Appointment.GenerateRegistration(validateApt.data ?? string.Empty));
            if (!generateReg.succeeded || (generateReg.errors != null && generateReg.errors.Length > 0))
            {
                ResetResult();
                string message = generateReg.errors.Length > 0 ? string.Join(", ", generateReg.errors) : generateReg.message;
                SetStatus(message, true);
                return;
            }

            var dateTime = DateTime.Now;
            var printerNormal = _services.Settings.Configuration["Apps:PrinterNormal"] ?? string.Empty;
            var printerLaser = _services.Settings.Configuration["Apps:PrinterLaser"] ?? string.Empty;

            ScanStatus = "Memproses cetak SEP...";
            var sep = await Task.Run(() => _services.Printout.Sep(validateApt.data ?? string.Empty, true, dateTime));
            if (sep.succeeded && sep.errors == null && sep.data != null)
            {
                try
                {
                    var data = sep.data;
                    List<InstanceReportSource> rptSource = [];
                    List<Models.Master.Parameter> parameters = [];

                    rptSource.Add(new InstanceReportSource
                    {
                        ReportDocument = new ReportLibrary.Sep()
                    });

                    parameters =
                    [
                        new()
                    {
                        name = "sep",
                        value = data.sep
                    },
                    new()
                    {
                        name = "tglsep",
                        value = data.tglsep
                    },
                    new()
                    {
                        name = "nokartu",
                        value = data.nokartu
                    },
                    new()
                    {
                        name = "nama",
                        value = data.nama
                    },
                    new()
                    {
                        name = "tgllahir",
                        value = data.tgllahir
                    },
                    new()
                    {
                        name = "sex",
                        value = data.sex
                    },
                    new()
                    {
                        name = "politujuan",
                        value = data.politujuan
                    },
                    new()
                    {
                        name = "polirujukan",
                        value = data.polirujukan
                    },
                    new()
                    {
                        name = "dokterpemeriksa",
                        value = data.dokterpemeriksa
                    },
                    new()
                    {
                        name = "dpjp",
                        value = data.dpjp
                    },
                    new()
                    {
                        name = "asalfaskes",
                        value = data.asalfaskes
                    },
                    new()
                    {
                        name = "diagnosa",
                        value = data.diagnosa
                    },
                    new()
                    {
                        name = "antrian",
                        value = Converter.IntToString(data.antrian)
                    },
                    new()
                    {
                        name = "jenispeserta",
                        value = data.jenispeserta
                    },
                    new()
                    {
                        name = "rm",
                        value = data.rm
                    },
                    new()
                    {
                        name = "jenisrawat",
                        value = data.jenisrawat
                    },
                    new()
                    {
                        name = "kelasrawat",
                        value = data.kelasrawat
                    },
                    new()
                    {
                        name = "cetakby",
                        value = data.cetakby
                    },
                    new()
                    {
                        name = "notlp",
                        value = data.notlp
                    },
                    new()
                    {
                        name = "urlqrcode",
                        value = data.linkqrcode
                    },
                    new()
                    {
                        name = "queueqr",
                        value = data.queueqr
                    }
                    ];

                    printProcess(new Filters.Printout.Print
                    {
                        rptSource = rptSource,
                        parameters = parameters,
                        printerName = printerNormal,
                        copies = 3
                    });
                }
                catch (Exception e)
                {
                    ResetResult();
                    SetStatus(e.Message, true);
                    return;
                }
            }

            ScanStatus = "Memproses cetak Label...";
            string aptNo = $"APT/{Converter.DateFormat(dateTime, DateFormat.Shortyearpattern)}-{validateApt.data}";
            var regNo = await Task.Run(() => _services.Appointment.AppointmentToRegistrationNumber(aptNo));
            if (!regNo.succeeded && regNo.errors != null)
            {
                ResetResult();
                string message = regNo.errors.Length > 0 ? string.Join(", ", regNo.errors) : regNo.message;
                SetStatus(message, true);
                return;
            }

            var label = await Task.Run(() => _services.Printout.Label(regNo.data ?? string.Empty, dateTime));
            if (label.succeeded && label.errors == null && label.data != null)
            {
                try
                {
                    var data = label.data;
                    List<InstanceReportSource> rptSource = [];
                    List<Models.Master.Parameter> parameters = [];

                    rptSource.Add(new InstanceReportSource
                    {
                        ReportDocument = new ReportLibrary.Barcode()
                    });

                    parameters =
                    [
                        new()
                        {
                            name = "nama",
                            value = data.nama
                        },
                        new()
                        {
                            name = "tgllahir",
                            value = data.tgllhr
                        },
                        new()
                        {
                            name = "sex",
                            value = data.lahirjk.Equals("m", StringComparison.CurrentCultureIgnoreCase) ? "Laki - Laki" : "Perempuan"
                        },
                        new()
                        {
                            name = "rm",
                            value = data.mr
                        },
                        new()
                        {
                            name = "noreg",
                            value = $"{data.mr} / {data.noreg}"
                        },
                        new()
                        {
                            name = "noantrian",
                            value = Converter.IntToString(data.noantrian)
                        }
                    ];

                    printProcess(new Filters.Printout.Print
                    {
                        rptSource = rptSource,
                        parameters = parameters,
                        printerName = printerLaser
                    });
                }
                catch (Exception e)
                {
                    ResetResult();
                    SetStatus(e.Message, true);
                    return;
                }
            }

            if (!IsFingerprintMode)
                return;

            ResultNama = result.data?.response.nama ?? "-";
            ResultNoKartu = result.data?.response.noKartu ?? "-";
            ResultUsia = $"{result.data?.response.usia} Tahun";
            ResultKeterangan = $"Code {result.data?.metaData?.code} · {result.data?.metaData?.message}";
            HasResult = true;
            SetStatus("Verifikasi berhasil.", false);
        }
        catch (FingerprintExceptions ex)
        {
            SetStatus(ex.Message, true);
            await _services.Fingerprint.RefreshDeviceAsync();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, true);
        }
        finally
        {
            IsScanning = false;
            ScanStatus = string.Empty;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Clear()
    {
        IdentityNumber = string.Empty;
        ResetResult();
        SetStatus(string.Empty, false);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
        HasStatus = !string.IsNullOrEmpty(message);
    }
}
