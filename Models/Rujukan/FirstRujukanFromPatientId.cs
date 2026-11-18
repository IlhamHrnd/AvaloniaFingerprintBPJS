using System;
using System.Collections.Generic;

namespace AvaloniaFingerprint.Models.Rujukan
{
    public class FirstRujukanFromPatientId
    {
        public class Cob
        {
            public string noAsuransi { get; set; } = string.Empty;
            public string nmAsuransi { get; set; } = string.Empty;
            public DateTime? tglTMT { get; set; }
            public DateTime? tglTAT { get; set; }
        }

        public class Diagnosa
        {
            public string kode { get; set; } = string.Empty;
            public string nama { get; set; } = string.Empty;
        }

        public class hakKelas
        {
            public string kode { get; set; } = string.Empty;
            public string keterangan { get; set; } = string.Empty;
        }

        public class Informasi
        {
            public string dinsos { get; set; } = string.Empty;
            public string prolanisPRB { get; set; } = string.Empty;
            public string noSKTM { get; set; } = string.Empty;
        }

        public class jenisPeserta
        {
            public string kode { get; set; } = string.Empty;
            public string keterangan { get; set; } = string.Empty;
        }

        public class metaData
        {
            public bool isValid { get; set; } = false;
            public string code { get; set; } = string.Empty;
            public string message { get; set; } = string.Empty;
        }

        public class Mr
        {
            public string noMR { get; set; } = string.Empty;
            public string noTelepon { get; set; } = string.Empty;
        }

        public class Pelayanan
        {
            public string kode { get; set; } = string.Empty;
            public string nama { get; set; } = string.Empty;
        }

        public class Peserta
        {
            public string noKartu { get; set; } = string.Empty;
            public string nik { get; set; } = string.Empty;
            public string nama { get; set; } = string.Empty;
            public string pisa { get; set; } = string.Empty;
            public string sex { get; set; } = string.Empty;
            public Mr mr { get; set; } = new();
            public DateTime tglLahir { get; set; } = DateTime.MinValue;
            public DateTime tglCetakKartu { get; set; } = DateTime.MinValue;
            public DateTime tglTAT { get; set; } = DateTime.MinValue;
            public DateTime tglTMT { get; set; } = DateTime.MinValue;
            public statusPeserta statusPeserta { get; set; } = new();
            public provUmum provUmum { get; set; } = new();
            public jenisPeserta jenisPeserta { get; set; } = new();
            public hakKelas hakKelas { get; set; } = new();
            public Umur umur { get; set; } = new();
            public Informasi informasi { get; set; } = new();
            public Cob cob { get; set; } = new();
        }

        public class poliRujukan
        {
            public string kode { get; set; } = string.Empty;
            public string nama { get; set; } = string.Empty;
        }

        public class provPerujuk
        {
            public string kode { get; set; } = string.Empty;
            public string nama { get; set; } = string.Empty;
        }

        public class provUmum
        {
            public string kdProvider { get; set; } = string.Empty;
            public string nmProvider { get; set; } = string.Empty;
        }

        public class Response
        {
            public string asalFaskes { get; set; } = string.Empty;
            public List<Rujukan> rujukan { get; set; } = [];
        }

        public class Root
        {
            public metaData metaData { get; set; } = new();
            public Response response { get; set; } = new();
        }

        public class Rujukan
        {
            public string noKunjungan { get; set; } = string.Empty;
            public DateTime tglKunjungan { get; set; } = DateTime.MinValue;
            public provPerujuk provPerujuk { get; set; } = new();
            public Peserta peserta { get; set; } = new();
            public Diagnosa diagnosa { get; set; } = new();
            public string keluhan { get; set; } = string.Empty;
            public poliRujukan poliRujukan { get; set; } = new();
            public Pelayanan pelayanan { get; set; } = new();
        }

        public class statusPeserta
        {
            public string kode { get; set; } = string.Empty;
            public string keterangan { get; set; } = string.Empty;
        }

        public class Umur
        {
            public string umurSekarang { get; set; } = string.Empty;
            public string umurSaatPelayanan { get; set; } = string.Empty;
        }
    }
}
