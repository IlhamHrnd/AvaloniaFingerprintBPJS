using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using AvaloniaFingerprint.Services;
using AvaloniaFingerprint.ViewModels;
using AvaloniaFingerprint.Views;

namespace AvaloniaFingerprint;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // CommunityToolkit.Mvvm already implements INotifyDataErrorInfo; the default
            // Avalonia validator would report every error twice.
            DisableDuplicateValidation();

            var services = AppServices.Current;
            var mainViewModel = new MainWindowViewModel(services);

            desktop.MainWindow = new MainWindow { DataContext = mainViewModel };
            desktop.ShutdownRequested += (_, _) => services.Dispose();

            services.Fingerprint.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableDuplicateValidation()
    {
        foreach (var plugin in BindingPlugins.DataValidators.ToArray())
        {
            if (plugin is DataAnnotationsValidationPlugin)
                BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
