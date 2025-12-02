# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
