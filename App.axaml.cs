using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaFingerprint.ViewModels;
using AvaloniaFingerprint.Views;
using Microsoft.Extensions.Configuration;

namespace AvaloniaFingerprint;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Only the login page is wired up for now; the main page is not built yet.
            desktop.MainWindow = new LoginWindow { DataContext = new LoginViewModel(BuildConfiguration()) };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Builds the configuration the view models read (App:Url, App:TimeoutSeconds) from
    /// appsettings.json, which is copied next to the executable.
    /// </summary>
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();
}
