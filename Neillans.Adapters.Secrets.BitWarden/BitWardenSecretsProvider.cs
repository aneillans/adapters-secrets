using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Neillans.Adapters.Secrets.Core;

namespace Neillans.Adapters.Secrets.BitWarden;

/// <summary>
/// BitWarden / VaultWarden implementation of the secrets provider.
/// This implementation uses the server HTTP API to list, read and write cipher items.
/// Authentication can be performed either with a static API key/token, or by logging in with a
/// BitWarden Organization API Key (client id/secret) via the OAuth2 client_credentials grant.
/// </summary>
public class BitWardenSecretsProvider : ISecretsProvider
{
    private readonly HttpClient _httpClient;
    private readonly BitWardenOptions _options;
    private readonly bool _usesOrganizationApiKey;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    public BitWardenSecretsProvider(IOptions<BitWardenOptions> options)
    {
        if (options?.Value == null)
            throw new ArgumentNullException(nameof(options));

        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.ServerUrl))
            throw new ArgumentException("ServerUrl is required", nameof(options));

        var hasApiKey = !string.IsNullOrWhiteSpace(_options.ApiKey);
        var hasClientCredentials = !string.IsNullOrWhiteSpace(_options.ClientId) && !string.IsNullOrWhiteSpace(_options.ClientSecret);

        if (!hasApiKey && !hasClientCredentials)
            throw new ArgumentException("Either ApiKey, or both ClientId and ClientSecret (Organization API Key), are required", nameof(options));

        if (hasApiKey && hasClientCredentials)
            throw new ArgumentException("Specify either ApiKey or ClientId/ClientSecret, not both", nameof(options));

        _usesOrganizationApiKey = hasClientCredentials;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_options.ServerUrl.TrimEnd('/') + "/")
        };

        if (!_usesOrganizationApiKey)
        {
            // Attach the static bearer token up-front; no login exchange is required.
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var items = await GetAllCiphersAsync(cancellationToken);
            var match = items.FirstOrDefault(i => string.Equals(i.Name, key, StringComparison.OrdinalIgnoreCase));
            return match == null ? null : ExtractValue(match);
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException($"Failed to get secret '{key}' from BitWarden", ex);
        }
    }

    public async Task<IDictionary<string, string?>> GetSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        try
        {
            var results = new Dictionary<string, string?>();
            var items = await GetAllCiphersAsync(cancellationToken);

            var lookup = items.ToDictionary(i => i.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            foreach (var key in keys)
            {
                results[key] = lookup.TryGetValue(key ?? string.Empty, out var item) ? ExtractValue(item) : null;
            }

            return results;
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException("Failed to get secrets from BitWarden", ex);
        }
    }

    public async Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key is required", nameof(key));

        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);

            var items = await GetAllCiphersAsync(cancellationToken);
            var existing = items.FirstOrDefault(i => string.Equals(i.Name, key, StringComparison.OrdinalIgnoreCase));

            if (existing != null && !string.IsNullOrWhiteSpace(existing.Id))
            {
                existing.Notes = value;
                var updateResponse = await _httpClient.PutAsJsonAsync($"api/ciphers/{existing.Id}", existing, cancellationToken);
                updateResponse.EnsureSuccessStatusCode();
            }
            else
            {
                var cipher = new CipherDto
                {
                    Name = key,
                    Type = 2, // Secure note
                    Notes = value,
                    OrganizationId = _options.OrganizationId
                };

                var createResponse = await _httpClient.PostAsJsonAsync("api/ciphers", cipher, cancellationToken);
                createResponse.EnsureSuccessStatusCode();
            }
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException($"Failed to set secret '{key}' in BitWarden", ex);
        }
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

    private static string? ExtractValue(CipherDto item)
    {
        if (item.Login?.Password is { Length: > 0 } pwd)
            return pwd;

        var pwField = item.Fields?.FirstOrDefault(f => string.Equals(f.Name, "password", StringComparison.OrdinalIgnoreCase));
        if (pwField?.Value is { Length: > 0 } fv)
            return fv;

        return !string.IsNullOrWhiteSpace(item.Notes) ? item.Notes : null;
    }

    private async Task<List<CipherDto>> GetAllCiphersAsync(CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        // The VaultWarden/Bitwarden API exposes ciphers at /api/ciphers. When operating with an
        // Organization API Key and an OrganizationId is configured, scope the request to that
        // organization's vault instead of the individual/personal vault.
        var path = !string.IsNullOrWhiteSpace(_options.OrganizationId)
            ? $"api/organizations/{_options.OrganizationId}/ciphers"
            : "api/ciphers";

        var response = await _httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CipherListResponseDto>(cancellationToken: cancellationToken);
        if (result?.Data != null)
            return result.Data;

        var items = await response.Content.ReadFromJsonAsync<List<CipherDto>>(cancellationToken: cancellationToken);
        return items ?? new List<CipherDto>();
    }

    /// <summary>
    /// Ensures a valid access token is attached to the HTTP client. No-op when using a static
    /// ApiKey. When using an Organization API Key, logs in (or refreshes) via the OAuth2
    /// client_credentials grant if there is no token or the current one has expired.
    /// </summary>
    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (!_usesOrganizationApiKey)
            return;

        if (_accessToken != null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
            return;

        await _authLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
                return;

            var identityUrl = (_options.IdentityUrl ?? $"{_options.ServerUrl.TrimEnd('/')}/identity").TrimEnd('/');

            using var identityClient = new HttpClient();
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId!,
                ["client_secret"] = _options.ClientSecret!,
                ["scope"] = _options.Scope,
                ["deviceType"] = "21", // SDK
                ["deviceName"] = "Neillans.Adapters.Secrets.BitWarden",
                ["deviceIdentifier"] = Guid.NewGuid().ToString()
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{identityUrl}/connect/token")
            {
                Content = new FormUrlEncodedContent(form)
            };

            using var response = await identityClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<TokenResponseDto>(cancellationToken: cancellationToken);
            if (token?.AccessToken is not { Length: > 0 })
                throw new SecretsProviderException("BitWarden Organization API Key login did not return an access token");

            _accessToken = token.AccessToken;
            // Refresh a little early to avoid using a token that expires mid-request.
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 60, 60));

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }
        catch (SecretsProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException("Failed to authenticate with BitWarden using the Organization API Key", ex);
        }
        finally
        {
            _authLock.Release();
        }
    }

    private class TokenResponseDto
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; } = 3600;

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }

    private class CipherListResponseDto
    {
        [JsonPropertyName("data")]
        public List<CipherDto>? Data { get; set; }
    }

    private class CipherDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public int Type { get; set; } = 2;
        public LoginDto? Login { get; set; }
        public string? Notes { get; set; }
        public List<FieldDto>? Fields { get; set; }
        public string? OrganizationId { get; set; }
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
