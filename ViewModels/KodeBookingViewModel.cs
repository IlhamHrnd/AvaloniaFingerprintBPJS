using AvaloniaFingerprint.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.ViewModels;

public sealed partial class KodeBookingViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public KodeBookingViewModel(AppServices services)
    {
        _services = services;
    }

    [ObservableProperty]
    private string _kodeBooking = string.Empty;

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatus;

    [ObservableProperty]
    private bool _statusIsError;

    public void OnSessionChanged(bool loggedIn)
    {
        IsLoggedIn = loggedIn;
        if (!loggedIn)
        {
            Reset();
        }
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        var kdBooking = KodeBooking?.Trim();
        if (string.IsNullOrEmpty(kdBooking))
        {
            SetStatus("Kode booking wajib diisi.", true);
            return;
        }

        IsBusy = true;
        SetStatus(string.Empty, false);

        try
        {
            var result = await Task.Run(() => _services.Appointment.GenerateRegistration(kdBooking));

            if (result.succeeded && (result.errors == null || result.errors.Length == 0))
            {
                var msg = !string.IsNullOrWhiteSpace(result.message)
                    ? result.message
                    : "Kode booking berhasil diproses.";
                SetStatus(msg, false);
            }
            else
            {
                var errorMsg = result.errors != null && result.errors.Length > 0
                    ? string.Join(", ", result.errors)
                    : (!string.IsNullOrWhiteSpace(result.message) ? result.message : "Kode booking gagal diproses.");
                SetStatus(errorMsg, true);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Terjadi kesalahan: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CetakUlangBarcode()
    {
        var kdBooking = KodeBooking?.Trim();
        if (string.IsNullOrWhiteSpace(kdBooking))
        {
            SetStatus("Kode booking tidak boleh kosong.", true);
            return;
        }

        SetStatus("Kode booking valid. Proses cetak ulang barcode siap diproses.", false);
    }

    [RelayCommand]
    private void Reset()
    {
        KodeBooking = string.Empty;
        SetStatus(string.Empty, false);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
        HasStatus = !string.IsNullOrEmpty(message);
    }
}
