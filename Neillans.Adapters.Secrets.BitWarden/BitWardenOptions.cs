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
    /// A pre-issued access/API token to authenticate requests. The adapter will attach this as a
    /// static bearer token. Mutually exclusive with <see cref="ClientId"/>/<see cref="ClientSecret"/>.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The client id portion of a BitWarden Organization API Key (e.g. "organization.{guid}").
    /// When supplied together with <see cref="ClientSecret"/>, the adapter authenticates using
    /// the OAuth2 client_credentials grant against <see cref="IdentityUrl"/> instead of using a
    /// static <see cref="ApiKey"/>.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// The client secret portion of a BitWarden Organization API Key.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// The identity server URL used to exchange the Organization API Key for an access token
    /// (e.g. https://identity.bitwarden.com, or https://vault.example.com/identity for self-hosted
    /// VaultWarden/BitWarden servers). Defaults to "{ServerUrl}/identity" when not specified.
    /// </summary>
    public string? IdentityUrl { get; set; }

    /// <summary>
    /// Optional organization id. When authenticating with an Organization API Key, this scopes
    /// vault operations (list/get/set) to the specified organization's vault instead of the
    /// individual/personal vault.
    /// </summary>
    public string? OrganizationId { get; set; }

    /// <summary>
    /// OAuth2 scope requested when authenticating with an Organization API Key.
    /// </summary>
    public string Scope { get; set; } = "api.organization";
}
