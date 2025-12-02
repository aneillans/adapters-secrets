using Microsoft.Extensions.DependencyInjection;

namespace Neillans.Adapters.Secrets.Core;

/// <summary>
/// Extension methods for configuring secrets providers in dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the secrets provider factory to the service collection.
    /// </summary>
    public static IServiceCollection AddSecretsProviderFactory(this IServiceCollection services)
    {
        services.AddSingleton<ISecretsProviderFactory, SecretsProviderFactory>();
        return services;
    }
}
