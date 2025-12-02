namespace Neillans.Adapters.Secrets.Core;

/// <summary>
/// Defines the contract for secrets providers.
/// </summary>
public interface ISecretsProvider
{
    /// <summary>
    /// Retrieves a secret value by its key.
    /// </summary>
    /// <param name="key">The key or name of the secret.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The secret value.</returns>
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves multiple secrets by their keys.
    /// </summary>
    /// <param name="keys">The keys or names of the secrets.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary of key-value pairs.</returns>
    Task<IDictionary<string, string?>> GetSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or updates a secret value.
    /// </summary>
    /// <param name="key">The key or name of the secret.</param>
    /// <param name="value">The secret value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a secret by its key.
    /// </summary>
    /// <param name="key">The key or name of the secret.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all available secret keys.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of secret keys.</returns>
    Task<IEnumerable<string>> ListSecretsAsync(CancellationToken cancellationToken = default);
}
