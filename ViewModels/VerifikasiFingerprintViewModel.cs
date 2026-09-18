using AvaloniaFingerprint.Exceptions;
using AvaloniaFingerprint.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.ViewModels;

/// <summary>Port of the WinForms <c>VerifikasiFingerprint</c> form.</summary>
public sealed partial class VerifikasiFingerprintViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public VerifikasiFingerprintViewModel(AppServices services)
    {
        _services = services;
        DeviceConnected = services.Fingerprint.IsDeviceConnected;
    }

    // ---- input ---------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private string _number = string.Empty;

    [ObservableProperty] private bool _isNik = true;
    [ObservableProperty] private bool _isNoBpjs;

    public int NumberMaxLength => 16;

    // ---- state ---------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private bool _isLoggedIn;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private bool _deviceConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _scanStatus = string.Empty;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasStatus;
    [ObservableProperty] private bool _statusIsError;

    // ---- result --------------------------------------------------------------

    [ObservableProperty] private string _resultNama = "-";
    [ObservableProperty] private string _resultNoKartu = "-";
    [ObservableProperty] private string _resultUsia = "-";
    [ObservableProperty] private string _resultMeta = "-";
    [ObservableProperty] private string? _fingerImage;
    [ObservableProperty] private bool _hasResult;

    public void OnSessionChanged(bool loggedIn)
    {
        IsLoggedIn = loggedIn;
        if (!loggedIn)
            ClearResult();
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

    private bool CanVerify() => IsLoggedIn && DeviceConnected && !IsBusy && Number.Trim().Length > 12;

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

            ScanStatus = "Memverifikasi ke BPJS...";
            FingerImage = capture.ImageBase64;

            var result = _services.Fingerprint.Verify(new Filters.Fingerprint.Verify
            {
                finger = capture.VerificationTemplate,
                keyword = Number.Trim(),
                jnsFilter = IsNoBpjs ? "psnoka" : "nik"
            });

            if (!result.succeeded)
            {
                ClearResult();
                SetStatus($"Verifikasi gagal: {result.errors}", true);
                return;
            }

            var root = result.data!;
            if (root.response?.noKartu is null)
            {
                ClearResult();
                var message = root.metaData?.message;
                SetStatus(message == "Request failed with status code InternalServerError"
                    ? "Server sedang sibuk, coba lagi."
                    : "Fingerprint belum terdaftar. Silakan daftar pada tab Registrasi Fingerprint.", true);
                return;
            }

            var validateApt = _services.Appointment.AppointmentNoFromFinger(new Filters.Appointment.AppointmentNoFromFinger
            {
                dateTime = DateTime.Now,
                nik = result.data?.response.nik ?? string.Empty,
                noKartu = result.data?.response.noKartu ?? string.Empty,
                patientName = result.data?.response.nama ?? string.Empty
            });

            if (!validateApt.succeeded || validateApt.errors.Length > 0)
            {
                ClearResult();
                string message = validateApt.errors.Length > 0 ? string.Join(", ", validateApt.errors) : validateApt.message;
                SetStatus(message, true);
                return;
            }

            ResultNama = root.response.nama ?? "-";
            ResultNoKartu = root.response.noKartu ?? "-";
            ResultUsia = $"{root.response.usia} Tahun";
            ResultMeta = $"Code {root.metaData?.code} · {root.metaData?.message}";
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
        Number = string.Empty;
        ClearResult();
        SetStatus(string.Empty, false);
    }

    private void ClearResult()
    {
        ResultNama = "-";
        ResultNoKartu = "-";
        ResultUsia = "-";
        ResultMeta = "-";
        FingerImage = null;
        HasResult = false;
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
        HasStatus = !string.IsNullOrEmpty(message);
    }

    partial void OnIsNikChanged(bool value)
    {
        if (value) IsNoBpjs = false;
    }

    partial void OnIsNoBpjsChanged(bool value)
    {
        if (value) IsNik = false;
    }
}
