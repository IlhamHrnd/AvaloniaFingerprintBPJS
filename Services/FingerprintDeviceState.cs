using System;

namespace AvaloniaFingerprint.Services
{
    /// <summary>
    /// Siklus hidup reader sidik jari, dari "tidak ada" sampai "benar-benar siap dipakai".
    /// <see cref="Ready"/> hanya boleh dicapai setelah SDK berhasil membuka reader dan melaporkan
    /// status siap. Keberadaan DPUruNet.dll, entri di <c>Win32_PnPEntity</c>, atau sekadar munculnya
    /// nama reader pada daftar SDK TIDAK cukup untuk mencapai state itu.
    /// </summary>
    public enum FingerprintReaderState
    {
        /// <summary>Tidak ada kandidat reader sama sekali.</summary>
        NoDevice = 0,

        /// <summary>
        /// Windows melihat perangkat biometrik, tetapi SDK tidak melaporkan reader yang bisa dibuka.
        /// Contoh: sensor WBF bawaan laptop, kamera IR Windows Hello, atau entri ghost setelah dicabut.
        /// State ini BUKAN "terhubung".
        /// </summary>
        Detected = 1,

        /// <summary>Reader terdaftar di SDK dan sedang divalidasi (Open / GetStatus / CanCapture).</summary>
        Initializing = 2,

        /// <summary>Reader tervalidasi dan siap dipakai. Satu-satunya state yang dihitung terhubung.</summary>
        Ready = 3,

        /// <summary>Reader ada di SDK tetapi gagal dibuka, tidak berstatus siap, atau tidak bisa capture.</summary>
        NotReady = 4,
    }

    /// <summary>
    /// Snapshot hasil satu probe device. Immutable dan selalu diganti utuh (tidak pernah dimutasi
    /// sebagian), sehingga nama/ID/keterangan basi tidak mungkin tertinggal saat reader dicabut.
    /// Inilah satu-satunya sumber kebenaran status fingerprint bagi Header maupun halaman lain.
    /// </summary>
    /// <param name="State">Tahap validasi reader.</param>
    /// <param name="DeviceName">Nama untuk ditampilkan ke pengguna.</param>
    /// <param name="DeviceId">Identitas stabil reader (dipakai mendeteksi pergantian device).</param>
    /// <param name="Detail">Keterangan/penyebab, terutama saat tidak Ready.</param>
    public sealed record FingerprintDeviceState(
        FingerprintReaderState State,
        string? DeviceName,
        string? DeviceId,
        string? Detail)
    {
        /// <summary>
        /// Diturunkan dari <see cref="State"/>, bukan disimpan sebagai flag terpisah, supaya tidak
        /// mungkin ada kondisi "terhubung" sementara state-nya bukan <see cref="FingerprintReaderState.Ready"/>.
        /// </summary>
        public bool IsConnected => State == FingerprintReaderState.Ready;

        /// <summary>State awal sebelum probe pertama selesai.</summary>
        public static readonly FingerprintDeviceState None = new(FingerprintReaderState.NoDevice, null, null, null);

        public static FingerprintDeviceState AsNoDevice(string? detail = null) =>
            new(FingerprintReaderState.NoDevice, null, null, detail);

        public static FingerprintDeviceState AsDetected(string? deviceName, string? deviceId, string? detail = null) =>
            new(FingerprintReaderState.Detected, deviceName, deviceId, detail);

        public static FingerprintDeviceState AsInitializing(string? deviceName, string? deviceId, string? detail = null) =>
            new(FingerprintReaderState.Initializing, deviceName, deviceId, detail);

        public static FingerprintDeviceState AsReady(string? deviceName, string? deviceId, string? detail = null) =>
            new(FingerprintReaderState.Ready, deviceName, deviceId, detail);

        public static FingerprintDeviceState AsNotReady(string? deviceName, string? deviceId, string? detail = null) =>
            new(FingerprintReaderState.NotReady, deviceName, deviceId, detail);
    }

    /// <summary>
    /// Probe reader melalui SDK DigitalPersona.
    /// <paramref name="publish"/> dipakai untuk mengumumkan state sementara (misalnya
    /// <see cref="FingerprintReaderState.Initializing"/>) sebelum validasi selesai, sehingga
    /// perpindahan state terlihat di UI tanpa menunggu probe berakhir.
    /// Mengembalikan <c>null</c> berarti "tidak ada pendapat": watcher harus mempertahankan state
    /// terakhir. Dipakai saat capture sedang berjalan atau reader sedang dipakai proses lain.
    /// </summary>
    public delegate FingerprintDeviceState? FingerprintSdkProbe(Action<FingerprintDeviceState> publish);
}
