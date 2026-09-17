using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AvaloniaFingerprint.ViewModels;

namespace AvaloniaFingerprint.Views;

public partial class LoginWindow : Window
{
    private LoginViewModel? _viewModel;
    private bool _navigated;

    public LoginWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// The data context is assigned by App.after construction, so the login result is
    /// observed here instead of a constructor parameter.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as LoginViewModel;

        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        base.OnDataContextChanged(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LoginViewModel.Succeeded)) return;
        if (_viewModel?.Succeeded != true) return;

        ShowMainMenu();
    }

    /// <summary>
    /// Replaces the login window with the main menu once the credentials were accepted.
    /// The menu is shown before this window closes so the desktop lifetime keeps running.
    /// </summary>
    private void ShowMainMenu()
    {
        if (_navigated) return;
        _navigated = true;

        var mainMenu = new MainWindow();

        // Order matters. App.axaml.cs sets desktop.MainWindow at startup, so Avalonia
        // runs with ShutdownMode.OnLastWindowClose: closing whichever window is the
        // MainWindow ends the application. Show the menu first, hand MainWindow over to
        // it, and only then close this one -- otherwise the app exits before the menu
        // is ever visible.
        mainMenu.Show();

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = mainMenu;
        }

        IsVisible = false;
        Close();
    }

    /// <summary>
    /// The floating action button has no backing feature yet, so it reports back
    /// through the existing message surface instead of doing nothing silently.
    /// </summary>
    private void OnFabClick(object? sender, RoutedEventArgs e) =>
        (DataContext as LoginViewModel)?.ShowMessage("Fitur belum tersedia.");
}
