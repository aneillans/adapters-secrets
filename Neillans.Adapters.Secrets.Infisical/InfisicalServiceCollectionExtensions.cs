using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Neillans.Adapters.Secrets.Core;
using Neillans.Adapters.Secrets.Infisical;

namespace Neillans.Adapters.Secrets.Infisical;

/// <summary>
/// Extension methods for configuring Infisical secrets provider.
/// </summary>
public static class InfisicalServiceCollectionExtensions
{
    /// <summary>
    /// Adds Infisical secrets provider to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration action for Infisical options.</param>
    public static IServiceCollection AddInfisicalSecretsProvider(
        this IServiceCollection services,
        Action<InfisicalOptions> configure)
    {
        services.Configure(configure);
        services.TryAddScoped<ISecretsProvider, InfisicalSecretsProvider>();
        return services;
    }
}
