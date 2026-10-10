using System.Threading.Tasks;
using Avalonia.Controls;
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
