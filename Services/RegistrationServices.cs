using AvaloniaFingerprint.Models.Fingerprint;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.Services
{
    public class RegistrationServices(IConfiguration config, SessionService session) : BaseService(config, session)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private string registrationsPath => Path.IsPathRooted(registrationsFile) ? registrationsFile : Path.Combine(AppContext.BaseDirectory, registrationsFile);
        public async Task<List<FingerprintRecord>> LoadAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!File.Exists(registrationsPath))
                    return new List<FingerprintRecord>();

                var json = await File.ReadAllTextAsync(registrationsPath).ConfigureAwait(false);
                var records = JsonConvert.DeserializeObject<List<FingerprintRecord>>(json);
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
                var json = JsonConvert.SerializeObject(records, Formatting.Indented);
                await File.WriteAllTextAsync(registrationsPath, json).ConfigureAwait(false);
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
}
