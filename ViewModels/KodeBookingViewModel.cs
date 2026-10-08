using AvaloniaFingerprint.Converters;
using AvaloniaFingerprint.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telerik.Reporting;

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
    private async Task CetakUlangBarcode()
    {
        var kdBooking = KodeBooking?.Trim();
        if (string.IsNullOrWhiteSpace(kdBooking))
        {
            SetStatus("Kode booking tidak boleh kosong.", true);
            return;
        }

        var dateTime = DateTime.Now;
        var printerNormal = _services.Settings.Configuration["Apps:PrinterNormal"] ?? string.Empty;
        var printerLaser = _services.Settings.Configuration["Apps:PrinterLaser"] ?? string.Empty;

        // Tahap 1: cetak SEP. Kegagalan di sini hanya dilaporkan lewat SetStatus; cetak Label
        // di bawah tetap dijalankan.
        var sep = await Task.Run(() => _services.Printout.Sep(kdBooking, true, dateTime));
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
                SetStatus(e.Message, true);
            }
        }

        // Tahap 2: cetak Label. Berbeda dari SEP, kegagalan di sini menghentikan seluruh proses.
        string aptNo = $"APT/{Converter.DateFormat(dateTime, DateFormat.Shortyearpattern)}-{kdBooking}";
        var regNo = await Task.Run(() => _services.Appointment.AppointmentToRegistrationNumber(aptNo));
        if (!regNo.succeeded && regNo.errors != null)
        {
            SetStatus($"Verifikasi gagal: {string.Join(' ', regNo.errors)}", true);
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
                SetStatus(e.Message, true);
            }
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
