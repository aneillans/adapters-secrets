namespace Neillans.Adapters.Secrets.Core;

/// <summary>
/// Factory for creating secrets providers.
/// </summary>
public interface ISecretsProviderFactory
{
    /// <summary>
    /// Creates a secrets provider of the specified type.
    /// </summary>
    /// <param name="providerType">The type of provider to create.</param>
    /// <returns>An instance of the secrets provider.</returns>
    ISecretsProvider CreateProvider(SecretsProviderType providerType);
}
