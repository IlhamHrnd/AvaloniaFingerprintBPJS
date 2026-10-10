using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Styling;
using Newtonsoft.Json;

namespace AvaloniaFingerprint.Services;

/// <summary>Preferensi tema UI: ikut OS (System), selalu Light, atau selalu Dark.</summary>
public enum ThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>
/// Memuat/menyimpan preferensi tema UI di %LocalAppData%\AvaloniaFingerprint\ui-settings.json
/// dan menerapkannya lewat <see cref="Application.RequestedThemeVariant"/> (DESIGN.md §5.6, §9.7).
/// Berkas ini hanya memuat preferensi tema — tidak pernah data biometrik/pasien (§12).
/// </summary>
public sealed class ThemeService
{
    private sealed class UiSettings
    {
        public ThemePreference Theme { get; set; } = ThemePreference.System;
    }

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AvaloniaFingerprint");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "ui-settings.json");

    /// <summary>Preferensi aktif saat ini (default: System).</summary>
    public ThemePreference Preference { get; private set; } = ThemePreference.System;

    /// <summary>Baca preferensi tersimpan; bila berkas tidak ada/rusak, tetap System.</summary>
    public ThemePreference Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonConvert.DeserializeObject<UiSettings>(File.ReadAllText(SettingsPath));
                if (settings is not null && Enum.IsDefined(settings.Theme))
                    Preference = settings.Theme;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ThemeService] Load failed: {ex.GetType().Name}: {ex.Message}");
        }

        Trace.WriteLine($"[ThemeService] Loaded theme preference: {Preference}");
        return Preference;
    }

    /// <summary>Simpan preferensi ke ui-settings.json (gagal simpan tidak mematikan aplikasi).</summary>
    public void Save(ThemePreference preference)
    {
        Preference = preference;

        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(
                SettingsPath,
                JsonConvert.SerializeObject(new UiSettings { Theme = preference }, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ThemeService] Save failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Terapkan preferensi ke Application.RequestedThemeVariant (System = ThemeVariant.Default).</summary>
    public void Apply()
    {
        if (Application.Current is not { } app)
            return;

        app.RequestedThemeVariant = Preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

        Trace.WriteLine($"[ThemeService] Applied theme preference: {Preference}");
    }
}
