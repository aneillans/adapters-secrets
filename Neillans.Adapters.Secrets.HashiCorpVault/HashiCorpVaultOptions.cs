namespace Neillans.Adapters.Secrets.HashiCorpVault;

/// <summary>
/// Configuration options for HashiCorp Vault (KV v2 secrets engine).
///
/// Each logical secret maps to a Vault path under <see cref="MountPoint"/> (optionally prefixed by
/// <see cref="BasePath"/>). The KV item at that path is expected to hold the value under a single
/// field named by <see cref="ValueKey"/> (the adapter falls back to the sole field if there is
/// exactly one). Writes replace the path's data with a single <see cref="ValueKey"/> field.
///
/// Authenticate with either a <see cref="Token"/> (token auth) or an AppRole
/// (<see cref="RoleId"/> + <see cref="SecretId"/>). Exactly one of the two must be supplied.
/// </summary>
public class HashiCorpVaultOptions
{
    /// <summary>
    /// The Vault server address, e.g. https://vault.example.com:8200.
    /// </summary>
    public string VaultAddress { get; set; } = string.Empty;

    /// <summary>
    /// A Vault token for token authentication. Provide this OR an AppRole
    /// (<see cref="RoleId"/> + <see cref="SecretId"/>).
    /// </summary>
    public string? Token { get; set; }

    /// <summary>The AppRole RoleId. Provide with <see cref="SecretId"/> to use AppRole auth.</summary>
    public string? RoleId { get; set; }

    /// <summary>The AppRole SecretId. Provide with <see cref="RoleId"/> to use AppRole auth.</summary>
    public string? SecretId { get; set; }

    /// <summary>
    /// The mount point of the KV v2 secrets engine. Defaults to "secret" (the dev-server default).
    /// </summary>
    public string MountPoint { get; set; } = "secret";

    /// <summary>
    /// Optional path prefix within the mount that every key is stored under (e.g. "apps/myservice").
    /// Empty means keys live at the root of the mount.
    /// </summary>
    public string BasePath { get; set; } = string.Empty;

    /// <summary>
    /// The field within each KV item that holds the secret value. Defaults to "value".
    /// </summary>
    public string ValueKey { get; set; } = "value";

    /// <summary>
    /// Optional Vault namespace (Vault Enterprise / HCP Vault). Ignored by open-source Vault.
    /// </summary>
    public string? Namespace { get; set; }
}
