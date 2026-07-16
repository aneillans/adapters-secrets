namespace Neillans.Adapters.Secrets.Core;

/// <summary>
/// Defines the supported secrets provider types.
/// </summary>
public enum SecretsProviderType
{
    /// <summary>
    /// Azure Key Vault provider.
    /// </summary>
    AzureKeyVault,

    /// <summary>
    /// Infisical provider.
    /// </summary>
    Infisical,
    
    /// <summary>
    /// BitWarden / VaultWarden provider.
    /// </summary>
    BitWarden,

    /// <summary>
    /// HashiCorp Vault provider (KV v2 secrets engine).
    /// </summary>
    HashiCorpVault,

    /// <summary>
    /// Non-persistent, in-process provider. Intended for tests and local/ephemeral runs.
    /// </summary>
    InMemory
}
