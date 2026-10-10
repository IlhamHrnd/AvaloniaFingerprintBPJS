using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaFingerprint.Services;

namespace AvaloniaFingerprint.Views;

public partial class ThemeDialog : Window
{
    private readonly ThemeService _theme;

    public ThemeDialog()
        : this(AppServices.Current.Theme)
    {
    }

    public ThemeDialog(ThemeService theme)
    {
        _theme = theme;
        InitializeComponent();

        switch (theme.Preference)
        {
            case ThemePreference.Light:
                LightOption.IsChecked = true;
                break;
            case ThemePreference.Dark:
                DarkOption.IsChecked = true;
                break;
            default:
                SystemOption.IsChecked = true;
                break;
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var preference = LightOption.IsChecked == true
            ? ThemePreference.Light
            : DarkOption.IsChecked == true
                ? ThemePreference.Dark
                : ThemePreference.System;

        _theme.Save(preference);
        _theme.Apply();
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
