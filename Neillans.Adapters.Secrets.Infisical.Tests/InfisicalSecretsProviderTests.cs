using Microsoft.Extensions.DependencyInjection;
using Neillans.Adapters.Secrets.Infisical;
using Neillans.Adapters.Secrets.Core;
using Xunit;

namespace Neillans.Adapters.Secrets.Infisical.Tests;

public class InfisicalSecretsProviderTests
{
    private static ISecretsProvider? BuildProvider()
    {
        if (!RequiredPresent("INFISICAL_CLIENT_ID", "INFISICAL_CLIENT_SECRET", "INFISICAL_PROJECT_ID", "INFISICAL_ENVIRONMENT"))
            return null;

        var services = new ServiceCollection();
        services.AddInfisicalSecretsProvider(options =>
        {
            options.ClientId = Env("INFISICAL_CLIENT_ID")!;
            options.ClientSecret = Env("INFISICAL_CLIENT_SECRET")!;
            options.ProjectId = Env("INFISICAL_PROJECT_ID")!;
            options.Environment = Env("INFISICAL_ENVIRONMENT")!;
            options.SiteUrl = Env("INFISICAL_SITE_URL") ?? "https://app.infisical.com";
            options.SecretPath = Env("INFISICAL_SECRET_PATH");
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
        Assert.False(string.IsNullOrWhiteSpace(value));
    }

    [Fact]
    public async Task Can_Set_And_Delete_Secret_If_Allowed()
    {
        if (Env("ALLOW_MUTATING_TESTS") != "true") return;
        var provider = BuildProvider();
        if (provider is null) return;

        var key = $"test-secret-{Guid.NewGuid():N}";
        var val = Guid.NewGuid().ToString();
        await provider.SetSecretAsync(key, val);
        var fetched = await provider.GetSecretAsync(key);
        Assert.Equal(val, fetched);
        await provider.DeleteSecretAsync(key);
    }

    [Fact]
    public async Task Can_List_Secrets_If_Configured()
    {
        var provider = BuildProvider();
        if (provider is null) return;
        var list = await provider.ListSecretsAsync();
        Assert.NotNull(list);
    }
}
