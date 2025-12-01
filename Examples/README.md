# Example Usage

This directory contains example applications demonstrating how to use the SecretsAdapter library.

## Running the Examples

### Azure Key Vault Example

1. Set up Azure Key Vault credentials (via environment variables, Azure CLI, or managed identity)
2. Update the vault URI in the example
3. Run: `dotnet run --project AzureKeyVaultExample`

### Infisical Example

1. Get your Infisical credentials (Client ID, Client Secret, Project ID)
2. Update the configuration in the example
3. Run: `dotnet run --project InfisicalExample`

## Configuration

For production use, store sensitive configuration (client secrets, etc.) in:
- Environment variables
- Azure Key Vault
- User Secrets (for development)
- Configuration files (with proper .gitignore)

Never commit secrets to source control!
