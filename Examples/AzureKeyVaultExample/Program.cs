using Microsoft.Extensions.DependencyInjection;
using Neillans.Adapters.Secrets.AzureKeyVault;
using Neillans.Adapters.Secrets.Core;

// Environment variables expected:
// VAULT_URI (required)
// AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET (optional for ClientSecret credential)
// SECRET_KEY (optional secret name to read)
// NEW_SECRET_KEY / NEW_SECRET_VALUE (optional for creating a secret)

var vaultUri = Environment.GetEnvironmentVariable("VAULT_URI");
if (string.IsNullOrWhiteSpace(vaultUri))
{
    Console.WriteLine("VAULT_URI not set. Set it to your Key Vault URI, e.g. https://your-vault.vault.azure.net/");
    return;
}

var services = new ServiceCollection();
services.AddAzureKeyVaultSecretsProvider(options =>
{
    options.VaultUri = vaultUri!;
    // Optional explicit credential values
    options.TenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
    options.ClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
    options.ClientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
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

Console.WriteLine("Listing secrets (may be limited by permissions)...");
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
