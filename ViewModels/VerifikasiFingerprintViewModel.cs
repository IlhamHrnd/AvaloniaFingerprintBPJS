using AvaloniaFingerprint.Converters;
using AvaloniaFingerprint.Exceptions;
using AvaloniaFingerprint.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telerik.Reporting;

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
            var result = await Task.Run(() => _services.Fingerprint.Verify(new Filters.Fingerprint.Verify
            {
                finger = capture.VerificationTemplate,
                keyword = Number.Trim(),
                jnsFilter = IsNoBpjs ? "psnoka" : "nik"
            }));

            if (!result.succeeded)
            {
                ClearResult();
                SetStatus($"Verifikasi gagal: {string.Join(' ', result.errors)}", true);
                return;
            }

            if (!result.succeeded || (result.errors != null && result.errors.Length > 0))
            {
                ClearResult();
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
                ClearResult();
                string message = validateApt.errors.Length > 0 ? string.Join(", ", validateApt.errors) : validateApt.message;
                SetStatus(message, true);
                return;
            }

            //Proses Generate Nomor Registrasi
            ScanStatus = "Memproses SEP ke BPJS...";
            var generateReg = await Task.Run(() => _services.Appointment.GenerateRegistration(validateApt.data ?? string.Empty));
            if (!generateReg.succeeded || (generateReg.errors != null && generateReg.errors.Length > 0))
            {
                ClearResult();
                string message = generateReg.errors.Length > 0 ? string.Join(", ", generateReg.errors) : generateReg.message;
                SetStatus(message, true);
                return;
            }

            var dateTime = DateTime.Now;
            var printerName = _services.Settings.Configuration["Apps:PrinterNormal"] ?? string.Empty;
            var printerLaser = _services.Settings.Configuration["Apps:PrinterLaser"] ?? string.Empty;

            // Proses cetak SEP
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

                    printProcess(rptSource, parameters, printerName, 1, 1, 3);
                }
                catch (Exception e)
                {
                    ClearResult();
                    SetStatus(e.Message, true);
                    return;
                }
            }

            // Proses cetak Label
            ScanStatus = "Memproses cetak Label...";
            string aptNo = $"APT/{Converter.DateFormat(dateTime, DateFormat.Shortyearpattern)}-{validateApt.data}";
            var regNo = await Task.Run(() => _services.Appointment.AppointmentToRegistrationNumber(aptNo));
            if (!regNo.succeeded && regNo.errors != null)
            {
                ClearResult();
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

                    printProcess(rptSource, parameters, printerLaser, 1, 1, 1);
                }
                catch (Exception e)
                {
                    ClearResult();
                    SetStatus(e.Message, true);
                    return;
                }
            }

            ResultNama = result.data?.response.nama ?? "-";
            ResultNoKartu = result.data?.response.noKartu ?? "-";
            ResultUsia = $"{result.data?.response.usia} Tahun";
            ResultMeta = $"Code {result.data?.metaData?.code} · {result.data?.metaData?.message}";
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
