using System;
using System.Collections.Generic;

namespace AvaloniaFingerprint.Exceptions;

/// <summary>
/// Basis semua kegagalan pengenalan wajah. Mengikuti gaya
/// <see cref="FingerprintExceptions"/>: pesan jadi satu-satunya argumen konstruktor.
/// Konstruktor kedua ditambahkan karena kegagalan di sini membungkus error native
/// dlib yang stack trace-nya berharga untuk diagnosis.
/// </summary>
public class FaceRecognitionExceptions : Exception
{
    public FaceRecognitionExceptions(string message) : base(message)
    {
    }

    public FaceRecognitionExceptions(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// File model dlib (.dat) atau native FaceRecognitionDotNet tidak tersedia, sehingga
/// <c>FaceRecognition.Create()</c> tidak bisa dijalankan. Pesan selalu menyebutkan
/// path yang dicari dan perintah untuk mengunduhnya.
/// </summary>
public class FaceModelMissingExceptions : FaceRecognitionExceptions
{
    public FaceModelMissingExceptions(string message) : base(message)
    {
    }

    public FaceModelMissingExceptions(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Tidak ada satu pun wajah terdeteksi pada gambar. Ini kondisi yang diharapkan
/// terjadi di lapangan (kamera belum siap, wajah di luar frame, terlalu gelap),
/// jadi pemanggil sebaiknya menampilkannya sebagai pesan, bukan crash.
/// </summary>
public class NoFaceDetectedExceptions(string message) : FaceRecognitionExceptions(message)
{
}

/// <summary>
/// Lebih dari satu wajah terdeteksi. Encoding wajah hanya bermakna untuk satu orang,
/// jadi service sengaja TIDAK memilih wajah pertama secara diam-diam - pemanggil harus
/// meminta pengguna mengulang dengan satu wajah di frame.
/// </summary>
public class MultipleFacesDetectedExceptions(string message) : FaceRecognitionExceptions(message)
{
    /// <summary>Jumlah wajah yang terdeteksi (selalu &gt; 1).</summary>
    public int FaceCount { get; init; }
}

/// <summary>
/// Wajah terdeteksi tetapi vektornya tidak dapat dipercaya: panjang bukan 128,
/// kosong, atau berisi NaN/Infinity. Menandakan kegagalan native di dalam dlib.
/// </summary>
public class FaceEncodingExceptions : FaceRecognitionExceptions
{
    public FaceEncodingExceptions(string message) : base(message)
    {
    }

    public FaceEncodingExceptions(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Gambar masukan tidak usable: null, kosong, bukan JPEG/PNG, atau dimensinya tidak
/// konsisten dengan panjang buffer pixel-nya.
/// </summary>
public class FaceImageExceptions : FaceRecognitionExceptions
{
    public FaceImageExceptions(string message) : base(message)
    {
    }

    public FaceImageExceptions(string message, Exception innerException) : base(message, innerException)
    {
    }
}
