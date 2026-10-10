using Avalonia.Controls;
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

    // RevealPassword: checkbox menampilkan/menyembunyikan karakter sandi (§9.2).
    private void OnRevealPasswordChanged(object? sender, RoutedEventArgs e) =>
        PasswordBox.PasswordChar = RevealCheck.IsChecked == true ? default : '\u2022';
}
