using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace AvaloniaFingerprint.Services;

public class AppSettings
{
    /// <summary>
    /// Configuration root shared with the services. Disimpan agar service yang memerlukan
    /// IConfiguration (client turunan BaseService) memakai instance yang sama, bukan membangun
    /// sendiri. Nilai konfigurasi dibaca langsung oleh <see cref="BaseService"/>.
    /// </summary>
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
