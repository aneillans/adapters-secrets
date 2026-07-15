using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Neillans.Adapters.Secrets.Core;

namespace Neillans.Adapters.Secrets.InMemory;

/// <summary>
/// Extension methods for configuring the in-memory secrets provider.
/// </summary>
public static class InMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Adds the in-memory secrets provider to the service collection. Registered as a
    /// singleton so that secrets set on one instance remain visible to later resolutions
    /// for the lifetime of the process.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddInMemorySecretsProvider(this IServiceCollection services)
    {
        services.TryAddSingleton<ISecretsProvider, InMemorySecretsProvider>();
        return services;
    }
}
