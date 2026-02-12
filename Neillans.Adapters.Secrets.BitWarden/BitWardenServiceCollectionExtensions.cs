using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Neillans.Adapters.Secrets.Core;

namespace Neillans.Adapters.Secrets.BitWarden;

/// <summary>
/// Extension methods for configuring BitWarden secrets provider.
/// </summary>
public static class BitWardenServiceCollectionExtensions
{
    /// <summary>
    /// Adds BitWarden secrets provider to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration action for BitWarden options.</param>
    public static IServiceCollection AddBitWardenSecretsProvider(
        this IServiceCollection services,
        Action<BitWardenOptions> configure)
    {
        services.Configure(configure);
        services.TryAddScoped<ISecretsProvider, BitWardenSecretsProvider>();
        return services;
    }
}
