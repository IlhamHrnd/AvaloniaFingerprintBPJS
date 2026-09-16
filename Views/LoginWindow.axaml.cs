using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AvaloniaFingerprint.ViewModels;

namespace AvaloniaFingerprint.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// The floating action button has no backing feature yet, so it reports back
    /// through the existing message surface instead of doing nothing silently.
    /// </summary>
    private void OnFabClick(object? sender, RoutedEventArgs e) =>
        (DataContext as LoginViewModel)?.ShowMessage("Fitur belum tersedia.");
}
