using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AvaloniaFingerprint.Services;
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

    private FingerprintReaderService? _fingerprint;

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

        StartFingerprint();
    }

    /// <summary>Releases the fingerprint reader when the menu closes.</summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _fingerprint?.Stop();
        _fingerprint = null;
    }

    /// <summary>
    /// Brings the reader up once the menu is usable so a finger is picked up without any
    /// extra button press. Any failure is reported in the status line, never thrown, so a
    /// missing or busy device can never take the menu down.
    /// </summary>
    private void StartFingerprint()
    {
        try
        {
            _fingerprint = new FingerprintReaderService();

            // The SDK raises these on a background thread, so every UI touch goes
            // through the dispatcher.
            _fingerprint.StatusChanged += (_, status) => RunOnUi(() => SetStatus(status));
            _fingerprint.FingerprintCaptured += (_, result) => RunOnUi(() => ShowFingerprintAlert(result));

            _fingerprint.Start();

            SetStatus(_fingerprint.IsDeviceAvailable
                ? "Menunggu fingerprint..."
                : "Fingerprint device tidak tersedia");
        }
        catch (Exception ex)
        {
            _fingerprint = null;
            SetStatus($"Fingerprint device tidak tersedia: {ex.Message}");
        }
    }

    private static void RunOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    /// <summary>Switches the input watermark between the BPJS card and the NIK number.</summary>
    private void OnJenisChanged(object? sender, RoutedEventArgs e)
    {
        var searchBox = this.FindControl<TextBox>("SearchBox");
        if (searchBox is null) return;

        var isBpjs = this.FindControl<RadioButton>("BpjsRadio")?.IsChecked == true;
        searchBox.PlaceholderText = isBpjs ? BpjsWatermark : NikWatermark;
        searchBox.Text = string.Empty;

        // Keep the reader state consistent across the BPJS <-> NIK switch.
        UpdateFingerprintInput();
    }

    /// <summary>
    /// Scanning only runs once a card number or NIK has been typed. While the input is
    /// empty the device may stay initialized but no capture is armed.
    /// </summary>
    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => UpdateFingerprintInput();

    private void UpdateFingerprintInput()
    {
        if (_fingerprint is null) return;

        var text = this.FindControl<TextBox>("SearchBox")?.Text?.Trim() ?? string.Empty;
        _fingerprint.CurrentInput = text;

        try
        {
            if (text.Length > 0)
            {
                _fingerprint.Start();
                SetStatus(_fingerprint.IsDeviceAvailable ? "Menunggu fingerprint..." : "Fingerprint device tidak tersedia");
            }
            else
            {
                SetStatus(_fingerprint.IsDeviceAvailable
                    ? "Masukkan No. BPJS / NIK untuk memulai fingerprint"
                    : "Fingerprint device tidak tersedia");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Fingerprint reader error: {ex.Message}");
        }
    }

    /// <summary>
    /// Dummy alert for this development stage. Nothing is sent to Tarakan Bridging; the
    /// capture only proves that the hardware and the event wiring work.
    /// </summary>
    private void ShowFingerprintAlert(FingerprintCapturedEventArgs result)
    {
        SetStatus(result.Success ? "Fingerprint terdeteksi" : result.Message);

        var isBpjs = this.FindControl<RadioButton>("BpjsRadio")?.IsChecked == true;
        var label = isBpjs ? "No. BPJS" : "NIK";
        var number = string.IsNullOrWhiteSpace(result.CardNumber) ? "-" : result.CardNumber;

        var dialog = new Window
        {
            Title = "Fingerprint",
            Width = 380,
            Height = 210,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brushes.White
        };

        var okButton = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        okButton.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Fingerprint terdeteksi!", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold },
                new TextBlock { Text = $"{label}:\n{number}" },
                new TextBlock { Text = $"Status:\n{result.Message}" },
                okButton
            }
        };

        dialog.ShowDialog(this);
    }

    private void SetStatus(string text)
    {
        var status = this.FindControl<TextBlock>("StatusText");
        if (status is not null) status.Text = text;
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
