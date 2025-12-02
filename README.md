# SecretsAdapter

A .NET 10 abstraction library for seamlessly switching between different secrets management providers. This library provides a unified interface for accessing secrets across multiple platforms without requiring consumers to have knowledge of the underlying provider implementations.

## Features

- **Provider Abstraction**: Single interface (`ISecretsProvider`) for all secrets operations
- **Multiple Providers**: Built-in support for:
  - Azure Key Vault
  - Infisical
- **Fully Encapsulated**: All provider-specific dependencies are contained within their respective packages
- **Dependency Injection**: First-class support for Microsoft.Extensions.DependencyInjection
- **Async/Await**: Fully asynchronous API with cancellation token support
- **.NET 10**: Built for the latest .NET platform

## Project Structure

```
adapters-secrets/
├── Neillans.Adapters.Secrets.Core/              # Core abstractions and interfaces
├── Neillans.Adapters.Secrets.AzureKeyVault/     # Azure Key Vault implementation
└── Neillans.Adapters.Secrets.Infisical/         # Infisical implementation
```

## Installation

Install the core package and the provider(s) you need:

```bash
# Core abstractions (required)
dotnet add package Neillans.Adapters.Secrets.Core

# Azure Key Vault provider
dotnet add package Neillans.Adapters.Secrets.AzureKeyVault

# Infisical provider
dotnet add package Neillans.Adapters.Secrets.Infisical
```

## Usage

### Azure Key Vault

```csharp
using Microsoft.Extensions.DependencyInjection;
using Neillans.Adapters.Secrets.AzureKeyVault;
using Neillans.Adapters.Secrets.Core;

// Configure services
var services = new ServiceCollection();

services.AddAzureKeyVaultSecretsProvider(options =>
{
    options.VaultUri = "https://your-vault.vault.azure.net/";
    
    // Optional: Use service principal authentication
    // options.TenantId = "your-tenant-id";
    // options.ClientId = "your-client-id";
    // options.ClientSecret = "your-client-secret";
    
    // Without these, DefaultAzureCredential is used (supports managed identity, Azure CLI, etc.)
});

var serviceProvider = services.BuildServiceProvider();
var secretsProvider = serviceProvider.GetRequiredService<ISecretsProvider>();

// Use the provider
var secret = await secretsProvider.GetSecretAsync("my-secret-key");
Console.WriteLine($"Secret value: {secret}");

// Set a secret
await secretsProvider.SetSecretAsync("new-secret", "secret-value");

// List all secrets
var secretKeys = await secretsProvider.ListSecretsAsync();
foreach (var key in secretKeys)
{
    Console.WriteLine($"Secret: {key}");
}

// Delete a secret
await secretsProvider.DeleteSecretAsync("old-secret");
```

### Infisical

```csharp
using Microsoft.Extensions.DependencyInjection;
using Neillans.Adapters.Secrets.Infisical;
using Neillans.Adapters.Secrets.Core;

// Configure services
var services = new ServiceCollection();

services.AddInfisicalSecretsProvider(options =>
{
    options.SiteUrl = "https://app.infisical.com"; // Default
    options.ClientId = "your-client-id";
    options.ClientSecret = "your-client-secret";
    options.ProjectId = "your-project-id";
    options.Environment = "dev"; // dev, staging, prod, etc.
    options.SecretPath = "/"; // Optional: folder path within environment
});

var serviceProvider = services.BuildServiceProvider();
var secretsProvider = serviceProvider.GetRequiredService<ISecretsProvider>();

// Use the provider (same interface as Azure Key Vault)
var secret = await secretsProvider.GetSecretAsync("my-secret-key");
Console.WriteLine($"Secret value: {secret}");
```

### Getting Multiple Secrets

```csharp
var keys = new[] { "secret1", "secret2", "secret3" };
var secrets = await secretsProvider.GetSecretsAsync(keys);

foreach (var (key, value) in secrets)
{
    Console.WriteLine($"{key}: {value}");
}
```

## API Reference

### ISecretsProvider

The core interface that all providers implement:

```csharp
public interface ISecretsProvider
{
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);
    Task<IDictionary<string, string?>> GetSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
    Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default);
    Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> ListSecretsAsync(CancellationToken cancellationToken = default);
}
```

### Configuration Options

#### AzureKeyVaultOptions

- `VaultUri` (required): The Key Vault URI (e.g., `https://myvault.vault.azure.net/`)
- `TenantId` (optional): Tenant ID for service principal authentication
- `ClientId` (optional): Client ID for service principal authentication
- `ClientSecret` (optional): Client secret for service principal authentication

#### InfisicalOptions

- `SiteUrl`: The Infisical site URL (default: `https://app.infisical.com`)
- `ClientId` (required): The client ID for authentication
- `ClientSecret` (required): The client secret for authentication
- `ProjectId` (required): The project ID where secrets are stored
- `Environment`: The environment name (default: `dev`)
- `SecretPath`: Optional secret path/folder within the environment (default: `/`)

## Architecture

The library follows a clean architecture pattern:

1. **Core Layer** (`Neillans.Adapters.Secrets.Core`): Contains abstractions, interfaces, and base types
2. **Provider Layer** (`Neillans.Adapters.Secrets.AzureKeyVault`, `Neillans.Adapters.Secrets.Infisical`): Implements the abstractions for specific providers
3. **Isolation**: Each provider package contains all its dependencies - consumers only need to reference the providers they use

## Exception Handling

All providers throw `SecretsProviderException` when an operation fails (except when a secret is not found, which returns `null`). This provides a consistent exception handling experience across all providers.

```csharp
try
{
    var secret = await secretsProvider.GetSecretAsync("my-secret");
}
catch (SecretsProviderException ex)
{
    Console.WriteLine($"Failed to retrieve secret: {ex.Message}");
}
```

## Benefits

- **Vendor Independence**: Easily switch between providers without changing application code
- **Testing**: Mock `ISecretsProvider` for unit tests
- **Flexibility**: Support multiple providers in the same application
- **Clean Code**: No provider-specific code in your business logic
- **Type Safety**: Strongly-typed configuration options

## Requirements

- .NET 10.0 or later
- Azure Key Vault provider requires Azure.Security.KeyVault.Secrets and Azure.Identity
- Infisical provider requires Infisical.Sdk

## License

[Add your license here]

## Contributing

[Add contribution guidelines here]
.NET Adapters to simplify switching between Azure Key Vault and Infisical
