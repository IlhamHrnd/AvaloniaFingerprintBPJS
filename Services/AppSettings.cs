using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace AvaloniaFingerprint.Services;

public class AppSettings
{
    public IConfiguration Configuration { get; private set; } = new ConfigurationBuilder().Build();

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

        settings.Configuration = config;
        return settings;
    }
}
