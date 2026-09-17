using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaFingerprint.Models.Old;

namespace AvaloniaFingerprint.Services.Old;

/// <summary>
/// Local log of the registrations made from this workstation, backing the
/// "Daftar Fingerprint" tab. The WinForms form of the same name was an empty stub, so
/// this keeps its own JSON file rather than reaching into the SIMRS database.
/// </summary>
public sealed class RegistrationStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RegistrationStore(AppSettings settings) => _path = settings.RegistrationsPath;

    public async Task<List<FingerprintRecord>> LoadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
                return new List<FingerprintRecord>();

            await using var stream = File.OpenRead(_path);
            var records = await JsonSerializer.DeserializeAsync<List<FingerprintRecord>>(stream).ConfigureAwait(false);
            return records ?? new List<FingerprintRecord>();
        }
        catch (Exception)
        {
            return new List<FingerprintRecord>();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddAsync(FingerprintRecord record)
    {
        var existing = await LoadAsync().ConfigureAwait(false);
        existing.Insert(0, record);
        await SaveAsync(existing).ConfigureAwait(false);
    }

    public async Task SaveAsync(List<FingerprintRecord> records)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(records, Options);
            await File.WriteAllTextAsync(_path, json).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A read-only install folder must not break the registration flow itself.
        }
        finally
        {
            _gate.Release();
        }
    }
}
