# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2026-07-16

### Added
- Added Hashicorp Vault support
- Implemented a full automated smoke test

### Changed
- Updated BitWarden provider to fix broken API for VaultWarden hosted
- Updated Infiscial for broken API

## [1.1.0] - 2026-07-15

### Added

#### In-Memory Provider (Neillans.Adapters.Secrets.InMemory)
- Full implementation of `ISecretsProvider` backed by a non-persistent, in-process `ConcurrentDictionary`
- Intended for unit/integration tests and local/ephemeral runs where no real secrets backend is required
- Dependency injection extensions via `AddInMemorySecretsProvider`
- No external configuration or network access required

#### BitWarden / VaultWarden Provider (Neillans.Adapters.Secrets.BitWarden)
- Read secrets from BitWarden/VaultWarden using a personal API key (`user.{guid}`)
- Client-side decryption of end-to-end-encrypted vault items: derives the master key (PBKDF2-SHA256 or Argon2id) from the account email + master password, unwraps the user and organization keys, and decrypts each item locally (AES-256-CBC + HMAC-SHA256; org keys via RSA-OAEP)
- Read-only: `SetSecretAsync` and `DeleteSecretAsync` throw `SecretsProviderException`

### Changed

#### BitWarden / VaultWarden Provider (Neillans.Adapters.Secrets.BitWarden)
- **Breaking:** `BitWardenOptions` now requires a personal API key (`ClientId` `user.{guid}` + `ClientSecret`) plus `Email` and `MasterPassword`. The previous `ApiKey`, `OrganizationId`, and `Scope` options, and the Organization API Key flow, have been removed — an Organization API Key only grants the Public API and cannot read vault items

## [1.0.0] - 2025-12-01

### Added

#### Core Library (Neillans.Adapters.Secrets.Core)
- Initial release of core abstractions library
- `ISecretsProvider` interface with full CRUD operations
- `ISecretsProviderFactory` for dynamic provider creation
- `SecretsProviderType` enumeration
- `SecretsProviderException` custom exception
- `SecretsProviderFactory` implementation
- Dependency injection support via `ServiceCollectionExtensions`
- Support for async/await with CancellationToken

#### Azure Key Vault Provider (Neillans.Adapters.Secrets.AzureKeyVault)
- Full implementation of `ISecretsProvider` for Azure Key Vault
- Support for DefaultAzureCredential (managed identity, Azure CLI, etc.)
- Support for service principal authentication
- `AzureKeyVaultOptions` configuration class
- Dependency injection extensions via `AddAzureKeyVaultSecretsProvider`
- Comprehensive error handling

#### Infisical Provider (Neillans.Adapters.Secrets.Infisical)
- Full implementation of `ISecretsProvider` for Infisical
- Universal Auth authentication support
- Project and environment-based secret management
- Secret path support for folder organization
- `InfisicalOptions` configuration class
- Dependency injection extensions via `AddInfisicalSecretsProvider`
- Comprehensive error handling

#### Documentation
- Comprehensive README with usage examples
- PROJECT_SUMMARY document with architecture details
- Example usage documentation
- Inline XML documentation for all public APIs

### Dependencies
- .NET 10.0 target framework
- Azure.Security.KeyVault.Secrets 4.8.0 (Azure provider)
- Azure.Identity 1.17.1 (Azure provider)
- Infisical.Sdk 3.0.4 (Infisical provider)
- Microsoft.Extensions.DependencyInjection.Abstractions 10.0.0 (Core)
- Microsoft.Extensions.Options 10.0.0 (Providers)

[1.0.0]: https://github.com/aneillans/adapters-secrets/releases/tag/v1.0.0
