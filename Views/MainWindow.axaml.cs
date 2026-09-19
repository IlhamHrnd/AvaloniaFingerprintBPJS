using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AvaloniaFingerprint.ViewModels;

namespace AvaloniaFingerprint.Views;

/// <summary>
/// Main menu shown right after a successful login. It reads the signed-in user from
/// the shared <see cref="ViewModelBase.CurrentUserId"/> state.
/// </summary>
public partial class MainWindow : Window
{
    private const string BpjsWatermark = "Masukkan No. Kartu BPJS";
    private const string NikWatermark = "Masukkan No. Induk Kependudukan";

    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Fills the footer from the shared state once the data context is in place. Done here
    /// rather than in <see cref="OnOpened"/> because the named control is not guaranteed to
    /// be resolved yet at that point, which silently left the label empty.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        var footer = this.FindControl<TextBlock>("UsernameFooter");
        if (footer is not null) footer.Text = $"Username : {ViewModelBase.CurrentUserId}";
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Fallback for the case where the data context is assigned before the controls exist.
        var footer = this.FindControl<TextBlock>("UsernameFooter");
        if (footer is not null) footer.Text = $"Username : {ViewModelBase.CurrentUserId}";
    }

    /// <summary>Switches the input watermark between the BPJS card and the NIK number.</summary>
    private void OnJenisChanged(object? sender, RoutedEventArgs e)
    {
        var searchBox = this.FindControl<TextBox>("SearchBox");
        if (searchBox is null) return;

        var isBpjs = this.FindControl<RadioButton>("BpjsRadio")?.IsChecked == true;
        searchBox.PlaceholderText = isBpjs ? BpjsWatermark : NikWatermark;
        searchBox.Text = string.Empty;
    }

    /// <summary>Patient lookup is not part of the current scope yet.</summary>
    private void OnVerifikasiClick(object? sender, RoutedEventArgs e) => ShowNotAvailable();

    /// <summary>The app bar menu has no backing feature yet.</summary>
    private void OnMenuClick(object? sender, RoutedEventArgs e) => ShowNotAvailable();

    private void ShowNotAvailable()
    {
        var status = this.FindControl<TextBlock>("StatusText");
        if (status is not null) status.Text = "Fitur belum tersedia.";
    }
}
