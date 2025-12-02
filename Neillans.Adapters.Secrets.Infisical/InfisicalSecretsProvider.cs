using Infisical.Sdk;
using Infisical.Sdk.Model;
using Microsoft.Extensions.Options;
using Neillans.Adapters.Secrets.Core;

namespace Neillans.Adapters.Secrets.Infisical;

/// <summary>
/// Infisical implementation of the secrets provider.
/// </summary>
public class InfisicalSecretsProvider : ISecretsProvider
{
    private readonly InfisicalClient _client;
    private readonly InfisicalOptions _options;

    public InfisicalSecretsProvider(IOptions<InfisicalOptions> options)
    {
        if (options?.Value == null)
            throw new ArgumentNullException(nameof(options));

        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new ArgumentException("ClientId is required", nameof(options));

        if (string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new ArgumentException("ClientSecret is required", nameof(options));

        if (string.IsNullOrWhiteSpace(_options.ProjectId))
            throw new ArgumentException("ProjectId is required", nameof(options));

        var settings = new InfisicalSdkSettingsBuilder()
            .WithHostUri(_options.SiteUrl)
            .Build();

        _client = new InfisicalClient(settings);
        
        // Authenticate using Universal Auth
        _client.Auth().UniversalAuth().LoginAsync(_options.ClientId, _options.ClientSecret).GetAwaiter().GetResult();
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var getSecretOptions = new GetSecretOptions
            {
                ProjectId = _options.ProjectId,
                EnvironmentSlug = _options.Environment,
                SecretName = key,
                SecretPath = _options.SecretPath ?? "/"
            };

            var secret = await _client.Secrets().GetAsync(getSecretOptions);
            return secret?.SecretValue;
        }
        catch (Exception ex) when (ex.Message.Contains("not found") || ex.Message.Contains("404"))
        {
            return null;
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException($"Failed to get secret '{key}' from Infisical", ex);
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
            var createSecretOptions = new CreateSecretOptions
            {
                ProjectId = _options.ProjectId,
                EnvironmentSlug = _options.Environment,
                SecretName = key,
                SecretValue = value,
                SecretPath = _options.SecretPath ?? "/"
            };

            await _client.Secrets().CreateAsync(createSecretOptions);
        }
        catch (Exception ex) when (ex.Message.Contains("already exists"))
        {
            // Secret exists, update it instead
            try
            {
                var updateSecretOptions = new UpdateSecretOptions
                {
                    ProjectId = _options.ProjectId,
                    EnvironmentSlug = _options.Environment,
                    SecretName = key,
                    NewSecretValue = value,
                    SecretPath = _options.SecretPath ?? "/"
                };

                await _client.Secrets().UpdateAsync(updateSecretOptions);
            }
            catch (Exception updateEx)
            {
                throw new SecretsProviderException($"Failed to update secret '{key}' in Infisical", updateEx);
            }
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException($"Failed to set secret '{key}' in Infisical", ex);
        }
    }

    public async Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var deleteSecretOptions = new DeleteSecretOptions
            {
                ProjectId = _options.ProjectId,
                EnvironmentSlug = _options.Environment,
                SecretName = key,
                SecretPath = _options.SecretPath ?? "/"
            };

            await _client.Secrets().DeleteAsync(deleteSecretOptions);
        }
        catch (Exception ex) when (ex.Message.Contains("not found") || ex.Message.Contains("404"))
        {
            // Secret doesn't exist, consider it deleted
            return;
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException($"Failed to delete secret '{key}' from Infisical", ex);
        }
    }

    public async Task<IEnumerable<string>> ListSecretsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var listSecretsOptions = new ListSecretsOptions
            {
                ProjectId = _options.ProjectId,
                EnvironmentSlug = _options.Environment,
                SecretPath = _options.SecretPath ?? "/"
            };

            var secrets = await _client.Secrets().ListAsync(listSecretsOptions);
            return secrets?.Select(s => s.SecretKey) ?? Enumerable.Empty<string>();
        }
        catch (Exception ex)
        {
            throw new SecretsProviderException("Failed to list secrets from Infisical", ex);
        }
    }
}
