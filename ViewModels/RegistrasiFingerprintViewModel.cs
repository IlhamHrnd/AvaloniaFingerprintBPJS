using AvaloniaFingerprint.Exceptions;
using AvaloniaFingerprint.Filters.Fingerprint;
using AvaloniaFingerprint.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.ViewModels;

/// <summary>Port of the WinForms <c>RegistrasiFingerprint</c> form.</summary>
public sealed partial class RegistrasiFingerprintViewModel : ViewModelBase
{
    private readonly AppServices _services;

    private readonly List<object> _rightFmds = new();
    private readonly List<object> _leftFmds = new();
    private string? _rightTemplate;
    private string? _leftTemplate;

    public RegistrasiFingerprintViewModel(AppServices services)
    {
        _services = services;
        DeviceConnected = services.Fingerprint.IsDeviceConnected;
    }

    public int NoKapstMaxLength => 13;

    // ---- input ---------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegistCommand))]
    private string _noKapst = string.Empty;

    // ---- state ---------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanRightCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanLeftCommand))]
    [NotifyCanExecuteChangedFor(nameof(RegistCommand))]
    private bool _isLoggedIn;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanRightCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanLeftCommand))]
    private bool _deviceConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanRightCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanLeftCommand))]
    [NotifyCanExecuteChangedFor(nameof(RegistCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _scanStatus = string.Empty;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private double _scanProgress;

    [ObservableProperty] private string? _rightImage;
    [ObservableProperty] private string? _leftImage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegistCommand))]
    private bool _rightDone;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegistCommand))]
    private bool _leftDone;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasStatus;
    [ObservableProperty] private bool _statusIsError;

    public void OnSessionChanged(bool loggedIn)
    {
        IsLoggedIn = loggedIn;
        if (!loggedIn)
            ResetInternal();
    }

    public void OnDeviceStatusChanged(bool connected)
    {
        DeviceConnected = connected;
        if (!connected && IsScanning)
            SetStatus("Perangkat fingerprint terputus saat memindai.", true);
    }

    private bool CanScan() => IsLoggedIn && DeviceConnected && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private Task ScanRightAsync() => ScanHandAsync(isRight: true);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private Task ScanLeftAsync() => ScanHandAsync(isRight: false);

    /// <summary>
    /// Scans four fingers of one hand and merges them into a single registration template,
    /// mirroring the enrollment loop of the original form.
    /// </summary>
    private async Task ScanHandAsync(bool isRight)
    {
        var total = Math.Max(1, _services.Fingerprint.FingersPerHand);
        var hand = isRight ? "kanan" : "kiri";
        var fmds = isRight ? _rightFmds : _leftFmds;

        IsBusy = true;
        IsScanning = true;
        ScanProgress = 0;
        SetStatus(string.Empty, false);
        fmds.Clear();

        string? firstImage = null;

        try
        {
            for (var i = 1; i <= total; i++)
            {
                ScanStatus = $"Pindai jari {hand} ke-{i} dari {total}";

                var capture = await _services.Fingerprint.CaptureAsync(_services.Fingerprint.CaptureTimeoutMs);

                if (capture.PreRegistrationFmd is not null)
                    fmds.Add(capture.PreRegistrationFmd);

                firstImage ??= capture.ImageBase64;
                if (isRight)
                    RightImage = capture.ImageBase64;
                else
                    LeftImage = capture.ImageBase64;

                ScanProgress = (double)i / total * 100;
            }

            var template = _services.Fingerprint.CreateEnrollmentTemplate(fmds);

            if (isRight)
            {
                _rightTemplate = template;
                RightImage = firstImage;
                RightDone = true;
            }
            else
            {
                _leftTemplate = template;
                LeftImage = firstImage;
                LeftDone = true;
            }

            ScanStatus = "Scan berhasil";
            SetStatus($"Sidik jari tangan {hand} berhasil dipindai.", false);
        }
        catch (FingerprintExceptions ex)
        {
            fmds.Clear();
            if (isRight) RightDone = false; else LeftDone = false;
            SetStatus(ex.Message, true);
            await _services.Fingerprint.RefreshDeviceAsync();
        }
        catch (Exception ex)
        {
            fmds.Clear();
            if (isRight) RightDone = false; else LeftDone = false;
            SetStatus(ex.Message, true);
        }
        finally
        {
            IsScanning = false;
            ScanStatus = string.Empty;
            ScanProgress = 0;
            IsBusy = false;
        }
    }

    private bool CanRegist() =>
        IsLoggedIn && !IsBusy && RightDone && LeftDone && NoKapst.Trim().Length > 0;

    // Sinkron: satu-satunya await di sini dulu menyalin sidik jari ke store lokal, dan itu
    // sudah dihapus karena aplikasi dilarang menyimpan data biometrik pasien. AddFinger sendiri
    // panggilan RestSharp yang blocking, jadi tidak ada lagi yang perlu di-await.
    [RelayCommand(CanExecute = nameof(CanRegist))]
    private void Regist()
    {
        IsBusy = true;
        SetStatus("Mengirim data ke BPJS...", false);

        try
        {
            var payload = new AddPeserta
            {
                nokapst = NoKapst.Trim(),
                finger = new List<AddPeserta.Finger>
                {
                    new()
                    {
                        finger = _rightTemplate ?? string.Empty,
                        index = _services.Fingerprint.RightHandIndex,
                        imgStr = RightImage ?? string.Empty
                    },
                    new()
                    {
                        finger = _leftTemplate ?? string.Empty,
                        index = _services.Fingerprint.LeftHandIndex,
                        imgStr = LeftImage ?? string.Empty
                    }
                }
            };

            var result = _services.Fingerprint.AddFinger(payload);
            if (!result.succeeded || result.data == null)
            {
                SetStatus($"Fingerprint gagal dikirim: {result.message} - {result.data?.metaData?.message}", true);
                return;
            }

            // Template dan gambar sidik jari hanya diproses sementara: dipegang di memori,
            // dikirim ke BPJS lewat AddFinger, lalu dilepas oleh ResetInternal(). Tidak ada
            // penyalinan ke database, file lokal, cache, session, maupun log.
            SetStatus("Registrasi fingerprint berhasil dikirim ke BPJS.", false);
            ResetInternal();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Reset()
    {
        ResetInternal();
        SetStatus(string.Empty, false);
    }

    private void ResetInternal()
    {
        NoKapst = string.Empty;
        _rightFmds.Clear();
        _leftFmds.Clear();
        _rightTemplate = null;
        _leftTemplate = null;
        RightImage = null;
        LeftImage = null;
        RightDone = false;
        LeftDone = false;
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
        HasStatus = !string.IsNullOrEmpty(message);
    }
}
