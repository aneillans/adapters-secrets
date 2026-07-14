using Microsoft.Extensions.DependencyInjection;
using Neillans.Adapters.Secrets.BitWarden;
using Neillans.Adapters.Secrets.Core;
using Xunit;

namespace Neillans.Adapters.Secrets.BitWarden.Tests;

public class BitWardenSecretsProviderTests
{
    private static ISecretsProvider? BuildProvider()
    {
        var hasApiKey = RequiredPresent("BITWARDEN_SERVER_URL", "BITWARDEN_API_KEY");
        var hasOrgApiKey = RequiredPresent("BITWARDEN_SERVER_URL", "BITWARDEN_CLIENT_ID", "BITWARDEN_CLIENT_SECRET");

        if (!hasApiKey && !hasOrgApiKey)
            return null;

        var services = new ServiceCollection();
        services.AddBitWardenSecretsProvider(options =>
        {
            options.ServerUrl = Env("BITWARDEN_SERVER_URL")!;

            if (hasOrgApiKey)
            {
                options.ClientId = Env("BITWARDEN_CLIENT_ID");
                options.ClientSecret = Env("BITWARDEN_CLIENT_SECRET");
                options.OrganizationId = Env("BITWARDEN_ORGANIZATION_ID");
            }
            else
            {
                options.ApiKey = Env("BITWARDEN_API_KEY")!;
            }
        });
        return services.BuildServiceProvider().GetRequiredService<ISecretsProvider>();
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);
    private static bool RequiredPresent(params string[] vars) => vars.All(v => !string.IsNullOrWhiteSpace(Env(v)));

    [Fact]
    public async Task Can_Get_Secret_If_Configured()
    {
        var secretKey = Env("TEST_SECRET_KEY");
        var provider = BuildProvider();
        if (provider is null || string.IsNullOrWhiteSpace(secretKey)) return; // skip

        var value = await provider.GetSecretAsync(secretKey!);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Secret '{secretKey}' was not found or empty.");
    }

    [Fact]
    public async Task Can_List_Secrets_If_Configured()
    {
        var provider = BuildProvider();
        if (provider is null) return;

        var list = await provider.ListSecretsAsync();
        Assert.NotNull(list);
        Assert.NotEmpty(list);
    }

    [Fact]
    public async Task Can_Set_Secret_If_Configured()
    {
        var provider = BuildProvider();
        if (provider is null) return;

        var key = $"adapters-secrets-test-{Guid.NewGuid():N}";
        const string value = "test-value";

        await provider.SetSecretAsync(key, value);
        var result = await provider.GetSecretAsync(key);

        Assert.Equal(value, result);
    }

    [Fact]
    public async Task Delete_Secret_Throws_NotSupported()
    {
        var provider = BuildProvider();
        if (provider is null) return;

        await Assert.ThrowsAsync<SecretsProviderException>(async () => 
            await provider.DeleteSecretAsync("any"));
    }
}
