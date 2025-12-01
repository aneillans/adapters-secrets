# Quick Start Guide

Get started with SecretsAdapter in 5 minutes!

## Installation

Choose and install the packages you need:

```bash
# Install Core (required)
dotnet add package Neillans.Adapters.Secrets.Core

# Install one or both providers
dotnet add package Neillans.Adapters.Secrets.AzureKeyVault
dotnet add package Neillans.Adapters.Secrets.Infisical
```

## Basic Usage

### Option 1: Azure Key Vault

```csharp
using Microsoft.Extensions.DependencyInjection;
using Neillans.Adapters.Secrets.AzureKeyVault;
using Neillans.Adapters.Secrets.Core;

// Setup
var services = new ServiceCollection();
services.AddAzureKeyVaultSecretsProvider(options =>
{
    options.VaultUri = "https://your-vault.vault.azure.net/";
});

var provider = services.BuildServiceProvider()
    .GetRequiredService<ISecretsProvider>();

// Get a secret
var secret = await provider.GetSecretAsync("database-password");
Console.WriteLine($"Password: {secret}");
```

### Option 2: Infisical

```csharp
using Microsoft.Extensions.DependencyInjection;
using Neillans.Adapters.Secrets.Infisical;
using Neillans.Adapters.Secrets.Core;

// Setup
var services = new ServiceCollection();
services.AddInfisicalSecretsProvider(options =>
{
    options.ClientId = "your-client-id";
    options.ClientSecret = "your-client-secret";
    options.ProjectId = "your-project-id";
    options.Environment = "dev";
});

var provider = services.BuildServiceProvider()
    .GetRequiredService<ISecretsProvider>();

// Get a secret
var secret = await provider.GetSecretAsync("api-key");
Console.WriteLine($"API Key: {secret}");
```

## Common Operations

### Get Multiple Secrets

```csharp
var keys = new[] { "db-host", "db-port", "db-name" };
var secrets = await provider.GetSecretsAsync(keys);

foreach (var (key, value) in secrets)
{
    Console.WriteLine($"{key} = {value}");
}
```

### Set a Secret

```csharp
await provider.SetSecretAsync("new-api-key", "super-secret-value");
```

### List All Secrets

```csharp
var allKeys = await provider.ListSecretsAsync();
foreach (var key in allKeys)
{
    Console.WriteLine($"Found secret: {key}");
}
```

### Delete a Secret

```csharp
await provider.DeleteSecretAsync("old-api-key");
```

## ASP.NET Core Integration

### Program.cs

```csharp
using Neillans.Adapters.Secrets.AzureKeyVault;
using Neillans.Adapters.Secrets.Core;

var builder = WebApplication.CreateBuilder(args);

// Add secrets provider
builder.Services.AddAzureKeyVaultSecretsProvider(options =>
{
    options.VaultUri = builder.Configuration["KeyVault:VaultUri"]!;
});

var app = builder.Build();
app.Run();
```

### Using in a Controller

```csharp
using Microsoft.AspNetCore.Mvc;
using Neillans.Adapters.Secrets.Core;

[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly ISecretsProvider _secretsProvider;

    public ConfigController(ISecretsProvider secretsProvider)
    {
        _secretsProvider = secretsProvider;
    }

    [HttpGet("connection-string")]
    public async Task<IActionResult> GetConnectionString()
    {
        var connStr = await _secretsProvider.GetSecretAsync("connection-string");
        return Ok(new { ConnectionString = connStr });
    }
}
```

## Configuration Options

### Azure Key Vault

```csharp
services.AddAzureKeyVaultSecretsProvider(options =>
{
    // Required
    options.VaultUri = "https://your-vault.vault.azure.net/";
    
    // Optional: Use these for service principal auth
    // options.TenantId = "your-tenant-id";
    // options.ClientId = "your-client-id";
    // options.ClientSecret = "your-client-secret";
    
    // Without these, DefaultAzureCredential is used
});
```

### Infisical

```csharp
services.AddInfisicalSecretsProvider(options =>
{
    // Required
    options.ClientId = "your-client-id";
    options.ClientSecret = "your-client-secret";
    options.ProjectId = "your-project-id";
    
    // Optional (with defaults)
    options.SiteUrl = "https://app.infisical.com";
    options.Environment = "dev";
    options.SecretPath = "/";
});
```

## Error Handling

```csharp
try
{
    var secret = await provider.GetSecretAsync("my-secret");
    if (secret == null)
    {
        Console.WriteLine("Secret not found");
    }
    else
    {
        Console.WriteLine($"Secret: {secret}");
    }
}
catch (SecretsProviderException ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
```

## Best Practices

1. **Store Configuration Securely**: Use environment variables or user secrets for sensitive config
2. **Use Dependency Injection**: Register providers in your DI container
3. **Handle Nulls**: Check for null when getting secrets
4. **Use Cancellation Tokens**: Pass cancellation tokens for long-running operations
5. **Don't Cache Secrets**: Let the provider handle any caching needs

## Next Steps

- Read the full [README.md](README.md) for detailed documentation
- Check [PROJECT_SUMMARY.md](PROJECT_SUMMARY.md) for architecture details
- Review the [CHANGELOG.md](CHANGELOG.md) for version history

## Need Help?

- Check the documentation
- Review the XML comments in the code
- Look at the example projects in the `Examples/` directory
