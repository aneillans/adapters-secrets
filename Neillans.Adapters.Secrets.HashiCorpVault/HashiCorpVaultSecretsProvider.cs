using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Neillans.Adapters.Secrets.Core;
using VaultSharp;
using VaultSharp.Core;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.AppRole;
using VaultSharp.V1.AuthMethods.Token;

namespace Neillans.Adapters.Secrets.HashiCorpVault;

/// <summary>
/// HashiCorp Vault implementation of the secrets provider, backed by the KV v2 secrets engine.
///
/// A "secret" maps to a Vault path (<see cref="HashiCorpVaultOptions.BasePath"/> + key) under the
/// configured mount; its value is the <see cref="HashiCorpVaultOptions.ValueKey"/> field of the KV
/// item (or the sole field, if there is exactly one). Writes replace the path's data with a single
/// value field.
/// </summary>
public class HashiCorpVaultSecretsProvider : ISecretsProvider
{
    private readonly IVaultClient _client;
    private readonly HashiCorpVaultOptions _options;
    private readonly string _mountPoint;
    private readonly string _basePath;

    public HashiCorpVaultSecretsProvider(IOptions<HashiCorpVaultOptions> options)
    {
        if (options?.Value == null)
            throw new ArgumentNullException(nameof(options));

        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.VaultAddress))
            throw new ArgumentException("VaultAddress is required", nameof(options));
        if (string.IsNullOrWhiteSpace(_options.ValueKey))
            throw new ArgumentException("ValueKey is required", nameof(options));

        var authMethod = CreateAuthMethod(_options);

        var settings = new VaultClientSettings(_options.VaultAddress, authMethod);
        if (!string.IsNullOrWhiteSpace(_options.Namespace))
            settings.Namespace = _options.Namespace;

        _client = new VaultClient(settings);
        _mountPoint = _options.MountPoint.Trim('/');
        _basePath = _options.BasePath.Trim('/');
    }

    private static IAuthMethodInfo CreateAuthMethod(HashiCorpVaultOptions config)
    {
        var hasToken = !string.IsNullOrWhiteSpace(config.Token);
        var hasAppRole = !string.IsNullOrWhiteSpace(config.RoleId) && !string.IsNullOrWhiteSpace(config.SecretId);

        if (hasToken && hasAppRole)
            throw new ArgumentException("Provide either a Token or an AppRole (RoleId + SecretId), not both.");
        if (hasToken)
            return new TokenAuthMethodInfo(config.Token);
        if (hasAppRole)
            return new AppRoleAuthMethodInfo(config.RoleId, config.SecretId);

        throw new ArgumentException("Authentication is required: set Token, or RoleId + SecretId for AppRole auth.");
    }

    /// <summary>Combines the base path and key into the KV item path.</summary>
    private string PathFor(string key)
    {
        var trimmedKey = (key ?? string.Empty).Trim('/');
        return string.IsNullOrEmpty(_basePath) ? trimmedKey : $"{_basePath}/{trimmedKey}";
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var secret = await _client.V1.Secrets.KeyValue.V2.ReadSecretAsync(
                path: PathFor(key), mountPoint: _mountPoint);

            var data = secret?.Data?.Data;
            if (data == null || data.Count == 0)
                return null;

            if (data.TryGetValue(_options.ValueKey, out var value))
                return ConvertValue(value);

            // Fall back to the sole field when the configured value key is absent.
            return data.Count == 1 ? ConvertValue(data.Values.First()) : null;
        }
        catch (VaultApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException($"Failed to get secret '{key}' from HashiCorp Vault", ex);
        }
    }

    public async Task<IDictionary<string, string?>> GetSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, string?>();
        var tasks = keys.Select(async key =>
        {
            var value = await GetSecretAsync(key, cancellationToken);
            return (key, value);
        });

        foreach (var (key, value) in await Task.WhenAll(tasks))
            results[key] = value;

        return results;
    }

    public async Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        try
        {
            var data = new Dictionary<string, object> { [_options.ValueKey] = value };
            await _client.V1.Secrets.KeyValue.V2.WriteSecretAsync(
                path: PathFor(key), data: data, mountPoint: _mountPoint);
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException($"Failed to set secret '{key}' in HashiCorp Vault", ex);
        }
    }

    public async Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            // Deletes every version and the metadata, i.e. removes the secret entirely.
            await _client.V1.Secrets.KeyValue.V2.DeleteMetadataAsync(
                path: PathFor(key), mountPoint: _mountPoint);
        }
        catch (VaultApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            // Already absent; treat as deleted.
            return;
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException($"Failed to delete secret '{key}' from HashiCorp Vault", ex);
        }
    }

    public async Task<IEnumerable<string>> ListSecretsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var listPath = _basePath; // "" lists the root of the mount
            var result = await _client.V1.Secrets.KeyValue.V2.ReadSecretPathsAsync(
                path: listPath, mountPoint: _mountPoint);

            var keys = result?.Data?.Keys ?? Enumerable.Empty<string>();
            // Drop sub-folder entries (Vault suffixes those with '/'); keep leaf secret names.
            return keys.Where(k => !k.EndsWith('/')).ToList();
        }
        catch (VaultApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            // No secrets under the path yet.
            return Enumerable.Empty<string>();
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException("Failed to list secrets from HashiCorp Vault", ex);
        }
    }

    /// <summary>KV values deserialize as <see cref="object"/> (often a <see cref="JsonElement"/>); coerce to a string.</summary>
    private static string? ConvertValue(object? value) => value switch
    {
        null => null,
        string s => s,
        JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() : je.GetRawText(),
        _ => value.ToString()
    };
}
