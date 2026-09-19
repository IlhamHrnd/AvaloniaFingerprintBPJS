using Newtonsoft.Json;

namespace AvaloniaFingerprint.Models.Fingerprint
{
    public class Verify
    {
        public class Asuransi
        {
            [JsonProperty("kdAsuransi")]
            public object? kdAsuransi { get; set; }

            [JsonProperty("nmAsuransi")]
            public object? nmAsuransi { get; set; }

            [JsonProperty("noAsuransi")]
            public object? noAsuransi { get; set; }

            [JsonProperty("cob")]
            public bool? cob { get; set; } = false;
        }

        public class BadanUsaha
        {
            [JsonProperty("nama")]
            public string nama { get; set; } = string.Empty;

            [JsonProperty("kode")]
            public string kode { get; set; } = string.Empty;
        }

        public class CabangTerbit
        {
            [JsonProperty("kdKC")]
            public string kdKC { get; set; } = string.Empty;

            [JsonProperty("nmKC")]
            public object? nmKC { get; set; }

            [JsonProperty("alamat")]
            public object? alamat { get; set; }

            [JsonProperty("telp")]
            public object? telp { get; set; }

            [JsonProperty("fax")]
            public object? fax { get; set; }

            [JsonProperty("dati")]
            public object? dati { get; set; }

            [JsonProperty("kdKR")]
            public object? kdKR { get; set; }
        }

        public class Golongan
        {
            [JsonProperty("nama")]
            public string nama { get; set; } = string.Empty;

            [JsonProperty("kode")]
            public string kode { get; set; } = string.Empty;
        }

        public class JnsKelas
        {
            [JsonProperty("nama")]
            public string nama { get; set; } = string.Empty;

            [JsonProperty("kode")]
            public string kode { get; set; } = string.Empty;
        }

        public class JnsPeserta
        {
            [JsonProperty("nama")]
            public string nama { get; set; } = string.Empty;

            [JsonProperty("kode")]
            public string kode { get; set; } = string.Empty;
        }

        public class JnsPesertaDetail
        {
            [JsonProperty("nama")]
            public string nama { get; set; } = string.Empty;

            [JsonProperty("kode")]
            public string kode { get; set; } = string.Empty;
        }

        public class KdPpkGigi
        {
            [JsonProperty("kdPPK")]
            public object? kdPPK { get; set; }

            [JsonProperty("nmPPK")]
            public object? nmPPK { get; set; }

            [JsonProperty("alamat")]
            public object? alamat { get; set; }

            [JsonProperty("telp")]
            public object? telp { get; set; }

            [JsonProperty("kc")]
            public object? kc { get; set; }
        }

        public class KdPpkPst
        {
            [JsonProperty("kdPPK")]
            public string kdPPK { get; set; } = string.Empty;

            [JsonProperty("nmPPK")]
            public string nmPPK { get; set; } = string.Empty;

            [JsonProperty("alamat")]
            public object? alamat { get; set; }

            [JsonProperty("telp")]
            public object? telp { get; set; }

            [JsonProperty("kc")]
            public object? kc { get; set; }
        }

        public class MetaData
        {
            [JsonProperty("message")]
            public string message { get; set; } = string.Empty;

            [JsonProperty("code")]
            public int? code { get; set; } = 0;

            [JsonProperty("isValid")]
            public bool? isValid { get; set; } = false;
        }

        public class Response
        {
            [JsonProperty("noKK")]
            public string noKK { get; set; } = string.Empty;

            [JsonProperty("kdPpkPst")]
            public KdPpkPst kdPpkPst { get; set; } = new();

            [JsonProperty("kdPpkGigi")]
            public KdPpkGigi kdPpkGigi { get; set; } = new();

            [JsonProperty("golongan")]
            public Golongan golongan { get; set; } = new();

            [JsonProperty("jnsKelas")]
            public JnsKelas jnsKelas { get; set; } = new();

            [JsonProperty("jnsPeserta")]
            public JnsPeserta jnsPeserta { get; set; } = new();

            [JsonProperty("jnsPesertaDetail")]
            public JnsPesertaDetail jnsPesertaDetail { get; set; } = new();

            [JsonProperty("badanUsaha")]
            public BadanUsaha badanUsaha { get; set; } = new();

            [JsonProperty("nipBaru")]
            public string nipBaru { get; set; } = string.Empty;

            [JsonProperty("nipLama")]
            public string nipLama { get; set; } = string.Empty;

            [JsonProperty("kdJenisPesertaUtama")]
            public string kdJenisPesertaUtama { get; set; } = string.Empty;

            [JsonProperty("golDarah")]
            public string golDarah { get; set; } = string.Empty;

            [JsonProperty("hubunganKel")]
            public string hubunganKel { get; set; } = string.Empty;

            [JsonProperty("statusKawin")]
            public StatusKawin statusKawin { get; set; } = new();

            [JsonProperty("statusAktif")]
            public StatusAktif statusAktif { get; set; } = new();

            [JsonProperty("noHP")]
            public string noHP { get; set; } = string.Empty;

            [JsonProperty("cabangTerbit")]
            public CabangTerbit cabangTerbit { get; set; } = new();

            [JsonProperty("noKTP")]
            public string noKTP { get; set; } = string.Empty;

            [JsonProperty("nik")]
            public string nik { get; set; } = string.Empty;

            [JsonProperty("flagTanggungan")]
            public int? flagTanggungan { get; set; } = 0;

            [JsonProperty("prolanisF")]
            public bool? prolanisF { get; set; } = false;

            [JsonProperty("asuransi")]
            public Asuransi asuransi { get; set; } = new();

            [JsonProperty("usia")]
            public int? usia { get; set; } = 0;

            [JsonProperty("email")]
            public string email { get; set; } = string.Empty;

            [JsonProperty("nama")]
            public string nama { get; set; } = string.Empty;

            [JsonProperty("noKartu")]
            public string noKartu { get; set; } = string.Empty;

            [JsonProperty("sktm")]
            public object? sktm { get; set; }

            [JsonProperty("tglCetak")]
            public object? tglCetak { get; set; }

            [JsonProperty("pria")]
            public bool? pria { get; set; } = false;

            [JsonProperty("wanita")]
            public bool? wanita { get; set; } = false;

            [JsonProperty("pesertaBPJS")]
            public bool? pesertaBPJS { get; set; } = false;

            [JsonProperty("pesertaCOB")]
            public bool? pesertaCOB { get; set; } = false;

            [JsonProperty("tglAkhirBerlaku")]
            public string tglAkhirBerlaku { get; set; } = string.Empty;

            [JsonProperty("pisa")]
            public string pisa { get; set; } = string.Empty;

            [JsonProperty("tglMulaiAktif")]
            public string tglMulaiAktif { get; set; } = string.Empty;

            [JsonProperty("tglLahir")]
            public string tglLahir { get; set; } = string.Empty;

            [JsonProperty("alamat")]
            public string alamat { get; set; } = string.Empty;

            [JsonProperty("sex")]
            public string sex { get; set; } = string.Empty;
        }

        public class VerifyRoot
        {
            [JsonProperty("response")]
            public Response response { get; set; } = new();

            [JsonProperty("metaData")]
            public MetaData metaData { get; set; } = new();
        }

        public class StatusAktif
        {
            [JsonProperty("nama")]
            public string nama { get; set; } = string.Empty;

            [JsonProperty("kode")]
            public string kode { get; set; } = string.Empty;
        }

        public class StatusKawin
        {
            [JsonProperty("nama")]
            public string nama { get; set; } = string.Empty;

            [JsonProperty("kode")]
            public string kode { get; set; } = string.Empty;
        }
    }
}