using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace AvaloniaFingerprint.Services;

public sealed class AppServices : IDisposable
{
    private static AppServices? _current;

    public static AppServices Current => _current ??= new AppServices();

    private AppServices()
    {
        Settings = AppSettings.Load();
        Theme = new ThemeService();

        PurgeLegacyBiometricStore(Settings.Configuration);

        Session = new SessionService();
        AppUser = new AppUserServices(Settings.Configuration, Session);
        Fingerprint = new FingerprintServices(Settings.Configuration, Session);
        Camera = new CameraServices(Settings.Configuration, Session);
        Appointment = new AppointmentServices(Settings.Configuration, Session);
        Printout = new PrintoutServices(Settings.Configuration, Session);

        FaceRecognition = new FaceRecognitionService(Settings.Configuration, Session);
    }

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
    public ThemeService Theme { get; }
    public SessionService Session { get; }
    public AppUserServices AppUser { get; }
    public FingerprintServices Fingerprint { get; }
    public CameraServices Camera { get; }
    public AppointmentServices Appointment { get; }
    public PrintoutServices Printout { get; }
    public FaceRecognitionService FaceRecognition { get; }

    public void Dispose()
    {
        Fingerprint.Dispose();
        Camera.Dispose();
        FaceRecognition.Dispose();
        _current = null;
    }
}
