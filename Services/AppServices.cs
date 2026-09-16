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
        Api = new BpjsApiClient(Settings.Bpjs);
        Fingerprint = new FingerprintService(Settings.Fingerprint, Settings.Device);
        Registrations = new RegistrationStore(Settings);
    }

    public AppSettings Settings { get; }
    public SessionService Session { get; }
    public BpjsApiClient Api { get; }
    public FingerprintService Fingerprint { get; }
    public RegistrationStore Registrations { get; }

    public void Dispose()
    {
        Fingerprint.Dispose();
        Api.Dispose();
        _current = null;
    }
}
