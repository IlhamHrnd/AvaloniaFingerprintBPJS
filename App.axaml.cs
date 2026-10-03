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
            desktop.ShutdownRequested += (_, _) => services.Dispose();
            services.Fingerprint.Start();
            services.Camera.Start();

            if (!services.Session.IsLoggedIn)
            {
                var loginViewModel = new LoginViewModel(services);
                var loginWindow = new LoginWindow
                {
                    DataContext = loginViewModel,
                    WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen
                };

                loginViewModel.RequestClose = () =>
                {
                    if (loginViewModel.Succeeded)
                    {
                        var mainViewModel = new MainWindowViewModel(services);
                        var mainWindow = new MainWindow { DataContext = mainViewModel };
                        desktop.MainWindow = mainWindow;
                        mainWindow.Show();
                        loginWindow.Close();
                    }
                };

                desktop.MainWindow = loginWindow;
            }
            else
            {
                var mainViewModel = new MainWindowViewModel(services);
                desktop.MainWindow = new MainWindow { DataContext = mainViewModel };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
