using Microsoft.Extensions.DependencyInjection;
using Neillans.Adapters.Secrets.Infisical;
using Neillans.Adapters.Secrets.Core;

// Environment variables expected:
// INFISICAL_CLIENT_ID, INFISICAL_CLIENT_SECRET, INFISICAL_PROJECT_ID, INFISICAL_ENVIRONMENT (required)
// INFISICAL_SITE_URL (optional, defaults to https://app.infisical.com)
// SECRET_KEY (optional secret name to read)
// NEW_SECRET_KEY / NEW_SECRET_VALUE (optional for creating a secret)
// INFISICAL_SECRET_PATH (optional path/folder e.g. /backend)

string? clientId = Environment.GetEnvironmentVariable("INFISICAL_CLIENT_ID");
string? clientSecret = Environment.GetEnvironmentVariable("INFISICAL_CLIENT_SECRET");
string? projectId = Environment.GetEnvironmentVariable("INFISICAL_PROJECT_ID");
string? environment = Environment.GetEnvironmentVariable("INFISICAL_ENVIRONMENT");
string? siteUrl = Environment.GetEnvironmentVariable("INFISICAL_SITE_URL") ?? "https://app.infisical.com";
string? secretPath = Environment.GetEnvironmentVariable("INFISICAL_SECRET_PATH");

if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(environment))
{
    Console.WriteLine("Missing required Infisical environment variables (CLIENT_ID, CLIENT_SECRET, PROJECT_ID, ENVIRONMENT). Exiting.");
    return;
}

var services = new ServiceCollection();
services.AddInfisicalSecretsProvider(options =>
{
    options.ClientId = clientId!;
    options.ClientSecret = clientSecret!;
    options.ProjectId = projectId!;
    options.Environment = environment!;
    options.SiteUrl = siteUrl;
    options.SecretPath = secretPath; // may be null
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

Console.WriteLine("Listing secrets (may be limited by path/env)...");
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
