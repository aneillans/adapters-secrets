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

### BitWarden Example

This example is a connection + decryption diagnostic: it authenticates, derives the vault keys and decrypts items client-side, printing each step so you can prove out (or debug) a connection.

1. Get your BitWarden/VaultWarden server URL, a **personal** API key (`user.{guid}` client id/secret, from Account Settings > Security > Keys > View API Key), and the account email + master password
2. Copy `appsettings.local.json.example` to `appsettings.local.json` (gitignored) and fill it in, or set the `BITWARDEN_*` environment variables
3. Run: `dotnet run --project BitWardenExample`

## Configuration

For production use, store sensitive configuration (client secrets, etc.) in:
- Environment variables
- Azure Key Vault
- User Secrets (for development)
- Configuration files (with proper .gitignore)

Never commit secrets to source control!
