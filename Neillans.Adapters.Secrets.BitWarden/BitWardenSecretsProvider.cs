using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Neillans.Adapters.Secrets.Core;

namespace Neillans.Adapters.Secrets.BitWarden;

/// <summary>
/// BitWarden / VaultWarden implementation of the secrets provider.
///
/// Reads vault items from the Password Manager API. Because the vault is end-to-end encrypted, the
/// provider authenticates with a personal API key, derives the vault keys client-side from the
/// account email + master password, and decrypts each item locally:
///   prelogin (KDF params) -> derive master key -> connect/token -> /api/sync -> unwrap keys -> decrypt.
///
/// A "secret" maps to a vault item's name; its value is taken from the login password, then a custom
/// field named "password", then the secure-note contents.
/// </summary>
public class BitWardenSecretsProvider : ISecretsProvider
{
    private readonly HttpClient _httpClient;
    private readonly BitWardenOptions _options;
    private readonly string _serverBase;
    private readonly string _identityUrl;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    // Derived once from the master password (KDF params come from prelogin).
    private byte[]? _stretchEnc;
    private byte[]? _stretchMac;

    // Unwrapped once from the first sync's profile.
    private byte[]? _userSymKey;
    private Dictionary<string, byte[]>? _orgKeys;

    public BitWardenSecretsProvider(IOptions<BitWardenOptions> options)
    {
        if (options?.Value == null)
            throw new ArgumentNullException(nameof(options));

        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.ServerUrl))
            throw new ArgumentException("ServerUrl is required", nameof(options));
        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new ArgumentException("ClientId is required (a personal API key, formatted as \"user.{guid}\")", nameof(options));
        if (!_options.ClientId.StartsWith("user.", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("ClientId must be a personal API key (\"user.{guid}\"). An Organization API Key (\"organization.*\") cannot read vault items.", nameof(options));
        if (string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new ArgumentException("ClientSecret is required", nameof(options));
        if (string.IsNullOrWhiteSpace(_options.Email))
            throw new ArgumentException("Email is required (used to log in and as the key-derivation salt)", nameof(options));
        if (string.IsNullOrWhiteSpace(_options.MasterPassword))
            throw new ArgumentException("MasterPassword is required (it is the only source of the vault decryption key)", nameof(options));

        _serverBase = _options.ServerUrl.TrimEnd('/');
        _identityUrl = (string.IsNullOrWhiteSpace(_options.IdentityUrl) ? $"{_serverBase}/identity" : _options.IdentityUrl).TrimEnd('/');

        _httpClient = new HttpClient { BaseAddress = new Uri(_serverBase + "/") };
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var secrets = await GetAllDecryptedSecretsAsync(cancellationToken);
            return secrets.TryGetValue(key ?? string.Empty, out var value) ? value : null;
        }
        catch (SecretsProviderException)
        {
            throw;
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
            var secrets = await GetAllDecryptedSecretsAsync(cancellationToken);
            var results = new Dictionary<string, string?>();
            foreach (var key in keys)
                results[key] = secrets.TryGetValue(key ?? string.Empty, out var value) ? value : null;
            return results;
        }
        catch (SecretsProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException("Failed to get secrets from BitWarden", ex);
        }
    }

    public Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        // Writing requires client-side encryption of the value plus building an encrypted cipher
        // payload; that path is not implemented. This adapter is read-only.
        throw new SecretsProviderException("Set operation is not supported by the BitWarden adapter (read-only).");
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        throw new SecretsProviderException("Delete operation is not supported by the BitWarden adapter.");
    }

    public async Task<IEnumerable<string>> ListSecretsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var secrets = await GetAllDecryptedSecretsAsync(cancellationToken);
            return secrets.Keys.ToList();
        }
        catch (SecretsProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException("Failed to list secrets from BitWarden", ex);
        }
    }

    /// <summary>
    /// Authenticates (if needed), fetches the encrypted vault, unwraps the keys and returns a
    /// name -&gt; decrypted-value map for every readable item.
    /// </summary>
    private async Task<Dictionary<string, string?>> GetAllDecryptedSecretsAsync(CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var sync = await _httpClient.GetFromJsonAsync<SyncResponseDto>("api/sync?excludeDomains=true", cancellationToken)
                   ?? new SyncResponseDto();

        await EnsureVaultKeysAsync(sync.Profile, cancellationToken);

        var results = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var cipher in sync.Ciphers ?? new List<CipherDto>())
        {
            var key = ResolveKey(cipher.OrganizationId);
            if (key == null)
                continue; // no key available for this cipher's organization

            var enc = key[..32];
            var mac = key[32..64];

            var name = TryDecrypt(cipher.Name, enc, mac);
            if (name == null)
                continue; // cannot identify an item whose name won't decrypt

            var value = TryDecrypt(cipher.Login?.Password, enc, mac)
                        ?? TryDecrypt(cipher.Fields?.FirstOrDefault(f => string.Equals(TryDecrypt(f.Name, enc, mac), "password", StringComparison.OrdinalIgnoreCase))?.Value, enc, mac)
                        ?? TryDecrypt(cipher.Notes, enc, mac);

            // Later duplicates overwrite earlier ones rather than throwing.
            results[name] = value;
        }

        return results;
    }

    private byte[]? ResolveKey(string? organizationId)
    {
        if (string.IsNullOrEmpty(organizationId))
            return _userSymKey;
        return _orgKeys != null && _orgKeys.TryGetValue(organizationId, out var key) ? key : null;
    }

    private static string? TryDecrypt(string? encString, byte[] enc, byte[] mac)
    {
        if (string.IsNullOrEmpty(encString))
            return null;
        try { return BitWardenCrypto.DecryptSymmetricToString(encString, enc, mac); }
        catch { return null; }
    }

    /// <summary>
    /// Ensures a valid access token is attached. Derives (once) the stretched master key from the
    /// master password, then logs in via the personal API key (client_credentials, scope api),
    /// refreshing when the current token has expired.
    /// </summary>
    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
            return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
                return;

            if (_stretchEnc == null)
                await DeriveMasterKeyAsync(cancellationToken);

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["scope"] = "api",
                ["deviceType"] = "21", // SDK
                ["deviceName"] = "Neillans.Adapters.Secrets.BitWarden",
                ["deviceIdentifier"] = Guid.NewGuid().ToString()
            };

            using var identityClient = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_identityUrl}/connect/token")
            {
                Content = new FormUrlEncodedContent(form)
            };
            using var response = await identityClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<TokenResponseDto>(cancellationToken: cancellationToken);
            if (token?.AccessToken is not { Length: > 0 })
                throw new SecretsProviderException("BitWarden login did not return an access token");

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
            throw new SecretsProviderException("Failed to authenticate with BitWarden", ex);
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>Fetches the account KDF parameters and derives + stretches the master key.</summary>
    private async Task DeriveMasterKeyAsync(CancellationToken cancellationToken)
    {
        var email = _options.Email;
        using var identityClient = new HttpClient();
        using var response = await identityClient.PostAsJsonAsync($"{_identityUrl}/accounts/prelogin", new { email }, cancellationToken);
        response.EnsureSuccessStatusCode();

        var prelogin = await response.Content.ReadFromJsonAsync<PreloginResponseDto>(cancellationToken: cancellationToken)
                       ?? new PreloginResponseDto();

        var masterKey = BitWardenCrypto.DeriveMasterKey(
            _options.MasterPassword, email, prelogin.Kdf,
            prelogin.KdfIterations,
            prelogin.KdfMemory ?? 0,
            prelogin.KdfParallelism ?? 0);

        (_stretchEnc, _stretchMac) = BitWardenCrypto.StretchMasterKey(masterKey);
    }

    /// <summary>Unwraps (once) the user symmetric key and any organization keys from the profile.</summary>
    private async Task EnsureVaultKeysAsync(ProfileDto? profile, CancellationToken cancellationToken)
    {
        if (_userSymKey != null)
            return;

        if (profile?.Key is not { Length: > 0 })
            throw new SecretsProviderException("Sync response did not contain a profile key; cannot decrypt the vault.");

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_userSymKey != null)
                return;

            var userSymKey = BitWardenCrypto.DecryptSymmetric(profile.Key, _stretchEnc!, _stretchMac!);
            var orgKeys = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            if (profile.Organizations is { Count: > 0 } && profile.PrivateKey is { Length: > 0 })
            {
                var privateKey = BitWardenCrypto.DecryptSymmetric(profile.PrivateKey, userSymKey[..32], userSymKey[32..64]);
                foreach (var org in profile.Organizations)
                {
                    if (string.IsNullOrEmpty(org.Id) || string.IsNullOrEmpty(org.Key))
                        continue;
                    try
                    {
                        orgKeys[org.Id] = BitWardenCrypto.DecryptAsymmetric(org.Key, privateKey);
                    }
                    catch
                    {
                        // Skip organizations whose key cannot be unwrapped; their ciphers are simply
                        // omitted rather than failing the whole read.
                    }
                }
            }

            _orgKeys = orgKeys;
            _userSymKey = userSymKey; // set last: signals initialization is complete
        }
        catch (SecretsProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException("Failed to unwrap the BitWarden vault keys (check the master password and email)", ex);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private class TokenResponseDto
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; } = 3600;
    }

    private class PreloginResponseDto
    {
        public int Kdf { get; set; }
        public int KdfIterations { get; set; } = 600_000;
        public int? KdfMemory { get; set; }
        public int? KdfParallelism { get; set; }
    }

    private class SyncResponseDto
    {
        public ProfileDto? Profile { get; set; }
        public List<CipherDto>? Ciphers { get; set; }
    }

    private class ProfileDto
    {
        public string? Key { get; set; }
        public string? PrivateKey { get; set; }
        public List<OrgDto>? Organizations { get; set; }
    }

    private class OrgDto
    {
        public string? Id { get; set; }
        public string? Key { get; set; }
    }

    private class CipherDto
    {
        public string? Id { get; set; }
        public string? OrganizationId { get; set; }
        public string? Name { get; set; }
        public string? Notes { get; set; }
        public int Type { get; set; }
        public LoginDto? Login { get; set; }
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
