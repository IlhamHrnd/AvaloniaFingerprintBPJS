using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaFingerprint.Services;
using AvaloniaFingerprint.ViewModels;

namespace AvaloniaFingerprint.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ShowLoginDialog = ShowLoginDialogAsync;
                vm.ShowThemeDialog = ShowThemeDialogAsync;
            }
        };
    }

    /// <summary>
    /// Window tampil Full Screen sehingga title bar (beserta tombol minimize/close) tidak ada.
    /// F11 men-toggle FullScreen ↔ Normal sebagai jalan keluar tanpa harus Alt+F4.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            WindowState = WindowState == Avalonia.Controls.WindowState.FullScreen
                ? Avalonia.Controls.WindowState.Normal
                : Avalonia.Controls.WindowState.FullScreen;
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private async Task ShowLoginDialogAsync()
    {
        var viewModel = new LoginViewModel(AppServices.Current);
        var dialog = new LoginWindow { DataContext = viewModel };
        viewModel.RequestClose = () => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private Task ShowThemeDialogAsync()
    {
        var dialog = new ThemeDialog(AppServices.Current.Theme);
        return dialog.ShowDialog(this);
    }
}
