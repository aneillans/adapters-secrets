using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Neillans.Adapters.Secrets.BitWarden;
using Neillans.Adapters.Secrets.Core;

var builder = Host.CreateApplicationBuilder(args);

// Fetch these from environment variables (required)
var serverUrl = Environment.GetEnvironmentVariable("BITWARDEN_SERVER_URL");
if (string.IsNullOrWhiteSpace(serverUrl))
{
    Console.WriteLine("BITWARDEN_SERVER_URL not set. Set it to your Bitwarden server URL (e.g., https://vault.bitwarden.com or your self-hosted instance URL) and try again.");
    return;
}

// Either a static API key, or a BitWarden Organization API Key (client id/secret pair), is required.
// The organization id is parsed automatically from the client id (formatted as
// "organization.{guid}"), so a separate organization id variable is not needed.
var apiKey = Environment.GetEnvironmentVariable("BITWARDEN_API_KEY");
var clientId = Environment.GetEnvironmentVariable("BITWARDEN_CLIENT_ID");
var clientSecret = Environment.GetEnvironmentVariable("BITWARDEN_CLIENT_SECRET");

if (string.IsNullOrWhiteSpace(apiKey) && (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)))
{
    Console.WriteLine("Set either BITWARDEN_API_KEY, or both BITWARDEN_CLIENT_ID and BITWARDEN_CLIENT_SECRET (an Organization API Key), and try again.");
    return;
}

// Register the BitWarden Secrets Provider
builder.Services.AddBitWardenSecretsProvider(options =>
{
    options.ServerUrl = serverUrl;

    if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
    {
        options.ClientId = clientId;
        options.ClientSecret = clientSecret;
    }
    else
    {
        options.ApiKey = apiKey!;
    }
});

var host = builder.Build();

var provider = host.Services.GetRequiredService<ISecretsProvider>();

Console.WriteLine("Fetching secrets from BitWarden...");

try
{
    // List all secret names available
    var secretNames = await provider.ListSecretsAsync();
    Console.WriteLine($"Found {secretNames.Count()} secrets.");

    foreach (var name in secretNames.Take(5))
    {
        Console.WriteLine($" - {name}");
    }

    if (secretNames.Any())
    {
        var firstSecretName = secretNames.First();
        Console.WriteLine($"\nFetching value for secret: {firstSecretName}");
        var value = await provider.GetSecretAsync(firstSecretName);
        Console.WriteLine($"Value for '{firstSecretName}': {(value != null ? "***" : "null")}");
    }
    else
    {
        Console.WriteLine("\nNo secrets found to fetch.");
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"An error occurred: {ex.Message}");
}
