namespace Neillans.Adapters.Secrets.Core;

/// <summary>
/// Configuration for selecting and configuring a secrets provider.
/// </summary>
public class SecretsAdapterConfiguration
{
    /// <summary>
    /// The type of secrets provider to use.
    /// </summary>
    public SecretsProviderType ProviderType { get; set; }
}
