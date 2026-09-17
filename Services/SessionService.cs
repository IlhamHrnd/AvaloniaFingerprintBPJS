using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaFingerprint.Services;

/// <summary>
/// Holds the petugas login state. Every tab is disabled until <see cref="IsLoggedIn"/> is true.
/// </summary>
public sealed partial class SessionService : ObservableObject
{
    [ObservableProperty]
    private string? _token;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _nama = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedIn))]
    private string _userId = string.Empty;
    [ObservableProperty] private DateTime? _loginTime;

    public bool IsLoggedIn => !string.IsNullOrEmpty(UserId);

    public event EventHandler? Changed;

    public void SignIn(string token, string username, string nama, string userId)
    {
        Username = username;
        Nama = nama;
        UserId = userId;
        LoginTime = DateTime.Now;
        Token = token;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        Token = null;
        Username = string.Empty;
        Nama = string.Empty;
        UserId = string.Empty;
        LoginTime = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
