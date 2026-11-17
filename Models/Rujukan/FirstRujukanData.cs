using System;
using System.Collections.Generic;

namespace AvaloniaFingerprint.Models.Rujukan
{
    public class FirstRujukanData
    {
        public MetaData metaData { get; set; } = new MetaData();

        public Response response { get; set; } = new Response();

        public class MetaData
        {
            public bool isValid { get; set; }

            public string? code { get; set; }

            public string? message { get; set; }
        }

        public class Response
        {
            public AsalFaskes? asalFaskes { get; set; }

            public List<Referral> rujukan { get; set; } = [];
        }

        public class AsalFaskes
        {
            public string? kode { get; set; }

            public string? nama { get; set; }
        }

        public class Referral
        {
            public string? noKunjungan { get; set; }

            public DateTime? tglKunjungan { get; set; }

            public KodeNama? provPerujuk { get; set; }

            public Peserta? peserta { get; set; }

            public KodeNama? diagnosa { get; set; }

            public string? keluhan { get; set; }

            public KodeNama? poliRujukan { get; set; }

            public KodeNama? pelayanan { get; set; }
        }

        public class KodeNama
        {
            public string? kode { get; set; }

            public string? nama { get; set; }
        }

        public class KodeKeterangan
        {
            public string? kode { get; set; }

            public string? keterangan { get; set; }
        }

        public class Peserta
        {
            public string? noKartu { get; set; }

            public string? nik { get; set; }

            public string? nama { get; set; }

            public string? pisa { get; set; }

            public string? sex { get; set; }

            public Mr? mr { get; set; }

            public DateTime? tglLahir { get; set; }

            public DateTime? tglCetakKartu { get; set; }

            public DateTime? tglTAT { get; set; }

            public DateTime? tglTMT { get; set; }

            public KodeKeterangan? statusPeserta { get; set; }

            public ProvUmum? provUmum { get; set; }

            public KodeKeterangan? jenisPeserta { get; set; }

            public KodeKeterangan? hakKelas { get; set; }

            public Umur? umur { get; set; }

            public Informasi? informasi { get; set; }

            public Cob? cob { get; set; }
        }

        public class Mr
        {
            public string? noMR { get; set; }

            public string? noTelepon { get; set; }
        }

        public class ProvUmum
        {
            public string? kdProvider { get; set; }

            public string? nmProvider { get; set; }
        }

        public class Umur
        {
            public string? umurSekarang { get; set; }

            public string? umurSaatPelayanan { get; set; }
        }

        public class Informasi
        {
            public string? dinsos { get; set; }

            public string? prolanisPRB { get; set; }

            public string? noSKTM { get; set; }
        }

        public class Cob
        {
            public string? noAsuransi { get; set; }

            public string? nmAsuransi { get; set; }

            public DateTime? tglTMT { get; set; }

            public DateTime? tglTAT { get; set; }
        }
    }
}
