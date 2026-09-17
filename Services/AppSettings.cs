using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace AvaloniaFingerprint.Services;

public class BpjsOptions
{
    public string LoginUrl { get; set; } = string.Empty;
    public string VerifyUrl { get; set; } = string.Empty;
    public string RegistUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 20;
}

public class FingerprintOptions
{
    public int CaptureTimeoutMs { get; set; } = 7000;
    public int VerifyTimeoutMs { get; set; } = 5000;
    public int FingersPerHand { get; set; } = 4;
    public int RightHandIndex { get; set; } = 1;
    public int LeftHandIndex { get; set; } = 6;
}

public class DeviceOptions
{
    public int PollIntervalSeconds { get; set; } = 2;
    public string[] MatchKeywords { get; set; } = { "U.are.U", "DigitalPersona", "Fingerprint", "Biometric" };
}

public class StorageOptions
{
    public string RegistrationsFile { get; set; } = "registrations.json";
}

public class LinkOptions
{
    public string DatinAPI { get; set; } = string.Empty;
}

public class AppSettings
{
    public BpjsOptions Bpjs { get; } = new();
    public FingerprintOptions Fingerprint { get; } = new();
    public DeviceOptions Device { get; } = new();
    public StorageOptions Storage { get; } = new();
    public LinkOptions Link { get; } = new(); 

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        var basePath = AppContext.BaseDirectory;
        var file = Path.Combine(basePath, "appsettings.json");
        if (!File.Exists(file))
            return settings;

        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        config.GetSection("Bpjs").Bind(settings.Bpjs);
        config.GetSection("Fingerprint").Bind(settings.Fingerprint);
        config.GetSection("Device").Bind(settings.Device);
        config.GetSection("Storage").Bind(settings.Storage);
        config.GetSection("Link").Bind(settings.Link);
        return settings;
    }

    /// <summary>Absolute path of the local registration log.</summary>
    public string RegistrationsPath => Path.IsPathRooted(Storage.RegistrationsFile) ? Storage.RegistrationsFile : Path.Combine(AppContext.BaseDirectory, Storage.RegistrationsFile);
}
