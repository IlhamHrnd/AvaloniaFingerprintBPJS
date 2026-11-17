using AvaloniaFingerprint.Models.Patient;
using AvaloniaFingerprint.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.ViewModels;

/// <summary>
/// Halaman "Rujukan Baru" (§8.7). Tahap integrasi pencarian pasien: No. Rekam Medis
/// dicari lewat <see cref="PatientServices.GetPatientList"/> dengan searchType = 2,
/// lalu Nama Pasien diisi dari <c>fullName</c> dan <see cref="PatientId"/> disimpan
/// untuk tahap registrasi rujukan berikutnya. Verifikasi sidik jari, pemilihan
/// rujukan/poli/dokter, kuota, dan cetak label masih UI-only (§11).
/// </summary>
public sealed partial class RujukanBaruViewModel : ViewModelBase
{
    /// <summary>
    /// Nilai <c>searchType</c> API Patient untuk pencarian No. Rekam Medis. Selalu 2:
    /// jenis pencarian tidak pernah ditebak dari isi input (tanpa mode ID/nama pasien).
    /// </summary>
    private const int SearchTypeMedicalNo = 2;

    private readonly AppServices _services;

    /// <summary>
    /// Naik setiap pencarian dimulai atau No. RM berubah. Respons dengan generasi
    /// usang dibuang agar tidak menimpa state pasien yang lebih baru (§6).
    /// </summary>
    private int _searchGeneration;

    public RujukanBaruViewModel(AppServices services)
    {
        _services = services;
    }

    /// <summary>No. Rekam Medis yang diketik/dipindai petugas; dikirim apa adanya sebagai filter.</summary>
    [ObservableProperty]
    private string _nomorRekamMedis = string.Empty;

    /// <summary>Nama pasien hasil pencarian (<c>fullName</c>); hanya ditampilkan, read-only.</summary>
    [ObservableProperty]
    private string _namaPasien = string.Empty;

    /// <summary>patientId pasien terpilih; dipakai tahap registrasi rujukan berikutnya.</summary>
    [ObservableProperty]
    private string _patientId = string.Empty;

    /// <summary>medicalNo yang dikembalikan API untuk pasien terpilih.</summary>
    [ObservableProperty]
    private string _medicalNo = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatus;

    [ObservableProperty]
    private bool _statusIsError;

    public IReadOnlyList<string> RujukanOptions { get; } =
    [
        "090101000120Y005992 - 31 Jan 2020 (Puskesmas Gambir)"
    ];

    public IReadOnlyList<string> PoliOptions { get; } = ["PARU"];

    public IReadOnlyList<string> DokterOptions { get; } =
    [
        "dr. Martua Rizal Situmorang, Sp.N., M.Kes., FINA"
    ];

    /// <summary>
    /// Setiap perubahan No. RM membatalkan pasien yang sedang terpilih: nama, patientId,
    /// dan medicalNo dibuang sehingga pencarian ulang wajib dilakukan (§6).
    /// </summary>
    partial void OnNomorRekamMedisChanged(string value)
    {
        _searchGeneration++;
        ClearPatient();
        SetStatus(string.Empty, false);
    }

    /// <summary>Logout membuang state pasien agar tidak terbawa ke sesi berikutnya.</summary>
    public void OnSessionChanged(bool loggedIn)
    {
        if (loggedIn)
            return;

        NomorRekamMedis = string.Empty;
        ClearPatient();
        SetStatus(string.Empty, false);
    }

    /// <summary>
    /// Mencari pasien berdasarkan No. Rekam Medis (searchType = 2). Input kosong
    /// ditolak sebelum memanggil API; hasil lama dibuang lebih dulu agar data
    /// pasien sebelumnya tidak pernah tampil bersama pesan baru.
    /// </summary>
    [RelayCommand]
    private async Task CariPasienAsync()
    {
        // Satu pencarian pada satu waktu; tombol dan input juga nonaktif saat IsBusy.
        if (IsBusy)
            return;

        // Trim saja: No. RM tidak pernah diubah ke angka, jadi nol di depan tetap utuh.
        var filter = NomorRekamMedis.Trim();
        if (filter.Length == 0)
        {
            ClearPatient();
            SetStatus("No. Rekam Medis wajib diisi sebelum mencari pasien.", true);
            return;
        }

        var generation = ++_searchGeneration;
        IsBusy = true;
        SetStatus(string.Empty, false);

        try
        {
            var result = await Task.Run(
                () => _services.Patient.GetPatientList(filter, SearchTypeMedicalNo));

            // Respons usang (No. RM sudah berubah / pencarian lain sudah dimulai).
            if (generation != _searchGeneration)
                return;

            if (!result.succeeded)
            {
                ClearPatient();
                SetStatus(DescribeFailure(result.errors, result.message), true);
                return;
            }

            var patient = result.data?.FirstOrDefault();
            var fullName = ResolveFullName(patient);
            if (patient is null || fullName.Length == 0)
            {
                ClearPatient();
                SetStatus($"Pasien dengan No. Rekam Medis {filter} tidak ditemukan.", true);
                return;
            }

            PatientId = patient.patientId;
            MedicalNo = string.IsNullOrWhiteSpace(patient.medicalNo) ? filter : patient.medicalNo;
            NamaPasien = fullName;
            SetStatus($"Pasien ditemukan: {fullName}.", false);
        }
        catch (Exception e)
        {
            if (generation != _searchGeneration)
                return;

            ClearPatient();
            SetStatus($"Pencarian pasien gagal: {e.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearPatient()
    {
        NamaPasien = string.Empty;
        PatientId = string.Empty;
        MedicalNo = string.Empty;
    }

    private static string DescribeFailure(string[]? errors, string? message)
    {
        var detail = errors is { Length: > 0 }
            ? string.Join(", ", errors.Where(e => !string.IsNullOrWhiteSpace(e)))
            : string.Empty;

        if (detail.Length > 0)
            return $"Pencarian pasien gagal: {detail}";

        return !string.IsNullOrWhiteSpace(message)
            ? $"Pencarian pasien gagal: {message}"
            : "Pencarian pasien gagal. Silakan coba lagi.";
    }

    /// <summary>
    /// fullName dipakai apa adanya. Rangkaian nama hanya disusun bila API tidak
    /// mengirim fullName, supaya nama tidak direkonstruksi saat sudah tersedia.
    /// </summary>
    private static string ResolveFullName(PatientList? patient)
    {
        if (patient is null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(patient.fullName))
            return patient.fullName.Trim();

        var parts = new[] { patient.firstName, patient.middleName, patient.lastName }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim());

        return string.Join(' ', parts);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
        HasStatus = !string.IsNullOrEmpty(message);
    }
}
