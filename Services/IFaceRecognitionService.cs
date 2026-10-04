using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.Services;

/// <summary>
/// Menghasilkan vektor embedding wajah 128 dimensi dari sebuah gambar.
/// <para>
/// Kontrak yang dijamin implementasi:
/// <list type="bullet">
///   <item><description>Hasil selalu <b>tepat 128</b> nilai <see cref="double"/>, semuanya finite (bukan NaN/Infinity).</description></item>
///   <item><description>Gagal berarti melempar subclass <c>FaceRecognitionExceptions</c> - TIDAK PERNAH mengembalikan null, list kosong, atau vektor berisi nol.</description></item>
///   <item><description>Satu wajah per gambar. 0 wajah → <c>NoFaceDetectedExceptions</c>; &gt;1 wajah → <c>MultipleFacesDetectedExceptions</c>.</description></item>
/// </list>
/// </para>
/// <para>
/// Urutan magnitudo nilai mengikuti dlib ResNet v1: pengukuran nyata memberi rentang sekitar
/// -0.35 s.d. +0.37. Vektor dua foto orang yang sama biasanya berjarak Euclidean &lt; 0.6;
/// orang berbeda &gt; 0.6 (obama vs biden terukur 0.85). Perbandingan 1:1 sengaja di luar
/// cakupan service ini.
/// </para>
/// <para>
/// Ketiga jalur masukan menghasilkan vektor IDENTIK untuk pixel yang sama, karena semuanya
/// bertemu di satu jalur: konversi ke RGB rapat, pembatasan ukuran, lalu dlib. Gambar yang
/// sisi terpanjangnya melebihi <c>FaceRecognition:MaxEdge</c> (default 800 px) diperkecil lebih
/// dulu - ini memangkas waktu deteksi CNN sekitar 2x tanpa mengubah identitas
/// (terukur: jarak 0.08 antara versi dibatasi dan versi resolusi asli).
/// </para>
/// </summary>
public interface IFaceRecognitionService : IDisposable
{
    /// <summary>
    /// <see langword="true"/> bila keempat file model dlib sudah ada di <see cref="ModelDirectory"/>
    /// (atau handle native sudah termuat). Pemeriksaan ini TIDAK memuat model dan tidak melempar,
    /// jadi aman dipakai UI untuk mematikan/menyalakan tombol sebelum memanggil method encoding.
    /// </summary>
    bool IsReady { get; }

    /// <summary>Path absolut folder model yang dipakai, untuk pesan diagnostik.</summary>
    string ModelDirectory { get; }

    /// <summary>
    /// Encoding dari gambar terkode di memori (JPEG/PNG/BMP/TIFF/WebP) - jalur unggah file.
    /// </summary>
    /// <param name="encodedImage">Byte gambar utuh. <see cref="ReadOnlySpan{T}"/> tidak dipakai karena harus bertahan melewati await.</param>
    /// <exception cref="Exceptions.FaceImageExceptions">Byte null/kosong atau tidak bisa didekode sebagai gambar.</exception>
    /// <exception cref="Exceptions.NoFaceDetectedExceptions">Tidak ada wajah.</exception>
    /// <exception cref="Exceptions.MultipleFacesDetectedExceptions">Lebih dari satu wajah.</exception>
    Task<List<double>> GetFaceEncodingAsync(byte[] encodedImage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Encoding dari pixel mentah BGRA 8-bit - jalur tangkapan kamera
    /// (<c>CameraFrameEventArgs.Pixels</c> / <c>WriteCaptureBgra</c>, keduanya BGRA8888).
    /// </summary>
    /// <param name="bgraPixels">Buffer pixel BGRA. Hanya dibaca <b>sinkron sebelum await pertama</b>,
    /// sehingga buffer milik <c>ArrayPool</c> aman dikembalikan pemanggil segera setelah method ini
    /// mengembalikan <see cref="Task"/>-nya.</param>
    /// <param name="width">Lebar gambar dalam pixel.</param>
    /// <param name="height">Tinggi gambar dalam pixel.</param>
    /// <param name="stride">Jarak byte antar baris. Isi 0 untuk memakai <c>width * 4</c> (baris rapat).</param>
    /// <exception cref="Exceptions.FaceImageExceptions">Dimensi tidak positif, buffer terlalu pendek untuk <paramref name="stride"/>.</exception>
    Task<List<double>> GetFaceEncodingAsync(byte[] bgraPixels, int width, int height, int stride, CancellationToken cancellationToken = default);

    /// <summary>
    /// Encoding dari file gambar di disk (JPEG/PNG/BMP/TIFF/WebP). Setara dengan membaca file
    /// lalu memanggil <see cref="GetFaceEncodingAsync(byte[], CancellationToken)"/>: keduanya
    /// memakai satu decoder (OpenCV) sehingga pixel yang sama menghasilkan vektor yang
    /// identik bit-per-bit. Kesetaraan ini penting bila vektor nanti dibandingkan 1:1.
    /// </summary>
    /// <exception cref="Exceptions.FaceImageExceptions">Path null/kosong, file tidak ada, kosong, atau gagal didekode.</exception>
    Task<List<double>> GetFaceEncodingFromFileAsync(string filePath, CancellationToken cancellationToken = default);
}
