using System;

namespace AvaloniaFingerprint.Models;

/// <summary>
/// A row in the "Daftar Fingerprint" tab: one fingerprint registration that was
/// submitted from this workstation.
/// </summary>
public class FingerprintRecord
{
    public string NoKapst { get; set; } = string.Empty;
    public string Nama { get; set; } = string.Empty;
    public DateTime Tanggal { get; set; } = DateTime.Now;
    public int JumlahJari { get; set; }
    public string Petugas { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    /// <summary>Base64 JPEG of the right index finger, used as the row thumbnail.</summary>
    public string? ImgRight { get; set; }

    /// <summary>Base64 JPEG of the left index finger.</summary>
    public string? ImgLeft { get; set; }
}
