namespace AvaloniaFingerprint.Models;

public class Verify
{
    public string keyword { get; set; } = string.Empty;
    public string jnsFilter { get; set; } = string.Empty;
    public string finger { get; set; } = string.Empty;

    public class Asuransi
    {
        public object? kdAsuransi { get; set; }
        public object? nmAsuransi { get; set; }
        public object? noAsuransi { get; set; }
        public bool cob { get; set; }
    }

    public class BadanUsaha
    {
        public string? nama { get; set; }
        public string? kode { get; set; }
    }

    public class CabangTerbit
    {
        public string? kdKC { get; set; }
        public object? nmKC { get; set; }
        public object? alamat { get; set; }
        public object? telp { get; set; }
        public object? fax { get; set; }
        public object? dati { get; set; }
        public object? kdKR { get; set; }
    }

    public class Golongan
    {
        public object? nama { get; set; }
        public object? kode { get; set; }
    }

    public class JnsKelas
    {
        public string? nama { get; set; }
        public string? kode { get; set; }
    }

    public class JnsPeserta
    {
        public string? nama { get; set; }
        public string? kode { get; set; }
    }

    public class JnsPesertaDetail
    {
        public string? nama { get; set; }
        public string? kode { get; set; }
    }

    public class KdPpkGigi
    {
        public object? kdPPK { get; set; }
        public object? nmPPK { get; set; }
        public object? alamat { get; set; }
        public object? telp { get; set; }
        public object? kc { get; set; }
    }

    public class KdPpkPst
    {
        public string? kdPPK { get; set; }
        public string? nmPPK { get; set; }
        public object? alamat { get; set; }
        public object? telp { get; set; }
        public object? kc { get; set; }
    }

    public class MetaData
    {
        public string? message { get; set; }
        public int code { get; set; }
    }

    public class Response
    {
        public string? noKK { get; set; }
        public KdPpkPst? kdPpkPst { get; set; }
        public KdPpkGigi? kdPpkGigi { get; set; }
        public Golongan? golongan { get; set; }
        public JnsKelas? jnsKelas { get; set; }
        public JnsPeserta? jnsPeserta { get; set; }
        public JnsPesertaDetail? jnsPesertaDetail { get; set; }
        public BadanUsaha? badanUsaha { get; set; }
        public string? nipBaru { get; set; }
        public object? nipLama { get; set; }
        public string? kdJenisPesertaUtama { get; set; }
        public string? golDarah { get; set; }
        public string? hubunganKel { get; set; }
        public StatusKawin? statusKawin { get; set; }
        public StatusAktif? statusAktif { get; set; }
        public string? noHP { get; set; }
        public CabangTerbit? cabangTerbit { get; set; }
        public string? noKTP { get; set; }
        public string? nik { get; set; }
        public int flagTanggungan { get; set; }
        public bool prolanisF { get; set; }
        public Asuransi? asuransi { get; set; }
        public int usia { get; set; }
        public string? email { get; set; }
        public string? nama { get; set; }
        public string? noKartu { get; set; }
        public object? sktm { get; set; }
        public object? tglCetak { get; set; }
        public bool pria { get; set; }
        public bool wanita { get; set; }
        public bool pesertaBPJS { get; set; }
        public bool pesertaCOB { get; set; }
        public string? tglAkhirBerlaku { get; set; }
        public string? pisa { get; set; }
        public string? tglMulaiAktif { get; set; }
        public string? tglLahir { get; set; }
        public string? alamat { get; set; }
        public string? sex { get; set; }
    }

    public class VerifyRoot
    {
        public Response? response { get; set; }
        public MetaData? metaData { get; set; }
    }

    public class StatusAktif
    {
        public string? nama { get; set; }
        public string? kode { get; set; }
    }

    public class StatusKawin
    {
        public string? nama { get; set; }
        public string? kode { get; set; }
    }
}
