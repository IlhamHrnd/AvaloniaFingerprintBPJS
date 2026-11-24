using System;

namespace AvaloniaFingerprint.Filters.Rujukan
{
    /// <summary>
    /// Body <c>POST /api/Rujukan/CreateSepFromRujukanBaru</c>. Seluruh nilai dikirim sebagai
    /// JSON body (bukan query string); <c>suId</c> dan <c>parId</c> sengaja tetap string karena
    /// API Service memperlakukan keduanya sebagai kode, bukan angka.
    /// </summary>
    public class CreateSepFromRujukanBaruRequest
    {
        public string patId { get; set; } = string.Empty;

        public string suId { get; set; } = string.Empty;

        public string parId { get; set; } = string.Empty;

        public DateTime visitDate { get; set; }

        public string noKunjungan { get; set; } = string.Empty;
    }
}
