using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Neillans.Adapters.Secrets.AzureKeyVault;
using Neillans.Adapters.Secrets.Core;

namespace Neillans.Adapters.Secrets.AzureKeyVault;

/// <summary>
/// Extension methods for configuring Azure Key Vault secrets provider.
/// </summary>
public static class AzureKeyVaultServiceCollectionExtensions
{
    /// <summary>
    /// Adds Azure Key Vault secrets provider to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration action for Azure Key Vault options.</param>
    public static IServiceCollection AddAzureKeyVaultSecretsProvider(
        this IServiceCollection services,
        Action<AzureKeyVaultOptions> configure)
    {
        services.Configure(configure);
        services.TryAddScoped<ISecretsProvider, AzureKeyVaultSecretsProvider>();
        return services;
    }
}
