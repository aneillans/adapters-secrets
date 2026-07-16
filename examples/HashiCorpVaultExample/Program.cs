using Microsoft.Extensions.DependencyInjection;
using Neillans.Adapters.Secrets.Core;
using Neillans.Adapters.Secrets.HashiCorpVault;

// Environment variables expected:
// VAULT_ADDR (required, e.g. http://127.0.0.1:8200)
// VAULT_TOKEN (token auth) OR VAULT_ROLE_ID + VAULT_SECRET_ID (AppRole auth)
// VAULT_MOUNT (optional, defaults to "secret")
// VAULT_BASE_PATH (optional path prefix within the mount)
// VAULT_VALUE_KEY (optional field name, defaults to "value")
// SECRET_KEY (optional secret name to read)
// NEW_SECRET_KEY / NEW_SECRET_VALUE (optional for creating a secret)

var vaultAddr = Environment.GetEnvironmentVariable("VAULT_ADDR");
if (string.IsNullOrWhiteSpace(vaultAddr))
{
    Console.WriteLine("VAULT_ADDR not set. Set it to your Vault address, e.g. http://127.0.0.1:8200");
    return;
}

var services = new ServiceCollection();
services.AddHashiCorpVaultSecretsProvider(options =>
{
    options.VaultAddress = vaultAddr!;
    options.Token = Environment.GetEnvironmentVariable("VAULT_TOKEN");
    options.RoleId = Environment.GetEnvironmentVariable("VAULT_ROLE_ID");
    options.SecretId = Environment.GetEnvironmentVariable("VAULT_SECRET_ID");
    options.MountPoint = Environment.GetEnvironmentVariable("VAULT_MOUNT") ?? "secret";
    options.BasePath = Environment.GetEnvironmentVariable("VAULT_BASE_PATH") ?? string.Empty;
    options.ValueKey = Environment.GetEnvironmentVariable("VAULT_VALUE_KEY") ?? "value";
});

var sp = services.BuildServiceProvider();
var provider = sp.GetRequiredService<ISecretsProvider>();

var secretKey = Environment.GetEnvironmentVariable("SECRET_KEY");
if (!string.IsNullOrWhiteSpace(secretKey))
{
    try
    {
        var value = await provider.GetSecretAsync(secretKey!);
        Console.WriteLine($"Secret '{secretKey}': {value}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to read secret '{secretKey}': {ex.Message}");
    }
}

var newKey = Environment.GetEnvironmentVariable("NEW_SECRET_KEY");
var newValue = Environment.GetEnvironmentVariable("NEW_SECRET_VALUE");
if (!string.IsNullOrWhiteSpace(newKey) && !string.IsNullOrWhiteSpace(newValue))
{
    try
    {
        await provider.SetSecretAsync(newKey!, newValue!);
        Console.WriteLine($"Created secret '{newKey}'.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to create secret '{newKey}': {ex.Message}");
    }
}

Console.WriteLine("Listing secrets (may be limited by policy/path)...");
try
{
    var keys = await provider.ListSecretsAsync();
    foreach (var k in keys)
    {
        Console.WriteLine($" - {k}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to list secrets: {ex.Message}");
}
