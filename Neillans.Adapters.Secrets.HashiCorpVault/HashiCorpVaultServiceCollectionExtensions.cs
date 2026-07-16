using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Neillans.Adapters.Secrets.Core;

namespace Neillans.Adapters.Secrets.HashiCorpVault;

/// <summary>
/// Extension methods for configuring the HashiCorp Vault secrets provider.
/// </summary>
public static class HashiCorpVaultServiceCollectionExtensions
{
    /// <summary>
    /// Adds the HashiCorp Vault secrets provider to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration action for HashiCorp Vault options.</param>
    public static IServiceCollection AddHashiCorpVaultSecretsProvider(
        this IServiceCollection services,
        Action<HashiCorpVaultOptions> configure)
    {
        services.Configure(configure);
        services.TryAddScoped<ISecretsProvider, HashiCorpVaultSecretsProvider>();
        return services;
    }
}
