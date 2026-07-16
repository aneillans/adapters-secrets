namespace Neillans.Adapters.Secrets.BitWarden;

/// <summary>
/// Configuration options for BitWarden / VaultWarden.
///
/// Reading vault items requires a PERSONAL API key (a "user.{guid}" client id/secret pair) plus
/// the account <see cref="Email"/> and <see cref="MasterPassword"/>. The API key authenticates the
/// session, but the vault is end-to-end encrypted: the decryption key is derived from the master
/// password + email, so both are mandatory. An Organization API Key ("organization.*") cannot be
/// used here - it only grants the Public API (members/collections/groups/policies), not vault items.
/// </summary>
public class BitWardenOptions
{
    /// <summary>
    /// The base URL of the VaultWarden/BitWarden server (e.g. https://vault.example.com).
    /// </summary>
    public string ServerUrl { get; set; } = "https://127.0.0.1";

    /// <summary>
    /// The client id portion of a personal API key, formatted as "user.{guid}". Obtain it from the
    /// web vault under Account Settings &gt; Security &gt; Keys &gt; View API Key.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// The client secret portion of the personal API key.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// The account email address. Used both to log in and as the salt when deriving the master key.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// The account master password. This is the only source of the vault decryption key; without it
    /// the server's ciphertext cannot be decrypted.
    /// </summary>
    public string MasterPassword { get; set; } = string.Empty;

    /// <summary>
    /// The identity server URL used to prelogin and exchange the API key for an access token
    /// (e.g. https://identity.bitwarden.com, or https://vault.example.com/identity for self-hosted
    /// VaultWarden/BitWarden servers). Defaults to "{ServerUrl}/identity" when not specified.
    /// </summary>
    public string? IdentityUrl { get; set; }
}
