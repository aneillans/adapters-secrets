using Microsoft.Extensions.DependencyInjection;
using Neillans.Adapters.Secrets.AzureKeyVault;
using Neillans.Adapters.Secrets.Core;
using Xunit;

namespace Neillans.Adapters.Secrets.AzureKeyVault.Tests;

public class AzureKeyVaultSecretsProviderTests
{
    private static ISecretsProvider? BuildProvider()
    {
        var vaultUri = Env("VAULT_URI");
        if (vaultUri is null) return null;

        var services = new ServiceCollection();
        services.AddAzureKeyVaultSecretsProvider(options =>
        {
            options.VaultUri = vaultUri;
            options.TenantId = Env("AZURE_TENANT_ID");
            options.ClientId = Env("AZURE_CLIENT_ID");
            options.ClientSecret = Env("AZURE_CLIENT_SECRET");
        });
        return services.BuildServiceProvider().GetRequiredService<ISecretsProvider>();
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);

    private static bool RequiredPresent(params string[] vars) => vars.All(v => !string.IsNullOrWhiteSpace(Env(v)));

    [Fact]
    public async Task Can_Get_Secret_If_Configured()
    {
        var secretKey = Env("TEST_SECRET_KEY");
        if (!RequiredPresent("VAULT_URI") || string.IsNullOrWhiteSpace(secretKey))
            return; // skip silently if not configured

        var provider = BuildProvider();
        Assert.NotNull(provider);

        var value = await provider!.GetSecretAsync(secretKey!);
        Assert.False(string.IsNullOrWhiteSpace(value));
    }

    [Fact]
    public async Task Can_Set_And_Delete_Secret_If_Allowed()
    {
        if (!RequiredPresent("VAULT_URI") || Env("ALLOW_MUTATING_TESTS") != "true")
            return; // skip

        var provider = BuildProvider();
        Assert.NotNull(provider);

        var key = $"test-secret-{Guid.NewGuid():N}";
        var val = Guid.NewGuid().ToString();
        await provider!.SetSecretAsync(key, val);
        var fetched = await provider.GetSecretAsync(key);
        Assert.Equal(val, fetched);

        await provider.DeleteSecretAsync(key);
    }

    [Fact]
    public async Task Can_List_Secrets_If_Configured()
    {
        if (!RequiredPresent("VAULT_URI")) return;
        var provider = BuildProvider();
        Assert.NotNull(provider);
        var list = await provider!.ListSecretsAsync();
        Assert.NotNull(list);
    }
}
