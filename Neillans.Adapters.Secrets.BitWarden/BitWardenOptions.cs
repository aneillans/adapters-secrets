namespace Neillans.Adapters.Secrets.BitWarden;

/// <summary>
/// Configuration options for BitWarden / VaultWarden.
/// </summary>
public class BitWardenOptions
{
    /// <summary>
    /// The base URL of the VaultWarden/BitWarden server (e.g. https://vault.example.com).
    /// </summary>
    public string ServerUrl { get; set; } = "https://127.0.0.1";

    /// <summary>
    /// API key or token to authenticate requests. The adapter will attach this as a bearer token.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
