using AvaloniaFingerprint.Services.Old;
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
        Api = new BpjsApiClient(Settings.Bpjs, Settings.Configuration);
        Fingerprint = new FingerprintServices(Settings.Configuration, Session);
        Registrations = new RegistrationServices(Settings.Configuration, Session);
    }

    public AppSettings Settings { get; }
    public SessionService Session { get; }
    public AppUserServices AppUser { get; }
    public BpjsApiClient Api { get; }
    public FingerprintServices Fingerprint { get; }
    public RegistrationServices Registrations { get; }

    public void Dispose()
    {
        Fingerprint.Dispose();
        Api.Dispose();
        _current = null;
    }
}
