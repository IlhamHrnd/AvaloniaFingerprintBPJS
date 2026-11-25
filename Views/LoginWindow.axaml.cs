using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AvaloniaFingerprint.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();

        // Focus awal di Username (§8.7.7).
        Opened += (_, _) => UsernameBox.Focus();
    }

    /// <summary>
    /// Window login startup tampil Full Screen sehingga title bar tidak ada; F11 men-toggle
    /// FullScreen ↔ Normal sebagai jalan keluar, sama seperti di <see cref="MainWindow"/>.
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

    // RevealPassword: checkbox menampilkan/menyembunyikan karakter sandi (§9.2).
    private void OnRevealPasswordChanged(object? sender, RoutedEventArgs e) =>
        PasswordBox.PasswordChar = RevealCheck.IsChecked == true ? default : '\u2022';
}
