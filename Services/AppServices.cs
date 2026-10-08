using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace AvaloniaFingerprint.Services;

/// <summary>
/// Minimal composition root. The app has a handful of long-lived singletons, so a full
/// DI container would be more ceremony than it is worth.
/// </summary>
public sealed class AppServices : IDisposable
{
    private static AppServices? _current;

    public static AppServices Current => _current ??= new AppServices();

    private AppServices()
    {
        Settings = AppSettings.Load();

        // Fitur "Daftar Fingerprint" dihapus karena aplikasi dilarang menyimpan data biometrik
        // pasien. Versi lama menulisnya ke registrations.json di folder aplikasi; sisa file itu
        // dibersihkan sekali di sini supaya workstation yang terlanjur menyimpan data ikut bersih.
        PurgeLegacyBiometricStore(Settings.Configuration);

        Session = new SessionService();
        AppUser = new AppUserServices(Settings.Configuration, Session);
        Fingerprint = new FingerprintServices(Settings.Configuration, Session);
        Camera = new CameraServices(Settings.Configuration, Session);
        Appointment = new AppointmentServices(Settings.Configuration, Session);
        Printout = new PrintoutServices(Settings.Configuration, Session);

        // Dimuat malas: konstruktor ini TIDAK memuat model dlib (~126 MB file, ~500 MB
        // native heap), jadi startup app tidak terpengaruh walaupun fitur wajah tak dipakai.
        FaceRecognition = new FaceRecognitionService(Settings.Configuration, Session);
    }

    /// <summary>
    /// Menghapus jejak penyimpanan biometrik dari versi lama: file JSON berisi gambar sidik jari
    /// pasien (base64) beserta identitas pemiliknya. Bersifat best-effort — kegagalan (file
    /// terkunci / folder read-only) tidak boleh menghalangi aplikasi berjalan. Isi file tidak
    /// pernah dibaca, dipindahkan, maupun ditulis ke log; hanya nama filenya yang dicatat.
    /// </summary>
    private static void PurgeLegacyBiometricStore(IConfiguration config)
    {
        try
        {
            var name = config["Storage:RegistrationsFile"] ?? "registrations.json";
            var path = Path.IsPathRooted(name) ? name : Path.Combine(AppContext.BaseDirectory, name);
            if (!File.Exists(path))
                return;

            File.Delete(path);
            Trace.WriteLine($"[AppServices] Legacy biometric store removed: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AppServices] Legacy biometric store removal failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public AppSettings Settings { get; }
    public SessionService Session { get; }
    public AppUserServices AppUser { get; }
    public FingerprintServices Fingerprint { get; }
    public CameraServices Camera { get; }
    public AppointmentServices Appointment { get; }
    public PrintoutServices Printout { get; }

    /// <summary>
    /// Diexpose lewat interface karena ini satu-satunya service di app yang punya kontrak
    /// formal (tepat 128 nilai finite, tepat satu wajah) - pemanggil sebaiknya bergantung
    /// pada kontrak itu, bukan pada implementasinya.
    /// </summary>
    public IFaceRecognitionService FaceRecognition { get; }

    public void Dispose()
    {
        Fingerprint.Dispose();
        Camera.Dispose();
        FaceRecognition.Dispose();
        _current = null;
    }
}
