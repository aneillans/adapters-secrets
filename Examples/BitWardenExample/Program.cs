using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Neillans.Adapters.Secrets.BitWarden;
using Neillans.Adapters.Secrets.Core;

var builder = Host.CreateApplicationBuilder(args);

// Fetch these from environment variables (required)
var serverUrl = Environment.GetEnvironmentVariable("BITWARDEN_SERVER_URL") 
    ?? throw new InvalidOperationException("BITWARDEN_SERVER_URL environment variable is required");
var apiKey = Environment.GetEnvironmentVariable("BITWARDEN_API_KEY") 
    ?? throw new InvalidOperationException("BITWARDEN_API_KEY environment variable is required");

// Register the BitWarden Secrets Provider
builder.Services.AddBitWardenSecretsProvider(options =>
{
    options.ServerUrl = serverUrl;
    options.ApiKey = apiKey;
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
