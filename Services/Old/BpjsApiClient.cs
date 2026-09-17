using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaFingerprint.Models.Old;
using Microsoft.Extensions.Configuration;
using static AvaloniaFingerprint.Models.Old.LoginPetugas;
using static AvaloniaFingerprint.Models.Old.RegistValidate;
using static AvaloniaFingerprint.Models.Old.Verify;

namespace AvaloniaFingerprint.Services.Old;

public sealed class ApiResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Error { get; init; }
    public string? RawContent { get; init; }

    public static ApiResult<T> Ok(T data, string? raw = null) => new() { Success = true, Data = data, RawContent = raw };
    public static ApiResult<T> Fail(string error, string? raw = null) => new() { Success = false, Error = error, RawContent = raw };
}

/// <summary>
/// Replaces the RestSharp calls of the WinForms forms with a single HttpClient
/// based client. Payload shapes are unchanged so the existing endpoints keep working.
/// </summary>
public sealed class BpjsApiClient : IDisposable
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never };

    private readonly BpjsOptions _options;
    private readonly HttpClient _http;

    public BpjsApiClient(BpjsOptions options, IConfiguration config)
    {
        _options = options;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds)) };
    }

    public Task<ApiResult<LoginPetugasRoot>> LoginAsync(string username, string password, CancellationToken ct = default)
        => PostAsync<LoginPetugasRoot>(_options.LoginUrl, new LoginPetugas { username = username, password = password }, null, ct);

    public Task<ApiResult<VerifyRoot>> VerifyAsync(Verify body, string token, CancellationToken ct = default)
        => PostAsync<VerifyRoot>(_options.VerifyUrl, body, token, ct);

    public Task<ApiResult<RegistValidateRoot>> AddPesertaAsync(AddPeserta body, string? token = null, CancellationToken ct = default)
        => PostAsync<RegistValidateRoot>(_options.RegistUrl, body, token, ct);

    private async Task<ApiResult<T>> PostAsync<T>(string url, object body, string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return ApiResult<T>.Fail("URL endpoint belum dikonfigurasi (appsettings.json).");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(body, body.GetType(), WriteOptions), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(token))
                request.Headers.TryAddWithoutValidation("token", token);

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return ApiResult<T>.Fail($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", content);

            var data = JsonSerializer.Deserialize<T>(content, ReadOptions);
            return data is null
                ? ApiResult<T>.Fail("Response kosong / tidak dapat dibaca.", content)
                : ApiResult<T>.Ok(data, content);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ApiResult<T>.Fail("Timeout menghubungi server.");
        }
        catch (Exception ex)
        {
            return ApiResult<T>.Fail(ex.Message);
        }
    }

    public void Dispose() => _http.Dispose();
}
