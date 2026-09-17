namespace AvaloniaFingerprint.Models.Old;

/// <summary>
/// Request body for the petugas login endpoint. In the WinForms version the
/// credentials were hard-coded fields; here they are filled from the login dialog.
/// </summary>
public class LoginPetugas
{
    public string username { get; set; } = string.Empty;
    public string password { get; set; } = string.Empty;

    public class Lokasi
    {
        public string? nama { get; set; }
        public string? kode { get; set; }
    }

    public class MetaData
    {
        public string? message { get; set; }
        public int code { get; set; }
    }

    public class Response
    {
        public string? userID { get; set; }
        public string? username { get; set; }
        public string? tokenLogin { get; set; }
        public string? nama { get; set; }
        public string? email { get; set; }
        public string? kdGrupUserApp { get; set; }
        public string? nmGrupUserApp { get; set; }
        public string? jnsLokasi { get; set; }
        public Lokasi? lokasi { get; set; }
        public string? lokasiDetail { get; set; }
        public string? urlHUB { get; set; }
    }

    public class LoginPetugasRoot
    {
        public Response? response { get; set; }
        public MetaData? metaData { get; set; }
    }
}
