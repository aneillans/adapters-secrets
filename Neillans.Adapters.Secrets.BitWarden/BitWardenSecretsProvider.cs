using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Neillans.Adapters.Secrets.Core;

namespace Neillans.Adapters.Secrets.BitWarden;

/// <summary>
/// BitWarden / VaultWarden implementation of the secrets provider.
/// This implementation uses the server HTTP API to list and read cipher items.
/// Mutating operations are not implemented because they typically require client-side encryption
/// and specific API behaviour; callers should use a dedicated tool or export/import flow.
/// </summary>
public class BitWardenSecretsProvider : ISecretsProvider
{
    private readonly HttpClient _httpClient;
    private readonly BitWardenOptions _options;

    public BitWardenSecretsProvider(IOptions<BitWardenOptions> options)
    {
        if (options?.Value == null)
            throw new ArgumentNullException(nameof(options));

        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.ServerUrl))
            throw new ArgumentException("ServerUrl is required", nameof(options));

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new ArgumentException("ApiKey is required", nameof(options));

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_options.ServerUrl.TrimEnd('/'))
        };

        // Attach bearer token by default
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var items = await GetAllCiphersAsync(cancellationToken);
            var match = items.FirstOrDefault(i => string.Equals(i.Name, key, StringComparison.OrdinalIgnoreCase));
            if (match == null) return null;

            // Prefer login password, then fields, then notes
            if (match.Login?.Password is {Length:>0} pwd)
                return pwd;

            var pwField = match.Fields?.FirstOrDefault(f => string.Equals(f.Name, "password", StringComparison.OrdinalIgnoreCase));
            if (pwField?.Value is {Length:>0} fv)
                return fv;

            if (!string.IsNullOrWhiteSpace(match.Notes))
                return match.Notes;

            return null;
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException($"Failed to get secret '{key}' from BitWarden", ex);
        }
    }

    public async Task<IDictionary<string, string?>> GetSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, string?>();
        var items = await GetAllCiphersAsync(cancellationToken);

        var lookup = items.ToDictionary(i => i.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        foreach (var key in keys)
        {
            if (lookup.TryGetValue(key ?? string.Empty, out var item))
            {
                var value = item.Login?.Password ?? item.Fields?.FirstOrDefault(f => string.Equals(f.Name, "password", StringComparison.OrdinalIgnoreCase))?.Value ?? item.Notes;
                results[key] = value;
            }
            else
            {
                results[key] = null;
            }
        }

        return results;
    }

    public Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        throw new SecretsProviderException("Set operation is not supported by the BitWarden adapter.");
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        throw new SecretsProviderException("Delete operation is not supported by the BitWarden adapter.");
    }

    public async Task<IEnumerable<string>> ListSecretsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var items = await GetAllCiphersAsync(cancellationToken);
            return items.Select(i => i.Name ?? string.Empty);
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException("Failed to list secrets from BitWarden", ex);
        }
    }

    private async Task<List<CipherDto>> GetAllCiphersAsync(CancellationToken cancellationToken)
    {
        // The VaultWarden/Bitwarden API exposes ciphers at /api/ciphers
        // This adapter performs a simple call and attempts to map the minimal fields.
        var response = await _httpClient.GetAsync("/api/ciphers", cancellationToken);
        response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<List<CipherDto>>(cancellationToken: cancellationToken);
        return items ?? new List<CipherDto>();
    }

    private class CipherDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public LoginDto? Login { get; set; }
        public string? Notes { get; set; }
        public List<FieldDto>? Fields { get; set; }
    }

    private class LoginDto
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
    }

    private class FieldDto
    {
        public string? Name { get; set; }
        public string? Value { get; set; }
    }
}
