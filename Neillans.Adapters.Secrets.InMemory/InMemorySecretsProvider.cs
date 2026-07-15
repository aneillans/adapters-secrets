using System.Collections.Concurrent;
using Neillans.Adapters.Secrets.Core;

namespace Neillans.Adapters.Secrets.InMemory;

/// <summary>
/// Non-persistent, in-process implementation of the secrets provider. Intended for tests
/// and local/ephemeral runs where no real secrets backend is required.
/// </summary>
public class InMemorySecretsProvider : ISecretsProvider
{
    private readonly ConcurrentDictionary<string, string> _store = new();

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.TryGetValue(key, out var value) ? value : null);

    public async Task<IDictionary<string, string?>> GetSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, string?>();
        foreach (var key in keys)
        {
            results[key] = await GetSecretAsync(key, cancellationToken);
        }

        return results;
    }

    public Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<string>> ListSecretsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<string>>(_store.Keys.ToList());
}
