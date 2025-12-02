using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using Neillans.Adapters.Secrets.Core;

namespace Neillans.Adapters.Secrets.AzureKeyVault;

/// <summary>
/// Azure Key Vault implementation of the secrets provider.
/// </summary>
public class AzureKeyVaultSecretsProvider : ISecretsProvider
{
    private readonly SecretClient _client;

    public AzureKeyVaultSecretsProvider(IOptions<AzureKeyVaultOptions> options)
    {
        if (options?.Value == null)
            throw new ArgumentNullException(nameof(options));

        var config = options.Value;
        
        if (string.IsNullOrWhiteSpace(config.VaultUri))
            throw new ArgumentException("VaultUri is required", nameof(options));

        var credential = CreateCredential(config);
        _client = new SecretClient(new Uri(config.VaultUri), credential);
    }

    private static Azure.Core.TokenCredential CreateCredential(AzureKeyVaultOptions config)
    {
        // If client credentials are provided, use ClientSecretCredential
        if (!string.IsNullOrWhiteSpace(config.ClientId) && 
            !string.IsNullOrWhiteSpace(config.ClientSecret) &&
            !string.IsNullOrWhiteSpace(config.TenantId))
        {
            return new ClientSecretCredential(config.TenantId, config.ClientId, config.ClientSecret);
        }

        // Otherwise, use DefaultAzureCredential (supports managed identity, Azure CLI, etc.)
        return new DefaultAzureCredential();
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.GetSecretAsync(key, cancellationToken: cancellationToken);
            return response.Value.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException($"Failed to get secret '{key}' from Azure Key Vault", ex);
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

        var secretValues = await Task.WhenAll(tasks);
        foreach (var (key, value) in secretValues)
        {
            results[key] = value;
        }

        return results;
    }

    public async Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.SetSecretAsync(key, value, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException($"Failed to set secret '{key}' in Azure Key Vault", ex);
        }
    }

    public async Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var operation = await _client.StartDeleteSecretAsync(key, cancellationToken);
            await operation.WaitForCompletionAsync(cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Secret doesn't exist, consider it deleted
            return;
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException($"Failed to delete secret '{key}' from Azure Key Vault", ex);
        }
    }

    public async Task<IEnumerable<string>> ListSecretsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var secrets = new List<string>();
            await foreach (var secretProperties in _client.GetPropertiesOfSecretsAsync(cancellationToken))
            {
                secrets.Add(secretProperties.Name);
            }
            return secrets;
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException("Failed to list secrets from Azure Key Vault", ex);
        }
    }
}
