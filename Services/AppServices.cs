using System;

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
        Session = new SessionService();
        AppUser = new AppUserServices(Settings.Configuration, Session);
        Fingerprint = new FingerprintServices(Settings.Configuration, Session);
        Camera = new CameraServices(Settings.Configuration, Session);
        Registrations = new RegistrationServices(Settings.Configuration, Session);
        Appointment = new AppointmentServices(Settings.Configuration, Session);
        Printout = new PrintoutServices(Settings.Configuration, Session);

        // Dimuat malas: konstruktor ini TIDAK memuat model dlib (~126 MB file, ~500 MB
        // native heap), jadi startup app tidak terpengaruh walaupun fitur wajah tak dipakai.
        FaceRecognition = new FaceRecognitionService(Settings.Configuration, Session);
    }

    public AppSettings Settings { get; }
    public SessionService Session { get; }
    public AppUserServices AppUser { get; }
    public FingerprintServices Fingerprint { get; }
    public CameraServices Camera { get; }
    public RegistrationServices Registrations { get; }
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
