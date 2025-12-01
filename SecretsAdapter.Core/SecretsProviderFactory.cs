using Microsoft.Extensions.DependencyInjection;

namespace Neillans.Adapters.Secrets.Core;

/// <summary>
/// Factory implementation for creating secrets providers.
/// </summary>
public class SecretsProviderFactory : ISecretsProviderFactory
{
    private readonly IServiceProvider _serviceProvider;

    public SecretsProviderFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public ISecretsProvider CreateProvider(SecretsProviderType providerType)
    {
        return providerType switch
        {
            SecretsProviderType.AzureKeyVault => _serviceProvider.GetRequiredService<ISecretsProvider>(),
            SecretsProviderType.Infisical => _serviceProvider.GetRequiredService<ISecretsProvider>(),
            _ => throw new NotSupportedException($"Provider type '{providerType}' is not supported.")
        };
    }
}
