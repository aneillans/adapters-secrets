# SecretsAdapter

A .NET 10 abstraction library for seamlessly switching between different secrets management providers. This library provides a unified interface for accessing secrets across multiple platforms without requiring consumers to have knowledge of the underlying provider implementations.

Nuget packages of this project can be downloaded from my private Nuget feed server: https://packages.neillans.co.uk/feeds/dotNet

## Features

- **Provider Abstraction**: Single interface (`ISecretsProvider`) for all secrets operations
- **Multiple Providers**: Built-in support for:
  - Azure Key Vault
  - Infisical
  - BitWarden / VaultWarden
  - In-Memory (for tests and local/ephemeral runs)
- **Fully Encapsulated**: All provider-specific dependencies are contained within their respective packages
- **Dependency Injection**: First-class support for Microsoft.Extensions.DependencyInjection
- **Async/Await**: Fully asynchronous API with cancellation token support
- **.NET 10**: Built for the latest .NET platform

## Project Structure

```
adapters-secrets/
├── Neillans.Adapters.Secrets.Core/              # Core abstractions and interfaces
├── Neillans.Adapters.Secrets.AzureKeyVault/     # Azure Key Vault implementation
├── Neillans.Adapters.Secrets.Infisical/         # Infisical implementation
├── Neillans.Adapters.Secrets.BitWarden/         # BitWarden / VaultWarden implementation
└── Neillans.Adapters.Secrets.InMemory/          # Non-persistent, in-process implementation
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

# BitWarden / VaultWarden provider
dotnet add package Neillans.Adapters.Secrets.BitWarden

# In-Memory provider (tests / local ephemeral runs)
dotnet add package Neillans.Adapters.Secrets.InMemory
```

## Usage
### Example Projects

Four runnable console examples are included in the `examples/` directory:

- `examples/AzureKeyVaultExample` – demonstrates configuring and interacting with Azure Key Vault via environment variables.
- `examples/InfisicalExample` – demonstrates configuring and interacting with Infisical.
- `examples/BitWardenExample` – demonstrates configuring and interacting with BitWarden / VaultWarden.
- `examples/HashiCorpVaultExample` – demonstrates configuring and interacting with HashiCorp Vault.

Run them by setting the required environment variables then executing:

```bash
dotnet run --project examples/AzureKeyVaultExample
dotnet run --project examples/InfisicalExample
dotnet run --project examples/BitWardenExample
dotnet run --project examples/HashiCorpVaultExample
```

Azure Key Vault example expected environment variables:

```
VAULT_URI=https://your-vault.vault.azure.net/
AZURE_TENANT_ID=<optional for client secret auth>
AZURE_CLIENT_ID=<optional for client secret auth>
AZURE_CLIENT_SECRET=<optional for client secret auth>
SECRET_KEY=<optional existing secret to read>
NEW_SECRET_KEY=<optional to create>
NEW_SECRET_VALUE=<optional to create>
ALLOW_MUTATING_TESTS=true   # only if you want write/delete tests to run
```

Infisical example expected environment variables:

```
INFISICAL_CLIENT_ID=
INFISICAL_CLIENT_SECRET=
INFISICAL_PROJECT_ID=
INFISICAL_ENVIRONMENT=dev
INFISICAL_SITE_URL=https://app.infisical.com   # optional
INFISICAL_SECRET_PATH=/                        # optional path/folder
SECRET_KEY=<optional existing secret to read>
NEW_SECRET_KEY=<optional to create>
NEW_SECRET_VALUE=<optional to create>
ALLOW_MUTATING_TESTS=true   # only if you want write/delete tests to run
```

If variables are missing the examples will exit gracefully.

BitWarden / VaultWarden example expected environment variables:

```
BITWARDEN_SERVER_URL=https://vault.bitwarden.com    # or your self-hosted VaultWarden URL

# A personal API key (Account Settings > Security > Keys > View API Key)
BITWARDEN_CLIENT_ID=      # formatted as user.{guid}
BITWARDEN_CLIENT_SECRET=

# The account email and master password. The vault is end-to-end encrypted, so the master
# password is required to derive the decryption key - a token alone cannot decrypt.
BITWARDEN_EMAIL=
BITWARDEN_MASTER_PASSWORD=
```

If variables are missing the examples will exit gracefully.

HashiCorp Vault example expected environment variables:

```
VAULT_ADDR=https://your-vault:8200

# Authenticate with either a token or AppRole credentials
VAULT_TOKEN=<token auth>
VAULT_ROLE_ID=<AppRole auth>
VAULT_SECRET_ID=<AppRole auth>

VAULT_MOUNT=secret        # optional KV mount name
VAULT_BASE_PATH=          # optional path prefix
VAULT_VALUE_KEY=value     # optional field within the secret
SECRET_KEY=<optional existing secret to read>
NEW_SECRET_KEY=<optional to create>
NEW_SECRET_VALUE=<optional to create>
```

If variables are missing the examples will exit gracefully.

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

### BitWarden / VaultWarden

```csharp
using Microsoft.Extensions.DependencyInjection;
using Neillans.Adapters.Secrets.BitWarden;
using Neillans.Adapters.Secrets.Core;

// Configure services
var services = new ServiceCollection();

services.AddBitWardenSecretsProvider(options =>
{
    options.ServerUrl = "https://vault.example.com"; // or self-hosted VaultWarden URL

    // A personal API key (Account Settings > Security > Keys > View API Key).
    options.ClientId = "user.your-user-guid";
    options.ClientSecret = "your-client-secret";

    // The vault is end-to-end encrypted. The email + master password derive the decryption
    // key client-side; both are required to read plaintext secrets.
    options.Email = "you@example.com";
    options.MasterPassword = "your-master-password";
});

var serviceProvider = services.BuildServiceProvider();
var secretsProvider = serviceProvider.GetRequiredService<ISecretsProvider>();

// Use the provider (same interface as Azure Key Vault / Infisical)
var secret = await secretsProvider.GetSecretAsync("my-secret-key");
Console.WriteLine($"Secret value: {secret}");
```

### In-Memory

```csharp
using Microsoft.Extensions.DependencyInjection;
using Neillans.Adapters.Secrets.InMemory;
using Neillans.Adapters.Secrets.Core;

// Configure services
var services = new ServiceCollection();
services.AddInMemorySecretsProvider();

var serviceProvider = services.BuildServiceProvider();
var secretsProvider = serviceProvider.GetRequiredService<ISecretsProvider>();

// Use the provider (same interface as the other providers)
await secretsProvider.SetSecretAsync("my-secret-key", "my-secret-value");
var secret = await secretsProvider.GetSecretAsync("my-secret-key");
Console.WriteLine($"Secret value: {secret}");
```

Secrets are held only in an in-process `ConcurrentDictionary` and are lost when the
process exits. This provider requires no external configuration and no network access,
making it ideal for unit/integration tests and local development where a real secrets
backend isn't available. It is registered as a singleton so secrets persist for the
lifetime of the application, but are never shared across processes.

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

#### BitWardenOptions

- `ServerUrl` (required): The base URL of the VaultWarden/BitWarden server (default: `https://127.0.0.1`)
- `ClientId` (required): A **personal** API key client id, formatted as `user.{guid}` (Account Settings > Security > Keys > View API Key). An Organization API Key (`organization.*`) is rejected — it can only reach the Public API, not vault items
- `ClientSecret` (required): The personal API key secret
- `Email` (required): The account email. Used to log in and as the key-derivation salt
- `MasterPassword` (required): The account master password. The vault is end-to-end encrypted, so this is the only source of the decryption key
- `IdentityUrl`: Identity server URL used for prelogin and the token exchange (default: `{ServerUrl}/identity`)

How it works: the adapter authenticates with the personal API key, fetches the KDF parameters via `prelogin`, derives the master key (PBKDF2-SHA256 or Argon2id) from the master password + email, then unwraps the user and organization keys and decrypts each vault item **client-side** (AES-256-CBC + HMAC-SHA256; org keys via RSA-OAEP). A secret's value is taken from the login password, then a custom field named `password`, then the secure-note contents.

Note: this adapter is **read-only**. `SetSecretAsync` and `DeleteSecretAsync` throw `SecretsProviderException`.

#### InMemory Provider

The in-memory provider takes no configuration options; call `AddInMemorySecretsProvider()` to register it. It is not persistent and not shared across processes, so it is not suitable for production use.

## Architecture

The library follows a clean architecture pattern:

1. **Core Layer** (`Neillans.Adapters.Secrets.Core`): Contains abstractions, interfaces, and base types
2. **Provider Layer** (`Neillans.Adapters.Secrets.AzureKeyVault`, `Neillans.Adapters.Secrets.Infisical`, `Neillans.Adapters.Secrets.BitWarden`, `Neillans.Adapters.Secrets.InMemory`): Implements the abstractions for specific providers
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
- BitWarden provider uses the BitWarden/VaultWarden HTTP API directly and depends on `Microsoft.Extensions.Options` plus `Konscious.Security.Cryptography.Argon2` (for Argon2id key derivation)
- In-Memory provider has no third-party dependencies beyond `Microsoft.Extensions.DependencyInjection.Abstractions`

## License

[Add your license here]

## Contributing

[Add contribution guidelines here]

.NET Adapters to simplify switching between Azure Key Vault, Infisical, BitWarden / VaultWarden, and an in-memory provider
