namespace Neillans.Adapters.Secrets.Infisical;

/// <summary>
/// Configuration options for Infisical.
/// </summary>
public class InfisicalOptions
{
    /// <summary>
    /// The Infisical site URL (e.g., https://app.infisical.com).
    /// </summary>
    public string SiteUrl { get; set; } = "https://app.infisical.com";

    /// <summary>
    /// The client ID for authentication.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// The client secret for authentication.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// The project ID where secrets are stored.
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// The environment name (e.g., dev, staging, prod).
    /// </summary>
    public string Environment { get; set; } = "dev";

    /// <summary>
    /// Optional secret path (folder path within the environment).
    /// </summary>
    public string? SecretPath { get; set; } = "/";
}
