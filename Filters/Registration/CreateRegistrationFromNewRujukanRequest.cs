using System;

namespace AvaloniaFingerprint.Filters.Registration
{
    /// <summary>
    /// Body <c>POST /api/Registration/CreateRegistrationFromNewRujukan</c>. Seluruh nilai
    /// dikirim sebagai JSON body (bukan query string). <c>parId</c> sengaja integer karena API
    /// Service memakai ID paramedis internal, sedangkan <c>suId</c> tetap string supaya kode
    /// unit yang berawalan nol tidak berubah menjadi angka.
    /// </summary>
    public class CreateRegistrationFromNewRujukanRequest
    {
        public string patId { get; set; } = string.Empty;

        public string noKunjungan { get; set; } = string.Empty;

        public DateTime dateTime { get; set; }

        public string suId { get; set; } = string.Empty;

        public int parId { get; set; }

        public string userId { get; set; } = string.Empty;

        public string sepNo { get; set; } = string.Empty;
    }
}
