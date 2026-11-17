using System.Collections.Generic;

namespace AvaloniaFingerprint.ViewModels;

/// <summary>
/// Halaman "Rujukan Baru" (§8.7) — tahap ini UI-only (lihat §11): hanya menampung
/// data contoh agar tata letak dan binding XAML bisa divalidasi. Tidak ada service,
/// command, maupun panggilan API / fingerprint / registrasi / printing di sini;
/// seluruh alur bisnis ditambahkan pada tahap integrasi berikutnya.
/// </summary>
public sealed class RujukanBaruViewModel : ViewModelBase
{
    /// <summary>Nomor rekam medis contoh (hasil pencarian pasien).</summary>
    public string NomorRekamMedis { get; } = "00-12-34-56";

    /// <summary>Nama pasien contoh; ditampilkan sebagai nilai read-only.</summary>
    public string NamaPasien { get; } = "Ahmad Fauzi";

    public IReadOnlyList<string> RujukanOptions { get; } =
    [
        "090101000120Y005992 - 31 Jan 2020 (Puskesmas Gambir)"
    ];

    public IReadOnlyList<string> PoliOptions { get; } = ["PARU"];

    public IReadOnlyList<string> DokterOptions { get; } =
    [
        "dr. Martua Rizal Situmorang, Sp.N., M.Kes., FINA"
    ];
}
