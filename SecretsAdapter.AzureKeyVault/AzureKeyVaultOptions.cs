namespace Neillans.Adapters.Secrets.AzureKeyVault;

/// <summary>
/// Configuration options for Azure Key Vault.
/// </summary>
public class AzureKeyVaultOptions
{
    /// <summary>
    /// The Key Vault URI (e.g., https://myvault.vault.azure.net/).
    /// </summary>
    public string VaultUri { get; set; } = string.Empty;

    /// <summary>
    /// Optional tenant ID for authentication.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Optional client ID for service principal authentication.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Optional client secret for service principal authentication.
    /// </summary>
    public string? ClientSecret { get; set; }
}
