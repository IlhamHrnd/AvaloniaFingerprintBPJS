using AvaloniaFingerprint.Exceptions;
using AvaloniaFingerprint.Models;
using AvaloniaFingerprint.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.ViewModels;

/// <summary>Port of the WinForms <c>RegistrasiFingerprint</c> form.</summary>
public sealed partial class RegistrasiFingerprintViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly DaftarFingerprintViewModel _daftar;

    private readonly List<object> _rightFmds = new();
    private readonly List<object> _leftFmds = new();
    private string? _rightTemplate;
    private string? _leftTemplate;

    public RegistrasiFingerprintViewModel(AppServices services, DaftarFingerprintViewModel daftar)
    {
        _services = services;
        _daftar = daftar;
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
        var options = _services.Settings.Fingerprint;
        var total = Math.Max(1, options.FingersPerHand);
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

                var capture = await _services.Fingerprint.CaptureAsync(options.CaptureTimeoutMs);

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

    [RelayCommand(CanExecute = nameof(CanRegist))]
    private async Task RegistAsync()
    {
        var options = _services.Settings.Fingerprint;

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
                        index = options.RightHandIndex,
                        imgStr = RightImage ?? string.Empty
                    },
                    new()
                    {
                        finger = _leftTemplate ?? string.Empty,
                        index = options.LeftHandIndex,
                        imgStr = LeftImage ?? string.Empty
                    }
                }
            };

            var result = await _services.Api.AddPesertaAsync(payload, _services.Session.Token);

            if (!result.Success)
            {
                SetStatus($"Fingerprint gagal disimpan: {result.Error}", true);
                return;
            }

            if (result.Data?.response != true)
            {
                var message = result.Data?.metaData?.message;
                SetStatus(string.IsNullOrWhiteSpace(message)
                    ? "Nomor peserta tidak valid (harus 13 karakter)."
                    : $"Gagal: {message}", true);
                return;
            }

            await _daftar.AddAsync(new FingerprintRecord
            {
                NoKapst = payload.nokapst,
                Tanggal = DateTime.Now,
                JumlahJari = _rightFmds.Count + _leftFmds.Count,
                Petugas = _services.Session.Username,
                Status = "Terkirim",
                ImgRight = RightImage,
                ImgLeft = LeftImage
            });

            SetStatus("Fingerprint berhasil disimpan.", false);
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
