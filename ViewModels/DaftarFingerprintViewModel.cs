using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaFingerprint.Models.Old;
using AvaloniaFingerprint.Services.Old;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvaloniaFingerprint.ViewModels;

/// <summary>
/// The WinForms <c>DaftarFingerprint</c> form was an empty placeholder, so this tab is
/// implemented as the list of registrations submitted from this workstation
/// (see <see cref="RegistrationStore"/>), with search and a detail preview.
/// </summary>
public sealed partial class DaftarFingerprintViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private List<FingerprintRecord> _all = new();

    public DaftarFingerprintViewModel(AppServices services) => _services = services;

    public ObservableCollection<FingerprintRecord> Records { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private FingerprintRecord? _selected;
    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isEmpty = true;

    public void OnSessionChanged(bool loggedIn)
    {
        IsLoggedIn = loggedIn;
        if (loggedIn)
            _ = RefreshAsync();
        else
        {
            _all = new List<FingerprintRecord>();
            ApplyFilter();
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var records = await _services.Registrations.LoadAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _all = records;
                ApplyFilter();
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task AddAsync(FingerprintRecord record)
    {
        await _services.Registrations.AddAsync(record);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is null)
            return;

        _all.Remove(Selected);
        await _services.Registrations.SaveAsync(_all);
        Selected = null;
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchText?.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _all
            : _all.Where(r =>
                r.NoKapst.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Nama.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Petugas.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        Records.Clear();
        foreach (var record in filtered)
            Records.Add(record);

        IsEmpty = Records.Count == 0;
    }
}
