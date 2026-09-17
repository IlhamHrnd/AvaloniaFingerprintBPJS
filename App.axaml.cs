using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
            var services = AppServices.Current;
            var mainViewModel = new MainWindowViewModel(services);

            desktop.MainWindow = new MainWindow { DataContext = mainViewModel };
            desktop.ShutdownRequested += (_, _) => services.Dispose();

            services.Fingerprint.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
