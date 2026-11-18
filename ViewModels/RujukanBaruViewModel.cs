using AvaloniaFingerprint.Converters;
using AvaloniaFingerprint.Models.Patient;
using AvaloniaFingerprint.Models.Rujukan;
using AvaloniaFingerprint.Models.ServiceUnit;
using AvaloniaFingerprint.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.ViewModels;

/// <summary>
/// Halaman "Rujukan Baru" (§8.7). Alur lengkap: cari pasien → pilih rujukan → poli →
/// dokter → kuota.
/// <para>
/// No. Rekam Medis dicari lewat <see cref="PatientServices.GetPatientList"/> dengan
/// searchType = 2, Nama Pasien diisi dari <c>fullName</c>, lalu <c>patientId</c> hasil
/// pencarian dipakai apa adanya sebagai <c>patId</c> pada
/// <see cref="RujukanServices.FirstRujukanFromPatientId"/> untuk mengisi ComboBox
/// "Rujukan Pasien". Setiap item menyimpan objek rujukan utuh agar tahap berikutnya tidak
/// perlu memanggil API lagi.
/// </para>
/// <para>
/// Rujukan terpilih menentukan poli lewat <c>poliRujukan.kode</c> →
/// <see cref="ServiceUnitServices.ServiceUnitBridgings"/>; poli terpilih menentukan jadwal
/// dokter lewat <c>bpjsserviceunitid</c> →
/// <see cref="ParamedicServices.ParamedicScheduleBpjsQuota"/>; dokter terpilih mengisi
/// tooltip kuota. Setiap tahap membatalkan seluruh tahap di bawahnya sehingga data rujukan
/// sebelumnya tidak pernah tertinggal (§6). Verifikasi sidik jari dan cetak label masih
/// UI-only (§11).
/// </para>
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

    /// <summary>
    /// Naik setiap permintaan rujukan dimulai atau state pasien dibersihkan. Dipakai
    /// terpisah dari <see cref="_searchGeneration"/> supaya respons rujukan yang datang
    /// terlambat tidak pernah menimpa daftar rujukan pasien yang lebih baru.
    /// </summary>
    private int _rujukanGeneration;

    /// <summary>
    /// Naik setiap permintaan daftar poli dimulai atau poli dibersihkan. Respons poli yang
    /// datang terlambat dibuang agar tidak menimpa pilihan rujukan yang lebih baru.
    /// </summary>
    private int _poliGeneration;

    /// <summary>
    /// Naik setiap permintaan jadwal dokter dimulai atau dokter dibersihkan. Terpisah dari
    /// <see cref="_poliGeneration"/> karena poli bisa berganti saat jadwal dokter masih
    /// berjalan.
    /// </summary>
    private int _dokterGeneration;

    public RujukanBaruViewModel(AppServices services)
    {
        _services = services;
        IsLoggedIn = services.Session.IsLoggedIn;
    }

    /// <summary>Diisi MainWindowViewModel; tombol "Login Sekarang" pada overlay login (§10.3).</summary>
    public Action? RequestLogin { get; set; }

    [RelayCommand]
    private void RequestLoginNow() => RequestLogin?.Invoke();

    /// <summary>
    /// Status sesi. Overlay "Login Required" (§8.5, §10.3) tampil selama false, dan seluruh
    /// input/aksi halaman ikut terkunci sampai login berhasil.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CariPasienCommand))]
    private bool _isLoggedIn;

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
    [NotifyCanExecuteChangedFor(nameof(CariPasienCommand))]
    private bool _isBusy;

    /// <summary>
    /// Input yang bisa diedit petugas (No. RM, tombol cari, tombol aksi) hanya aktif saat
    /// sesi login valid dan tidak ada permintaan yang berjalan (§10.3).
    /// </summary>
    public bool IsInputEnabled => IsLoggedIn && !IsBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasStatus;

    [ObservableProperty]
    private bool _statusIsError;

    /// <summary>
    /// Rujukan milik pasien terpilih, hasil <c>GET /api/Rujukan/FirstRujukanFromPatientId</c>.
    /// Item membawa objek DTO lengkap (noKunjungan, tglKunjungan, provPerujuk, peserta,
    /// diagnosa, keluhan, poliRujukan, pelayanan) supaya tahap berikutnya tidak memanggil
    /// API lagi. Kosong selama pasien belum ditemukan / rujukan belum dimuat.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<RujukanOption> _rujukanOptions = [];

    /// <summary>Rujukan yang dipilih petugas; tidak pernah diisi otomatis.</summary>
    [ObservableProperty]
    private RujukanOption? _selectedRujukan;

    /// <summary>Teks tampilan rujukan terpilih (nomor kunjungan, tanggal, faskes perujuk).</summary>
    [ObservableProperty]
    private string _rujukanDisplay = string.Empty;

    /// <summary>True selama permintaan rujukan berjalan (di luar pencarian pasien).</summary>
    [ObservableProperty]
    private bool _isRujukanBusy;

    /// <summary>Pesan status khusus rujukan, terpisah dari banner pencarian pasien.</summary>
    [ObservableProperty]
    private string _rujukanStatusMessage = string.Empty;

    /// <summary>Banner danger rujukan: respons gagal atau <c>metaData.isValid = false</c>.</summary>
    [ObservableProperty]
    private bool _rujukanStatusIsError;

    /// <summary>Banner warning rujukan: permintaan sukses tetapi pasien tidak punya rujukan.</summary>
    [ObservableProperty]
    private bool _rujukanStatusIsEmpty;

    /// <summary>True bila ComboBox rujukan berisi data yang bisa dipilih.</summary>
    public bool HasRujukanOptions => RujukanOptions.Count > 0;

    /// <summary>
    /// Pemilihan rujukan (dan poli/dokter turunannya) hanya aktif saat sesi login valid,
    /// rujukan tersedia, dan tidak ada permintaan yang sedang berjalan (§6, §8, §10.3).
    /// </summary>
    public bool IsRujukanEnabled => IsLoggedIn && !IsBusy && !IsRujukanBusy && HasRujukanOptions;

    /// <summary>
    /// Poli tujuan untuk rujukan terpilih, hasil
    /// <c>GET /api/ServiceUnit/ServiceUnitBridgings?suId={poliRujukan.kode}</c>. Item
    /// membawa DTO <see cref="ServiceUnitBridging"/> utuh sehingga tahap dokter tidak perlu
    /// memanggil API lagi. Kosong selama rujukan belum dipilih / daftar belum dimuat.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<PoliOption> _poliOptions = [];

    /// <summary>Poli yang dipilih petugas; tidak pernah diisi otomatis.</summary>
    [ObservableProperty]
    private PoliOption? _selectedPoli;

    /// <summary>Teks tampilan poli terpilih (<c>unitname</c>).</summary>
    [ObservableProperty]
    private string _poliDisplay = string.Empty;

    /// <summary>True selama permintaan daftar poli berjalan.</summary>
    [ObservableProperty]
    private bool _isPoliBusy;

    /// <summary>Pesan status khusus poli, terpisah dari banner rujukan.</summary>
    [ObservableProperty]
    private string _poliStatusMessage = string.Empty;

    /// <summary>Banner danger poli: respons gagal atau kode poli rujukan tidak tersedia.</summary>
    [ObservableProperty]
    private bool _poliStatusIsError;

    /// <summary>Banner warning poli: permintaan sukses tetapi tidak ada unit layanan.</summary>
    [ObservableProperty]
    private bool _poliStatusIsEmpty;

    /// <summary>True bila ComboBox poli berisi data yang bisa dipilih.</summary>
    public bool HasPoliOptions => PoliOptions.Count > 0;

    /// <summary>
    /// ComboBox poli hanya aktif setelah login dan rujukan dipilih, saat daftar poli sudah
    /// terisi, dan saat tidak ada permintaan lain yang berjalan.
    /// </summary>
    public bool IsPoliEnabled =>
        IsLoggedIn && SelectedRujukan is not null && !IsBusy && !IsRujukanBusy && !IsPoliBusy && HasPoliOptions;

    /// <summary>
    /// Jadwal dokter untuk poli terpilih, hasil
    /// <c>GET /api/Paramedic/ParamedicScheduleBpjsQuota</c> dengan rentang satu hari
    /// (hari ini), <c>parId</c> kosong (semua dokter), dan <c>suId</c> =
    /// <c>bpjsserviceunitid</c> poli. Item membawa DTO
    /// <see cref="Models.Paramedic.ParamedicScheduleBpjsQuota"/> utuh sehingga kuota dokter
    /// terpilih bisa dibaca tanpa panggilan tambahan.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<DokterOption> _dokterOptions = [];

    /// <summary>Dokter yang dipilih petugas; tidak pernah diisi otomatis.</summary>
    [ObservableProperty]
    private DokterOption? _selectedDokter;

    /// <summary>Teks tampilan dokter terpilih (<c>paramedicname</c>).</summary>
    [ObservableProperty]
    private string _dokterDisplay = string.Empty;

    /// <summary>True selama permintaan jadwal dokter berjalan.</summary>
    [ObservableProperty]
    private bool _isDokterBusy;

    /// <summary>Pesan status khusus dokter, terpisah dari banner poli/rujukan.</summary>
    [ObservableProperty]
    private string _dokterStatusMessage = string.Empty;

    /// <summary>Banner danger dokter: respons gagal atau kode unit BPJS poli tidak tersedia.</summary>
    [ObservableProperty]
    private bool _dokterStatusIsError;

    /// <summary>Banner warning dokter: permintaan sukses tetapi tidak ada jadwal dokter.</summary>
    [ObservableProperty]
    private bool _dokterStatusIsEmpty;

    /// <summary>True bila ComboBox dokter berisi data yang bisa dipilih.</summary>
    public bool HasDokterOptions => DokterOptions.Count > 0;

    /// <summary>
    /// ComboBox dokter hanya aktif setelah login dan poli dipilih, saat jadwal sudah terisi,
    /// dan saat tidak ada permintaan lain yang berjalan.
    /// </summary>
    public bool IsDokterEnabled =>
        IsLoggedIn && SelectedPoli is not null && !IsBusy && !IsPoliBusy && !IsDokterBusy && HasDokterOptions;

    /// <summary>
    /// Total kuota (<c>quota</c>) jadwal dokter terpilih. Null selama dokter belum dipilih
    /// sehingga tooltip menampilkan "-" alih-alih nol yang menyesatkan.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KuotaTotalText))]
    private int? _kuotaTotal;

    /// <summary>Kuota terpakai (<c>quotaterpakai</c>) jadwal dokter terpilih.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KuotaTerpakaiText))]
    private int? _kuotaTerpakai;

    /// <summary>
    /// Kuota sisa (<c>quotasisa</c>) jadwal dokter terpilih. Nilai negatif dari API
    /// ditampilkan apa adanya (tidak di-clamp ke nol) supaya petugas melihat kelebihan kuota.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KuotaSisaText))]
    private int? _kuotaSisa;

    public string KuotaTotalText => KuotaTotal?.ToString() ?? "-";

    public string KuotaTerpakaiText => KuotaTerpakai?.ToString() ?? "-";

    public string KuotaSisaText => KuotaSisa?.ToString() ?? "-";

    /// <summary>True bila jadwal dokter terpilih membawa data kuota.</summary>
    public bool HasKuota => KuotaTotal is not null;

    /// <summary>Derived state ikut berubah setiap daftar rujukan diganti.</summary>
    partial void OnRujukanOptionsChanged(IReadOnlyList<RujukanOption> value)
    {
        OnPropertyChanged(nameof(HasRujukanOptions));
        OnPropertyChanged(nameof(IsRujukanEnabled));
    }

    /// <summary>Derived state ikut berubah setiap daftar poli diganti.</summary>
    partial void OnPoliOptionsChanged(IReadOnlyList<PoliOption> value)
    {
        OnPropertyChanged(nameof(HasPoliOptions));
        OnPropertyChanged(nameof(IsPoliEnabled));
    }

    /// <summary>Derived state ikut berubah setiap daftar dokter diganti.</summary>
    partial void OnDokterOptionsChanged(IReadOnlyList<DokterOption> value)
    {
        OnPropertyChanged(nameof(HasDokterOptions));
        OnPropertyChanged(nameof(IsDokterEnabled));
    }

    /// <summary>
    /// ComboBox rujukan nonaktif selama pencarian pasien / permintaan rujukan berjalan, dan
    /// input No. RM ikut terkunci pada kondisi yang sama.
    /// </summary>
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsInputEnabled));
        OnPropertyChanged(nameof(IsRujukanEnabled));
        OnPropertyChanged(nameof(IsPoliEnabled));
        OnPropertyChanged(nameof(IsDokterEnabled));
    }

    /// <summary>
    /// Login/logout mengubah seluruh derived state: overlay "Login Required" memakai
    /// <see cref="IsLoggedIn"/> langsung, sedangkan input dan pilihan rujukan/poli/dokter
    /// memakai flag turunan yang ikut dihitung ulang di sini (§10.3).
    /// </summary>
    partial void OnIsLoggedInChanged(bool value)
    {
        OnPropertyChanged(nameof(IsInputEnabled));
        OnPropertyChanged(nameof(IsRujukanEnabled));
        OnPropertyChanged(nameof(IsPoliEnabled));
        OnPropertyChanged(nameof(IsDokterEnabled));
    }

    /// <summary>Rujukan yang sedang dimuat juga menonaktifkan ComboBox poli.</summary>
    partial void OnIsRujukanBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRujukanEnabled));
        OnPropertyChanged(nameof(IsPoliEnabled));
    }

    /// <summary>ComboBox poli nonaktif selama daftar poli diambil.</summary>
    partial void OnIsPoliBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPoliEnabled));
        OnPropertyChanged(nameof(IsDokterEnabled));
    }

    /// <summary>ComboBox dokter nonaktif selama jadwal dokter diambil.</summary>
    partial void OnIsDokterBusyChanged(bool value) => OnPropertyChanged(nameof(IsDokterEnabled));

    /// <summary>Kuota hanya bermakna bila dokter terpilih, jadi HasKuota ikut berubah.</summary>
    partial void OnKuotaTotalChanged(int? value) => OnPropertyChanged(nameof(HasKuota));

    /// <summary>
    /// Teks tampilan hanya diturunkan dari item terpilih; objek DTO tetap utuh di item.
    /// Rujukan baru membatalkan poli, dokter, dan kuota milik rujukan sebelumnya lalu
    /// memuat daftar poli dari kode poli rujukan.
    /// </summary>
    partial void OnSelectedRujukanChanged(RujukanOption? value)
    {
        RujukanDisplay = value?.Display ?? string.Empty;
        OnPropertyChanged(nameof(IsPoliEnabled));

        // Seluruh rantai di bawah rujukan dibuang lebih dulu: poli milik rujukan lama tidak
        // boleh sempat terlihat bersama rujukan baru (§6).
        ClearPoli();

        if (value is not null)
            _ = LoadPoliAsync(value);
    }

    /// <summary>
    /// Poli baru membatalkan dokter dan kuota milik poli sebelumnya lalu memuat jadwal
    /// dokter. Jadwal tidak pernah dimuat sebelum poli dipilih karena <c>suId</c> endpoint
    /// Paramedic berasal dari unit poli terpilih.
    /// </summary>
    partial void OnSelectedPoliChanged(PoliOption? value)
    {
        PoliDisplay = value?.Display ?? string.Empty;
        OnPropertyChanged(nameof(IsDokterEnabled));

        ClearDokter();

        if (value is not null)
            _ = LoadDokterAsync(value);
    }

    /// <summary>Kuota diturunkan dari jadwal dokter terpilih; tanpa dokter, kuota dikosongkan.</summary>
    partial void OnSelectedDokterChanged(DokterOption? value)
    {
        DokterDisplay = value?.Display ?? string.Empty;

        KuotaTotal = value?.Schedule.quota;
        KuotaTerpakai = value?.Schedule.quotaterpakai;
        KuotaSisa = value?.Schedule.quotasisa;
    }

    /// <summary>
    /// Setiap perubahan No. RM membatalkan pasien yang sedang terpilih: nama, patientId,
    /// medicalNo, daftar rujukan, dan pilihannya dibuang sehingga pencarian ulang wajib
    /// dilakukan (§6).
    /// </summary>
    partial void OnNomorRekamMedisChanged(string value)
    {
        _searchGeneration++;
        ClearPatient();
        SetStatus(string.Empty, false);
    }

    /// <summary>
    /// Status sesi dipakai overlay "Login Required" (§10.3). Logout membuang state pasien
    /// agar tidak terbawa ke sesi berikutnya; generation counter yang dinaikkan di sini
    /// membuat respons API yang masih berjalan tidak pernah menimpa state sesi baru.
    /// </summary>
    public void OnSessionChanged(bool loggedIn)
    {
        IsLoggedIn = loggedIn;

        if (loggedIn)
            return;

        NomorRekamMedis = string.Empty;
        ClearPatient();
        SetStatus(string.Empty, false);
    }

    /// <summary>
    /// Pencarian hanya boleh berjalan saat sesi login valid dan tidak ada pencarian lain
    /// (§10.3). CanExecute menutup tombol cari sekaligus KeyBinding Enter pada TextBox No. RM.
    /// </summary>
    private bool CanCariPasien() => IsLoggedIn && !IsBusy;

    /// <summary>
    /// Mencari pasien berdasarkan No. Rekam Medis (searchType = 2). Input kosong
    /// ditolak sebelum memanggil API; hasil lama dibuang lebih dulu agar data
    /// pasien sebelumnya tidak pernah tampil bersama pesan baru.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCariPasien))]
    private async Task CariPasienAsync()
    {
        // Satu pencarian pada satu waktu; tombol dan input juga nonaktif saat IsBusy.
        if (IsBusy)
            return;

        // Overlay "Login Required" aktif: pencarian tidak boleh berjalan, termasuk lewat
        // KeyBinding Enter pada TextBox No. RM (§10.3).
        if (!IsLoggedIn)
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

            // Daftar rujukan diambil memakai patientId (bukan No. RM/BPJS/NIK) dan tetap
            // memakai generasi pencarian yang sama supaya responsnya tidak menimpa pasien lain.
            // IsBusy dilepas lebih dulu: pencarian pasien selesai, pengambilan rujukan punya
            // indikator sendiri sehingga field No. RM dan tombol cari tetap responsif.
            IsBusy = false;
            await LoadRujukanAsync(patient.patientId, generation);
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

    /// <summary>
    /// Mengambil daftar rujukan milik <paramref name="patientId"/> lalu mengisi ComboBox
    /// "Rujukan Pasien". Dua tingkat validasi: pembungkus <c>succeeded</c> dan
    /// <c>data.metaData.isValid</c>; keduanya harus benar sebelum <c>response.rujukan</c>
    /// dianggap berisi data. Kegagalan di sini tidak pernah membuang identitas pasien —
    /// hanya daftar rujukan dan pilihannya yang dibersihkan.
    /// </summary>
    private async Task LoadRujukanAsync(string patientId, int generation)
    {
        ClearRujukan();

        if (string.IsNullOrWhiteSpace(patientId))
        {
            SetRujukanStatus("patientId pasien tidak tersedia sehingga rujukan tidak dapat dimuat.", error: true);
            return;
        }

        var rujukanGeneration = ++_rujukanGeneration;
        IsRujukanBusy = true;

        try
        {
            var result = await Task.Run(
                () => _services.Rujukan.FirstRujukanFromPatientId(patientId));

            // Respons usang: No. RM berubah, pencarian lain dimulai, atau logout.
            if (generation != _searchGeneration || rujukanGeneration != _rujukanGeneration)
                return;

            if (!result.succeeded)
            {
                ClearRujukan();
                SetRujukanStatus(DescribeRujukanFailure(result.errors, result.message), error: true);
                return;
            }

            var data = result.data;
            if (data?.metaData?.isValid != true)
            {
                ClearRujukan();
                SetRujukanStatus(
                    DescribeRujukanFailure(metaData: data?.metaData?.message), error: true);
                return;
            }

            var referrals = data.response?.rujukan?
                .OrderBy(r => r.tglKunjungan)
                .Select(r => new RujukanOption(r))
                .ToList() ?? [];

            if (referrals.Count == 0)
            {
                ClearRujukan();
                SetRujukanStatus(
                    $"patientId {patientId} tidak memiliki rujukan. Pasien tetap tersimpan; " +
                    "lanjutkan pendaftaran tanpa rujukan atau hubungi petugas rujukan.",
                    empty: true);
                return;
            }

            RujukanOptions = referrals;
            SetStatus($"Pasien ditemukan: {NamaPasien}. {referrals.Count} rujukan tersedia.", false);
        }
        catch (Exception e)
        {
            if (generation != _searchGeneration || rujukanGeneration != _rujukanGeneration)
                return;

            ClearRujukan();
            SetRujukanStatus($"Pemuatan rujukan gagal: {e.Message}", error: true);
        }
        finally
        {
            // Hanya permintaan terbaru yang boleh memadamkan indikator: respons usang tidak
            // boleh mematikan busy milik permintaan yang lebih baru.
            if (rujukanGeneration == _rujukanGeneration)
                IsRujukanBusy = false;
        }
    }
    /// <summary>
    /// Mengambil unit layanan untuk <c>poliRujukan.kode</c> rujukan terpilih lalu mengisi
    /// ComboBox "Poli". Kode poli BPJS dikirim apa adanya sebagai <c>suId</c> karena server
    /// mencocokkan <c>sub.BridgingId == suId</c> — bukan ID unit Avicenna. Kegagalan di sini
    /// tidak pernah membuang rujukan/pasien; hanya poli, dokter, dan kuota yang dibersihkan.
    /// </summary>
    private async Task LoadPoliAsync(RujukanOption option)
    {
        var kode = option.Referral.poliRujukan?.kode?.Trim() ?? string.Empty;

        var generation = ++_poliGeneration;
        SetPoliStatus(string.Empty);
        PoliOptions = [];

        if (kode.Length == 0)
        {
            SetPoliStatus(
                "Rujukan terpilih tidak memuat kode poli, sehingga daftar poli tidak dapat dimuat.",
                error: true);
            return;
        }

        IsPoliBusy = true;

        try
        {
            var result = await Task.Run(
                () => _services.ServiceUnit.ServiceUnitBridgings(kode));

            // Respons usang: rujukan sudah berganti atau poli sudah dibersihkan.
            if (generation != _poliGeneration)
                return;

            if (!result.succeeded)
            {
                PoliOptions = [];

                // 404 "Data Not Found" berarti tidak ada unit untuk kode tersebut, bukan
                // kegagalan teknis, jadi ditampilkan sebagai data kosong (warning).
                if (IsDataNotFound(result.errors))
                    SetPoliStatus(DescribeEmptyPoli(kode), empty: true);
                else
                    SetPoliStatus(DescribePoliFailure(result.errors, result.message), error: true);

                return;
            }

            var units = result.data?
                .Select(unit => new PoliOption(unit))
                .ToList() ?? [];

            if (units.Count == 0)
            {
                PoliOptions = [];
                SetPoliStatus(DescribeEmptyPoli(kode), empty: true);
                return;
            }

            PoliOptions = units;
        }
        catch (Exception e)
        {
            if (generation != _poliGeneration)
                return;

            PoliOptions = [];
            SetPoliStatus($"Pemuatan daftar poli gagal: {e.Message}", error: true);
        }
        finally
        {
            // Hanya permintaan terbaru yang boleh memadamkan indikator.
            if (generation == _poliGeneration)
                IsPoliBusy = false;
        }
    }



    /// <summary>
    /// Membuang daftar rujukan dan pilihannya. Dipanggil setiap pasien diganti/batal
    /// ditemukan dan setiap permintaan rujukan dimulai ulang, sehingga rujukan milik
    /// <summary>
    /// Mengambil jadwal dokter untuk poli terpilih lalu mengisi ComboBox "Dokter".
    /// <c>suId</c> memakai <c>bpjsserviceunitid</c> (kode unit BPJS) karena server
    /// mencocokkan <c>psb.BpjsSubSpecialityCode == suId</c>. Rentang tanggal satu hari
    /// (hari ini) dan <c>parId</c> kosong: server hanya memfilter paramedis bila
    /// <c>parId</c> tidak kosong, jadi kosong berarti semua dokter pada poli tersebut.
    /// </summary>
    private async Task LoadDokterAsync(PoliOption option)
    {
        var suId = option.Unit.bpjsserviceunitid?.Trim() ?? string.Empty;

        var generation = ++_dokterGeneration;
        SetDokterStatus(string.Empty);
        DokterOptions = [];

        if (suId.Length == 0)
        {
            SetDokterStatus(
                $"Poli {option.Display} tidak memiliki kode unit BPJS, sehingga jadwal dokter " +
                "tidak dapat dimuat.",
                error: true);
            return;
        }

        // Satu hari: jadwal hari ini, sejalan dengan tanggal yang dipakai pencetakan SEP/label.
        var tanggal = DateTime.Today;

        IsDokterBusy = true;

        try
        {
            var result = await Task.Run(
                () => _services.Paramedic.ParamedicScheduleBpjsQuota(
                    tanggal, tanggal, string.Empty, suId));

            // Respons usang: poli sudah berganti atau dokter sudah dibersihkan.
            if (generation != _dokterGeneration)
                return;

            if (!result.succeeded)
            {
                DokterOptions = [];

                // 404 "Data Not Found" = tidak ada jadwal pada tanggal itu (warning), bukan
                // kegagalan teknis.
                if (IsDataNotFound(result.errors))
                    SetDokterStatus(DescribeEmptyDokter(option.Display, tanggal), empty: true);
                else
                    SetDokterStatus(DescribeDokterFailure(result.errors, result.message), error: true);

                return;
            }

            var schedules = result.data?
                .Select(schedule => new DokterOption(schedule))
                .ToList() ?? [];

            if (schedules.Count == 0)
            {
                DokterOptions = [];
                SetDokterStatus(DescribeEmptyDokter(option.Display, tanggal), empty: true);
                return;
            }

            DokterOptions = schedules;
        }
        catch (Exception e)
        {
            if (generation != _dokterGeneration)
                return;

            DokterOptions = [];
            SetDokterStatus($"Pemuatan jadwal dokter gagal: {e.Message}", error: true);
        }
        finally
        {
            if (generation == _dokterGeneration)
                IsDokterBusy = false;
        }
    }


    /// pasien sebelumnya tidak pernah terlihat (§6).
    /// </summary>
    private void ClearRujukan()
    {
        _rujukanGeneration++;
        IsRujukanBusy = false;
        RujukanOptions = [];
        SelectedRujukan = null;
        RujukanDisplay = string.Empty;
        SetRujukanStatus(string.Empty, error: false);

        // Rantai di bawah rujukan selalu ikut dibuang, termasuk saat SelectedRujukan sudah
        // null sehingga handler perubahannya tidak menyala.
        ClearPoli();
    }

    /// <summary>
    /// Membuang daftar poli beserta seluruh turunannya (dokter dan kuota). Dipanggil setiap
    /// rujukan diganti/dibersihkan dan setiap permintaan poli dimulai ulang sehingga poli
    /// milik rujukan sebelumnya tidak pernah terlihat (§6).
    /// </summary>
    private void ClearPoli()
    {
        _poliGeneration++;
        IsPoliBusy = false;
        PoliOptions = [];
        SelectedPoli = null;
        PoliDisplay = string.Empty;
        SetPoliStatus(string.Empty);
    }

    /// <summary>
    /// Membuang daftar dokter dan kuotanya. Dipanggil setiap poli diganti/dibersihkan dan
    /// setiap permintaan jadwal dimulai ulang.
    /// </summary>
    private void ClearDokter()
    {
        _dokterGeneration++;
        IsDokterBusy = false;
        DokterOptions = [];
        SelectedDokter = null;
        DokterDisplay = string.Empty;
        SetDokterStatus(string.Empty);
    }

    private void ClearPatient()
    {
        NamaPasien = string.Empty;
        PatientId = string.Empty;
        MedicalNo = string.Empty;
        ClearRujukan();
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
    /// Pesan kegagalan rujukan. Detail dipilih berurutan: <c>errors[]</c> API,
    /// <c>metaData.message</c> (respons pembungkus sukses tetapi metadata menyatakan
    /// tidak valid), lalu <c>message</c> pembungkus.
    /// </summary>
    private static string DescribeRujukanFailure(
        string[]? errors = null, string? message = null, string? metaData = null)
    {
        var detail = errors is { Length: > 0 }
            ? string.Join(", ", errors.Where(e => !string.IsNullOrWhiteSpace(e)))
            : string.Empty;

        if (detail.Length == 0 && !string.IsNullOrWhiteSpace(metaData))
            detail = metaData.Trim();

        if (detail.Length > 0)
            return $"Rujukan pasien gagal dimuat: {detail}";

        return !string.IsNullOrWhiteSpace(message)
            ? $"Rujukan pasien gagal dimuat: {message}"
            : "Rujukan pasien gagal dimuat. Silakan coba lagi.";
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

    /// <summary>
    /// Status khusus rujukan. <paramref name="error"/> memakai banner danger (respons gagal /
    /// metadata tidak valid), <paramref name="empty"/> memakai banner warning (tidak ada
    /// rujukan). Pesan kosong menyembunyikan keduanya.
    /// </summary>
    private void SetRujukanStatus(string message, bool error = false, bool empty = false)
    {
        RujukanStatusMessage = message;
        RujukanStatusIsError = error;
        RujukanStatusIsEmpty = empty;
    }

    /// <summary>
    /// Status khusus poli. <paramref name="error"/> memakai banner danger (respons gagal /
    /// kode poli tidak tersedia), <paramref name="empty"/> memakai banner warning (tidak ada
    /// unit layanan). Pesan kosong menyembunyikan keduanya.
    /// </summary>
    private void SetPoliStatus(string message, bool error = false, bool empty = false)
    {
        PoliStatusMessage = message;
        PoliStatusIsError = error;
        PoliStatusIsEmpty = empty;
    }

    /// <summary>
    /// Status khusus dokter. <paramref name="error"/> memakai banner danger (respons gagal /
    /// kode unit BPJS tidak tersedia), <paramref name="empty"/> memakai banner warning
    /// (tidak ada jadwal pada tanggal yang diminta). Pesan kosong menyembunyikan keduanya.
    /// </summary>
    private void SetDokterStatus(string message, bool error = false, bool empty = false)
    {
        DokterStatusMessage = message;
        DokterStatusIsError = error;
        DokterStatusIsEmpty = empty;
    }

    /// <summary>
    /// Server mengembalikan 404 + <c>errors = ["Data Not Found"]</c> saat daftar kosong.
    /// Kasus itu bukan kegagalan teknis, jadi diperlakukan sebagai data kosong (warning).
    /// </summary>
    private static bool IsDataNotFound(string[]? errors) =>
        errors is { Length: 1 } &&
        string.Equals(errors[0]?.Trim(), "Data Not Found", StringComparison.OrdinalIgnoreCase);

    private static string DescribeEmptyPoli(string kode) =>
        $"Kode poli {kode} tidak memiliki unit layanan terdaftar. " +
        "Periksa bridging unit poli di aplikasi Avicenna.";

    private static string DescribeEmptyDokter(string poli, DateTime tanggal) =>
        $"Tidak ada jadwal dokter untuk poli {poli} pada " +
        $"{Converter.DateFormat(tanggal, DateFormat.Datelong)}.";

    /// <summary>
    /// Pesan kegagalan daftar poli. Detail dipilih berurutan: <c>errors[]</c> API lalu
    /// <c>message</c> pembungkus.
    /// </summary>
    private static string DescribePoliFailure(string[]? errors, string? message)
    {
        var detail = errors is { Length: > 0 }
            ? string.Join(", ", errors.Where(e => !string.IsNullOrWhiteSpace(e)))
            : string.Empty;

        if (detail.Length > 0)
            return $"Daftar poli gagal dimuat: {detail}";

        return !string.IsNullOrWhiteSpace(message)
            ? $"Daftar poli gagal dimuat: {message}"
            : "Daftar poli gagal dimuat. Silakan coba lagi.";
    }

    /// <summary>
    /// Pesan kegagalan jadwal dokter. Detail dipilih berurutan: <c>errors[]</c> API lalu
    /// <c>message</c> pembungkus.
    /// </summary>
    private static string DescribeDokterFailure(string[]? errors, string? message)
    {
        var detail = errors is { Length: > 0 }
            ? string.Join(", ", errors.Where(e => !string.IsNullOrWhiteSpace(e)))
            : string.Empty;

        if (detail.Length > 0)
            return $"Jadwal dokter gagal dimuat: {detail}";

        return !string.IsNullOrWhiteSpace(message)
            ? $"Jadwal dokter gagal dimuat: {message}"
            : "Jadwal dokter gagal dimuat. Silakan coba lagi.";
    }

    /// <summary>
    /// Item ComboBox "Rujukan Pasien". Menyimpan objek rujukan asli secara utuh
    /// (noKunjungan, tglKunjungan, provPerujuk, peserta, diagnosa, keluhan, poliRujukan,
    /// pelayanan) sehingga tahap poli/dokter/kuota tidak perlu memanggil API lagi; DTO
    /// tidak diubah, teks tampilan hanya ditambahkan di sini.
    /// </summary>
    public sealed class RujukanOption(FirstRujukanFromPatientId.Rujukan referral)
    {
        public FirstRujukanFromPatientId.Rujukan Referral { get; } = referral;

        /// <summary>Contoh: "090101000120Y005992 - 31 Jan 2020 (Puskesmas Gambir)".</summary>
        public string Display { get; } = Describe(referral);

        public override string ToString() => Display;

        private static string Describe(FirstRujukanFromPatientId.Rujukan referral)
        {
            var noKunjungan = referral.noKunjungan?.Trim() ?? string.Empty;
            var faskes = referral.provPerujuk?.nama?.Trim() ?? string.Empty;

            // Tanggal diformat lewat Converter.DateFormat yang sudah dipakai seluruh aplikasi;
            // tglKunjungan non-nullable dengan default DateTime.MinValue, jadi nilai default
            // dianggap kosong dan tidak pernah diganti DateTime.Now.
            var tanggal = referral.tglKunjungan == DateTime.MinValue
                ? string.Empty
                : Converter.DateFormat(referral.tglKunjungan, DateFormat.Datelong);

            var detail = tanggal.Length > 0 && faskes.Length > 0
                ? $"{tanggal} ({faskes})"
                : tanggal.Length > 0
                    ? tanggal
                    : faskes.Length > 0 ? $"({faskes})" : string.Empty;

            if (noKunjungan.Length > 0 && detail.Length > 0)
                return $"{noKunjungan} - {detail}";

            if (noKunjungan.Length > 0)
                return noKunjungan;

            return detail.Length > 0 ? detail : "Rujukan tanpa nomor kunjungan";
        }
    }

    /// <summary>
    /// Item ComboBox "Poli". Menyimpan DTO <see cref="ServiceUnitBridging"/> utuh
    /// (<c>serviceunitid</c>, <c>bpjsserviceunitid</c>, <c>unitname</c>) supaya tahap jadwal
    /// dokter bisa memakai <c>bpjsserviceunitid</c> tanpa memanggil API lagi; DTO tidak
    /// diubah, teks tampilan hanya ditambahkan di sini.
    /// </summary>
    public sealed class PoliOption(ServiceUnitBridging unit)
    {
        public ServiceUnitBridging Unit { get; } = unit;

        /// <summary>
        /// <c>unitname</c> dipakai lebih dulu karena itu nama unit yang dipakai internal;
        /// nama Avicenna/BPJS hanya fallback bila unit belum punya nama.
        /// </summary>
        public string Display { get; } = Describe(unit);

        public override string ToString() => Display;

        private static string Describe(ServiceUnitBridging unit)
        {
            var unitName = unit.unitname?.Trim() ?? string.Empty;
            if (unitName.Length > 0)
                return unitName;

            var serviceUnitName = unit.serviceunitname?.Trim() ?? string.Empty;
            if (serviceUnitName.Length > 0)
                return serviceUnitName;

            var bpjsName = unit.bpjsserviceunitname?.Trim() ?? string.Empty;
            if (bpjsName.Length > 0)
                return bpjsName;

            // Kode BPJS dipakai sebagai identitas terakhir supaya item tidak tampil kosong.
            var kode = unit.bpjsserviceunitid?.Trim() ?? string.Empty;
            return kode.Length > 0 ? kode : "Poli tanpa nama";
        }
    }

    /// <summary>
    /// Item ComboBox "Dokter". Menyimpan jadwal BPJS utuh sehingga tooltip kuota bisa dibaca
    /// langsung dari item terpilih tanpa panggilan tambahan. Server sudah mengelompokkan
    /// jadwal per tanggal + dokter + poli, jadi satu item mewakili satu dokter pada tanggal
    /// yang diminta; urutan respons API dipertahankan.
    /// </summary>
    public sealed class DokterOption(Models.Paramedic.ParamedicScheduleBpjsQuota schedule)
    {
        public Models.Paramedic.ParamedicScheduleBpjsQuota Schedule { get; } = schedule;

        /// <summary>Contoh: "dr. Martua Rizal Situmorang, Sp.N., M.Kes., FINA".</summary>
        public string Display { get; } = Describe(schedule);

        public override string ToString() => Display;

        private static string Describe(Models.Paramedic.ParamedicScheduleBpjsQuota schedule)
        {
            var name = schedule.paramedicname?.Trim() ?? string.Empty;
            if (name.Length > 0)
                return name;

            // Tanpa nama, kode paramedis BPJS masih lebih informatif daripada item kosong.
            var bpjsId = schedule.bpjsparamedicid?.Trim() ?? string.Empty;
            return bpjsId.Length > 0 ? bpjsId : "Dokter tanpa nama";
        }
    }
}
