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

    // Naik setiap sumber foto berubah atau verifikasi baru dimulai; dipakai agar hasil
    // proses lama tidak menimpa status/hasil dari foto terbaru.
    private int _facePhotoGeneration;

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

    /// <summary>Diisi MainWindowViewModel; tombol "Login Sekarang" pada overlay login (§10.3).</summary>
    public Action? RequestLogin { get; set; }

    [RelayCommand]
    private void RequestLoginNow() => RequestLogin?.Invoke();

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
        NotifyResultState();

        if (!value)
            return;

        if (IsFingerprintMode) IsFingerprintMode = false;
        EnterWajahMode();
    }

    private void EnterFingerprintMode()
    {
        CapturedPhoto = null;
        UploadedPhoto = null;
        _uploadedPhotoBytes = null;
        IsShowingUploadedPhoto = false;
        ClearFrames();

        ResetFaceProgress();

        ResetResult();
        SetStatus(string.Empty, false);

        CameraUsageChanged?.Invoke();
    }

    private void EnterWajahMode()
    {
        FingerImage = null;
        IsScanning = false;
        ScanStatus = string.Empty;

        ResetFaceProgress();

        ResetResult();
        SetStatus(ResolveIdleFaceStatus(), false);

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
    [NotifyPropertyChangedFor(nameof(IsVerifying))]
    private bool _isBusy;

    [ObservableProperty] private string _scanStatus = string.Empty;
    [ObservableProperty] private bool _isScanning;

    // Teks tahapan Verifikasi Wajah. Dipisah dari ScanStatus (milik Fingerprint) supaya
    // status kedua metode tidak saling menimpa ketika petugas berpindah mode.
    [ObservableProperty] private string _faceStatus = string.Empty;

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

    /// <summary>
    /// Numpad sentuh pada view Verifikasi. Menulis ke <see cref="IdentityNumber"/> memakai
    /// jalur yang sama dengan pengetikan fisik: sanitasi & batas <see cref="NikLength"/>
    /// tetap ditangani <see cref="OnIdentityNumberChanged"/>, jadi validasi tidak berubah.
    /// </summary>
    [RelayCommand]
    private void AppendDigit(string? digit)
    {
        if (string.IsNullOrEmpty(digit) || !char.IsAsciiDigit(digit[0]))
            return;

        if (IdentityNumber.Length >= NikLength)
            return;

        IdentityNumber += digit[0];
    }

    /// <summary>Hapus satu karakter terakhir dari <see cref="IdentityNumber"/>.</summary>
    [RelayCommand]
    private void Backspace()
    {
        if (IdentityNumber.Length > 0)
            IdentityNumber = IdentityNumber[..^1];
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
    [NotifyPropertyChangedFor(nameof(IsVerifying))]
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

    [ObservableProperty] private string _resultNama = string.Empty;
    [ObservableProperty] private string _resultNoKartu = string.Empty;
    [ObservableProperty] private string _resultUsia = string.Empty;
    [ObservableProperty] private string _resultKeterangan = string.Empty;
    [ObservableProperty] private bool _hasResult;

    // State NoData (§10.1 state 5, resolusi A8): pesan memuat "tidak ditemukan" →
    // dibedakan dari Failure generik agar petugas tidak menyimpulkan kerusakan sistem.
    [ObservableProperty] private bool _isNoDataResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultImage))]
    private string? _fingerImage;

    public string ResultPlaceholderLabel => IsWajahMode
        ? "Hasil verifikasi wajah akan muncul di sini."
        : "Hasil verifikasi sidik jari akan muncul di sini.";

    public bool HasResultImage => FingerImage is not null || CapturedPhoto is not null;

    /// <summary>Sedang memproses verifikasi (sidik jari/wajah) — state Processing (§10.1).</summary>
    public bool IsVerifying => IsBusy || IsVerifyingFace;

    // ===== Lima state panel "Hasil Verifikasi" (§10.1) =====
    // Dihitung di VM agar view tidak menyusun kondisi multi-binding; perubahan
    // diberitakan lewat NotifyResultState() pada partial OnChanged di bawah.

    /// <summary>State 2 — Processing: verifikasi sidik jari/wajah sedang berjalan.</summary>
    public bool IsResultProcessing => IsVerifying;

    /// <summary>State 3 — Success: hasil verifikasi tersedia.</summary>
    public bool IsResultSuccess => !IsVerifying && HasResult;

    /// <summary>State 4 — Failure: status error tanpa hasil (ResetResult sudah dipanggil di alur gagal).</summary>
    public bool IsResultFailure =>
        !IsVerifying && !HasResult && HasStatus && StatusIsError && !IsNoDataResult;

    /// <summary>State 5 — NoData: API sukses tetapi data peserta tidak ditemukan.</summary>
    public bool IsResultNoData => !IsVerifying && !HasResult && IsNoDataResult;

    /// <summary>State 1 — Empty: tidak ada proses, hasil, maupun status error/no-data.</summary>
    public bool IsResultEmpty =>
        !IsVerifying && !HasResult && !IsResultFailure && !IsResultNoData;

    /// <summary>Teks status idle mode wajah (non-error) ditampilkan sebagai caption di kartu kiri.</summary>
    public bool IsIdleFaceHint =>
        IsWajahMode && !IsVerifying && HasStatus && !StatusIsError && !IsNoDataResult && !HasResult;

    private void NotifyResultState()
    {
        OnPropertyChanged(nameof(IsResultProcessing));
        OnPropertyChanged(nameof(IsResultSuccess));
        OnPropertyChanged(nameof(IsResultFailure));
        OnPropertyChanged(nameof(IsResultNoData));
        OnPropertyChanged(nameof(IsResultEmpty));
        OnPropertyChanged(nameof(IsIdleFaceHint));
    }

    partial void OnIsBusyChanged(bool value) => NotifyResultState();

    partial void OnIsVerifyingFaceChanged(bool value) => NotifyResultState();

    partial void OnHasResultChanged(bool value) => NotifyResultState();

    partial void OnHasStatusChanged(bool value) => NotifyResultState();

    partial void OnStatusIsErrorChanged(bool value) => NotifyResultState();

    partial void OnIsNoDataResultChanged(bool value) => NotifyResultState();

    private void ResetResult()
    {
        ResultNama = string.Empty;
        ResultNoKartu = string.Empty;
        ResultUsia = string.Empty;
        ResultKeterangan = string.Empty;
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

        FingerImage = null;
        ResetResult();
        IsScanning = false;
        ScanStatus = string.Empty;

        ResetFaceProgress();

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
        // Jangan menyentuh kamera saat verifikasi wajah sedang berjalan; frame yang
        // sedang diproses bisa hilang di tengah jalan.
        if (IsVerifyingFace)
            return;

        IsConnecting = true;
        SetStatus("Menghubungkan kamera...", false);

        try
        {
            var result = await _services.Camera.ReconnectAsync();

            OnCameraStatusChanged(result.Connected, _services.Camera.ResolveStatus(), result.Detail);

            if (result.Connected)
                SetStatus("Kamera siap, silakan arahkan wajah", false);
            else if (_services.Camera.IsDeviceConnected)
                SetStatus(CameraServices.DetailOpenFailed, true);
            else
                SetStatus(CameraServices.DetailDeviceNotFound, true);
        }
        catch (Exception ex)
        {
            SetStatus($"Gagal menghubungkan kamera: {SafeDetail(ex.Message)}", true);
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private bool CanConnectCamera() => IsWajahMode && !IsConnecting && !IsBusy && !IsVerifyingFace;

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

        SetStatus("Memuat foto...", false);

        try
        {
            using var stream = await file.OpenReadAsync();
            using var buffer = new System.IO.MemoryStream();
            await stream.CopyToAsync(buffer);
            var bytes = buffer.ToArray();

            UploadedPhoto = new Bitmap(new System.IO.MemoryStream(bytes));
            _uploadedPhotoBytes = bytes;
            IsShowingUploadedPhoto = true;

            // Sumber foto berganti -> proses/encoding dari foto lama tidak boleh dipakai lagi.
            InvalidateFacePhoto();

            SetStatus($"Foto siap diverifikasi: {SafeDetail(file.Name)}", false);
        }
        catch (Exception ex)
        {
            UploadedPhoto = null;
            _uploadedPhotoBytes = null;
            IsShowingUploadedPhoto = false;

            InvalidateFacePhoto();

            SetStatus($"Gagal membaca file foto: {SafeDetail(ex.Message)}", true);
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
    private void TogglePhotoSource()
    {
        IsShowingUploadedPhoto = !IsShowingUploadedPhoto;

        // Sumber gambar berganti, jadi hasil proses sebelumnya sudah tidak relevan.
        InvalidateFacePhoto();
        SetStatus(ResolveIdleFaceStatus(), false);
    }

    [RelayCommand]
    private void ClearPhoto()
    {
        CapturedPhoto = null;
        UploadedPhoto = null;
        _uploadedPhotoBytes = null;
        IsShowingUploadedPhoto = false;

        InvalidateFacePhoto();

        ResetResult();
        SetStatus("Menunggu foto wajah", false);
    }

    [RelayCommand(CanExecute = nameof(CanVerifyFace))]
    private async Task VerifyFaceAsync()
    {
        // Status transaksi sebelumnya dibersihkan lebih dulu supaya pesan sukses/gagal lama
        // tidak tertinggal ketika petugas memulai verifikasi baru.
        ResetResult();
        SetStatus(string.Empty, false);
        FaceStatus = string.Empty;

        // Dinaikkan di awal: verifikasi lama yang masih berjalan langsung dianggap basi
        // sehingga tidak bisa menimpa status/hasil dari foto terbaru.
        IsVerifyingFace = true;
        var generation = ++_facePhotoGeneration;

        try
        {
            FaceStatus = "Memeriksa data...";

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

            if (!IsLoggedIn)
            {
                SetStatus("Sesi login belum aktif. Silakan login ulang sebelum verifikasi wajah.", true);
                return;
            }

            if (IsConnecting)
            {
                SetStatus("Kamera masih sedang dihubungkan. Tunggu sampai kamera siap.", true);
                return;
            }

            FaceStatus = "Menyiapkan foto...";

            var snapshot = CaptureCurrentImage();

            if (snapshot is null || !snapshot.HasInput)
            {
                SetStatus("Belum ada gambar untuk diverifikasi. Hubungkan kamera atau upload foto terlebih dahulu.", true);
                return;
            }

            if (!_services.FaceRecognition.IsReady)
            {
                SetStatus(
                    "Model wajah belum tersedia sehingga encoding tidak bisa diproses. " +
                    $"Folder yang dicari: {SafeDetail(_services.FaceRecognition.ModelDirectory)}", true);
                return;
            }

            CapturedPhoto = snapshot.Display;

            FaceStatus = "Memproses wajah...";

            var encoding = snapshot.EncodedBytes is { Length: > 0 }
                ? await _services.FaceRecognition.GetFaceEncodingAsync(snapshot.EncodedBytes)
                : await _services.FaceRecognition.GetFaceEncodingAsync(
                      snapshot.BgraPixels!, snapshot.Width, snapshot.Height, snapshot.Stride);

            if (IsStaleFacePhoto(generation))
                return;

            if (encoding is null || encoding.Count != FaceRecognitionService.EncodingLength)
            {
                SetStatus(
                    $"Encoding wajah tidak valid. Jumlah data harus {FaceRecognitionService.EncodingLength}, " +
                    $"diterima {encoding?.Count ?? 0}.", true);
                return;
            }

            // Encoding hanya dipakai untuk request ini dan sengaja tidak disimpan ke field,
            // agar data biometrik tidak tertahan di memori setelah verifikasi selesai.
            FaceStatus = "Mengirim data untuk verifikasi wajah...";

            // Verify()/Nik() melakukan HTTP secara sinkron, jadi dijalankan di thread pool
            // agar UI tidak beku dan teks tahapan sempat ter-render.
            var verify = await Task.Run(() => _services.FaceRecognition.Verify(new Filters.FaceRecognition.Verify
            {
                encoding = encoding,
                id = identity,
                token = _services.Session.Token ?? string.Empty
            }));

            if (IsStaleFacePhoto(generation))
                return;

            FaceStatus = "Memproses hasil verifikasi wajah...";

            // Response berhasil
            // 1. Data -> status = true
            // 2. Data -> status = false, message = "Peserta telah terdaftar hari ini."
            // Catatan: Root<T>.errors default-nya array kosong (bukan null), jadi panjangnya
            // wajib dicek. Bila hanya null yang dicek, respons sukses pun terbaca gagal.
            if (verify.errors is { Length: > 0 })
            {
                SetStatus($"Verifikasi wajah gagal: {SafeDetail(string.Join(", ", verify.errors))}", true);
                return;
            }

            if (verify.data is null)
            {
                SetStatus(FaceServerNoResponseMessage, true);
                return;
            }

            if (!verify.data.status && !IsAlreadyRegisteredToday(verify.data.message))
            {
                SetStatus($"Verifikasi wajah gagal: {SafeDetail(verify.data.message)}", true);
                return;
            }

            FaceStatus = "Memverifikasi data peserta...";

            var nik = await Task.Run(() => _services.FaceRecognition.Nik(new Filters.FaceRecognition.NIK
            {
                nik = identity,
                token = _services.Session.Token ?? string.Empty
            }));

            if (IsStaleFacePhoto(generation))
                return;

            if (nik.errors is { Length: > 0 })
            {
                SetStatus($"Verifikasi wajah gagal: {SafeDetail(string.Join(", ", nik.errors))}", true);
                return;
            }

            if (nik.data is null)
            {
                SetStatus(FaceServerNoResponseMessage, true);
                return;
            }

            if (!nik.data.status)
            {
                SetStatus($"Verifikasi wajah gagal: {SafeDetail(nik.data.message)}", true);
                return;
            }

            FaceStatus = "Memverifikasi kode booking pasien...";

            var validateApt = await Task.Run(() => _services.Appointment.AppointmentNoFromFinger(new Filters.Appointment.AppointmentNoFromFinger
            {
                dateTime = DateTime.Now,
                nik = nik.data?.nik ?? string.Empty,
                noKartu = nik.data?.nokartu ?? string.Empty,
                patientName = nik.data?.nama ?? string.Empty
            }));

            if (IsStaleFacePhoto(generation))
                return;

            if (!validateApt.succeeded || (validateApt.errors != null && validateApt.errors.Length > 0))
            {
                string message = validateApt.errors.Length > 0 ? string.Join(", ", validateApt.errors) : validateApt.message;
                SetStatus($"Verifikasi wajah gagal: {SafeDetail(message)}", true);
                return;
            }

            FaceStatus = "Memproses SEP ke BPJS...";

            var generateReg = await Task.Run(() => _services.Appointment.GenerateRegistration(validateApt.data ?? string.Empty));
            if (!generateReg.succeeded || (generateReg.errors != null && generateReg.errors.Length > 0))
            {
                string message = generateReg.errors.Length > 0 ? string.Join(", ", generateReg.errors) : generateReg.message;
                SetStatus($"Verifikasi wajah gagal: {SafeDetail(message)}", true);
                return;
            }

            var dateTime = DateTime.Now;
            var printerNormal = _services.Settings.Configuration["Apps:PrinterNormal"] ?? string.Empty;
            var printerLaser = _services.Settings.Configuration["Apps:PrinterLaser"] ?? string.Empty;

            FaceStatus = "Memproses cetak SEP...";
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
                        printerName = printerNormal
                    });
                }
                catch (Exception e)
                {
                    SetStatus(e.Message, true);
                    return;
                }
            }

            FaceStatus = "Memproses cetak Label...";
            string aptNo = $"APT/{Converter.DateFormat(dateTime, DateFormat.Shortyearpattern)}-{validateApt.data}";
            var regNo = await Task.Run(() => _services.Appointment.AppointmentToRegistrationNumber(aptNo));
            if (!regNo.succeeded && regNo.errors != null)
            {
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

            if (IsStaleFacePhoto(generation))
                return;

            FaceStatus = "Menyiapkan hasil verifikasi...";

            ResultNama = nik.data?.nama ?? string.Empty;
            ResultNoKartu = nik.data?.nokartu ?? string.Empty;
            ResultKeterangan = $"Code {(nik.data?.status == true ? "200" : "404")} · {nik.data?.message}";
            HasResult = true;
            SetStatus("Verifikasi wajah berhasil.", false);
        }
        catch (TaskCanceledException)
        {
            ReportFaceFailure(generation, "Verifikasi wajah gagal: waktu tunggu habis saat menghubungi server. Silakan coba lagi.");
        }
        catch (OperationCanceledException)
        {
            ReportFaceFailure(generation, "Verifikasi wajah dibatalkan.");
        }
        catch (NoFaceDetectedExceptions)
        {
            ReportFaceFailure(generation, "Wajah tidak ditemukan pada foto.");
        }
        catch (MultipleFacesDetectedExceptions)
        {
            ReportFaceFailure(generation, "Terdapat lebih dari satu wajah pada foto. Pastikan hanya satu wajah yang terlihat.");
        }
        catch (FaceEncodingExceptions)
        {
            ReportFaceFailure(generation, "Encoding wajah gagal diproses.");
        }
        catch (FaceImageExceptions ex)
        {
            ReportFaceFailure(generation, $"Foto wajah tidak dapat diproses: {SafeDetail(ex.Message)}");
        }
        catch (FaceModelMissingExceptions ex)
        {
            ReportFaceFailure(generation, $"Model wajah belum tersedia: {SafeDetail(ex.Message)}");
        }
        catch (FaceRecognitionExceptions ex)
        {
            ReportFaceFailure(generation, $"Pengenalan wajah gagal: {SafeDetail(ex.Message)}");
        }
        catch (Exception ex)
        {
            ReportFaceFailure(generation, $"Pengenalan wajah gagal: {SafeDetail(ex.Message)}");
        }
        finally
        {
            // Selalu dilepas, termasuk saat exception atau pembatalan, supaya tombol
            // Verifikasi Wajah tidak pernah tersangkut dalam keadaan nonaktif.
            IsVerifyingFace = false;
            FaceStatus = string.Empty;
        }
    }

    private bool CanVerifyFace() =>
        IsWajahMode
        && IsPreviewVisible
        && !IsVerifyingFace
        && !IsBusy
        && !IsConnecting;

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
                        printerName = printerNormal
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

            ResultNama = result.data?.response.nama ?? string.Empty;
            ResultNoKartu = result.data?.response.noKartu ?? string.Empty;
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
        ResetFaceProgress();
        SetStatus(IsWajahMode ? ResolveIdleFaceStatus() : string.Empty, false);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
        HasStatus = !string.IsNullOrEmpty(message);

        // Penanda NoData (§10.1 state 5): hasil API dengan pesan "tidak ditemukan"
        // ditampilkan sebagai banner Warning, bukan Danger.
        IsNoDataResult = HasStatus &&
            message.Contains("tidak ditemukan", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tampilkan kegagalan Verifikasi Wajah hanya bila proses yang bersangkutan masih
    /// relevan (foto/sumber gambar belum diganti petugas di tengah proses).
    /// </summary>
    private void ReportFaceFailure(int generation, string message)
    {
        if (IsStaleFacePhoto(generation))
            return;

        SetStatus(message, true);
    }

    private bool IsStaleFacePhoto(int generation) => generation != _facePhotoGeneration;

    /// <summary>
    /// Tandai bahwa sumber foto berubah, sehingga proses verifikasi lama tidak boleh
    /// lagi menimpa status maupun hasil.
    /// </summary>
    private int InvalidateFacePhoto() => ++_facePhotoGeneration;

    private void ResetFaceProgress()
    {
        // IsVerifyingFace sengaja tidak dipaksa false di sini: flag itu milik tunggal
        // VerifyFaceAsync dan selalu dilepas di blok finally agar tombol tidak tersangkut.
        _facePhotoGeneration++;
        FaceStatus = string.Empty;
    }

    /// <summary>
    /// Pesan idle Verifikasi Wajah, mengikuti kondisi kamera/foto yang sebenarnya.
    /// </summary>
    private string ResolveIdleFaceStatus()
    {
        if (IsConnecting)
            return "Menghubungkan kamera...";

        if (IsPreviewActive)
            return "Kamera siap, silakan arahkan wajah";

        if (IsShowingUploadedPhoto && UploadedPhoto is not null)
            return "Foto siap diverifikasi";

        if (HasUploadedPhoto)
            return "Menunggu foto wajah";

        return "Silakan hubungkan kamera atau upload foto";
    }

    private const int MaxStatusDetailLength = 240;

    private const string FaceServerNoResponseMessage =
        "Verifikasi wajah gagal: server FRISTA tidak memberikan respons yang valid. " +
        "Periksa koneksi jaringan atau coba lagi beberapa saat.";

    private const string AlreadyRegisteredTodayMessage = "peserta telah terdaftar hari ini.";

    private static bool IsAlreadyRegisteredToday(string? message) =>
        string.Equals(message?.Trim(), AlreadyRegisteredTodayMessage, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Rapikan dan batasi panjang detail teknis sebelum ditampilkan ke petugas, supaya
    /// payload besar (mis. body request yang memuat encoding wajah) tidak bocor ke UI.
    /// </summary>
    private static string SafeDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "-";

        var cleaned = string.Join(' ', detail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return cleaned.Length <= MaxStatusDetailLength
            ? cleaned
            : cleaned[..MaxStatusDetailLength].TrimEnd() + "...";
    }
}
