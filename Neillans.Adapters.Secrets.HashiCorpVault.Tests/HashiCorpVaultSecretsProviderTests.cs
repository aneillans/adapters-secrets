using Microsoft.Extensions.DependencyInjection;
using Neillans.Adapters.Secrets.Core;
using Neillans.Adapters.Secrets.HashiCorpVault;
using Xunit;

namespace Neillans.Adapters.Secrets.HashiCorpVault.Tests;

public class HashiCorpVaultSecretsProviderTests
{
    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);
    private static bool RequiredPresent(params string[] vars) => vars.All(v => !string.IsNullOrWhiteSpace(Env(v)));

    private static ISecretsProvider Resolve(Action<HashiCorpVaultOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddHashiCorpVaultSecretsProvider(configure);
        return services.BuildServiceProvider().GetRequiredService<ISecretsProvider>();
    }

    private static void ConfigureValid(HashiCorpVaultOptions options)
    {
        options.VaultAddress = "http://127.0.0.1:8200";
        options.Token = "test-token";
    }

    private static ISecretsProvider? BuildProvider()
    {
        if (!RequiredPresent("VAULT_ADDR", "VAULT_TOKEN"))
            return null;

        return Resolve(options =>
        {
            options.VaultAddress = Env("VAULT_ADDR")!;
            options.Token = Env("VAULT_TOKEN")!;
            options.MountPoint = Env("VAULT_MOUNT") ?? "secret";
            options.BasePath = Env("VAULT_BASE_PATH") ?? string.Empty;
            options.ValueKey = Env("VAULT_VALUE_KEY") ?? "value";
        });
    }

    // ---- Options validation (offline) ---------------------------------------

    [Fact]
    public void VaultAddress_Is_Required()
    {
        var ex = Assert.Throws<ArgumentException>(() => Resolve(options =>
        {
            ConfigureValid(options);
            options.VaultAddress = "";
        }));
        Assert.Contains("VaultAddress", ex.Message);
    }

    [Fact]
    public void Authentication_Is_Required()
    {
        var ex = Assert.Throws<ArgumentException>(() => Resolve(options =>
        {
            options.VaultAddress = "http://127.0.0.1:8200";
            // no token, no AppRole
        }));
        Assert.Contains("Authentication is required", ex.Message);
    }

    [Fact]
    public void Token_And_AppRole_Together_Are_Rejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => Resolve(options =>
        {
            options.VaultAddress = "http://127.0.0.1:8200";
            options.Token = "test-token";
            options.RoleId = "role";
            options.SecretId = "secret";
        }));
        Assert.Contains("not both", ex.Message);
    }

    [Fact]
    public void Valid_Token_Options_Construct_Successfully()
    {
        var provider = Resolve(ConfigureValid);
        Assert.NotNull(provider);
    }

    [Fact]
    public void Valid_AppRole_Options_Construct_Successfully()
    {
        var provider = Resolve(options =>
        {
            options.VaultAddress = "http://127.0.0.1:8200";
            options.RoleId = "role-id";
            options.SecretId = "secret-id";
        });
        Assert.NotNull(provider);
    }

    // ---- Live integration (only run when env vars are configured) ------------

    [Fact]
    public async Task Can_List_Secrets_If_Configured()
    {
        var provider = BuildProvider();
        if (provider is null) return;

        var list = await provider.ListSecretsAsync();
        Assert.NotNull(list);
    }

    [Fact]
    public async Task Can_Get_Secret_If_Configured()
    {
        var secretKey = Env("TEST_SECRET_KEY");
        var provider = BuildProvider();
        if (provider is null || string.IsNullOrWhiteSpace(secretKey)) return;

        var value = await provider.GetSecretAsync(secretKey!);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Secret '{secretKey}' was not found or empty.");
    }

    [Fact]
    public async Task Missing_Secret_Returns_Null_If_Configured()
    {
        var provider = BuildProvider();
        if (provider is null) return;

        var value = await provider.GetSecretAsync($"definitely-not-seeded-{Guid.NewGuid():N}");
        Assert.Null(value);
    }

    [Fact]
    public async Task Can_Set_Get_And_Delete_Secret_If_Allowed()
    {
        if (Env("ALLOW_MUTATING_TESTS") != "true") return;
        var provider = BuildProvider();
        if (provider is null) return;

        var key = $"test-secret-{Guid.NewGuid():N}";
        var val = Guid.NewGuid().ToString();

        await provider.SetSecretAsync(key, val);
        Assert.Equal(val, await provider.GetSecretAsync(key));

        await provider.DeleteSecretAsync(key);
        Assert.Null(await provider.GetSecretAsync(key));
    }
}
