using Microsoft.Extensions.DependencyInjection;
using Neillans.Adapters.Secrets.Core;
using Xunit;

namespace Neillans.Adapters.Secrets.InMemory.Tests;

public class InMemorySecretsProviderTests
{
    private static ISecretsProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddInMemorySecretsProvider();
        return services.BuildServiceProvider().GetRequiredService<ISecretsProvider>();
    }

    [Fact]
    public async Task GetSecretAsync_Returns_Null_When_Not_Set()
    {
        var provider = BuildProvider();
        var value = await provider.GetSecretAsync("missing-key");
        Assert.Null(value);
    }

    [Fact]
    public async Task Can_Set_And_Get_Secret()
    {
        var provider = BuildProvider();
        await provider.SetSecretAsync("my-key", "my-value");
        var value = await provider.GetSecretAsync("my-key");
        Assert.Equal("my-value", value);
    }

    [Fact]
    public async Task Set_Overwrites_Existing_Secret()
    {
        var provider = BuildProvider();
        await provider.SetSecretAsync("my-key", "first");
        await provider.SetSecretAsync("my-key", "second");
        var value = await provider.GetSecretAsync("my-key");
        Assert.Equal("second", value);
    }

    [Fact]
    public async Task Can_Delete_Secret()
    {
        var provider = BuildProvider();
        await provider.SetSecretAsync("my-key", "my-value");
        await provider.DeleteSecretAsync("my-key");
        var value = await provider.GetSecretAsync("my-key");
        Assert.Null(value);
    }

    [Fact]
    public async Task Delete_NonExistent_Secret_Does_Not_Throw()
    {
        var provider = BuildProvider();
        var exception = await Record.ExceptionAsync(() => provider.DeleteSecretAsync("missing-key"));
        Assert.Null(exception);
    }

    [Fact]
    public async Task Can_Get_Multiple_Secrets()
    {
        var provider = BuildProvider();
        await provider.SetSecretAsync("key1", "value1");
        await provider.SetSecretAsync("key2", "value2");

        var results = await provider.GetSecretsAsync(new[] { "key1", "key2", "key3" });

        Assert.Equal("value1", results["key1"]);
        Assert.Equal("value2", results["key2"]);
        Assert.Null(results["key3"]);
    }

    [Fact]
    public async Task Can_List_Secrets()
    {
        var provider = BuildProvider();
        await provider.SetSecretAsync("key1", "value1");
        await provider.SetSecretAsync("key2", "value2");

        var keys = (await provider.ListSecretsAsync()).ToList();

        Assert.Contains("key1", keys);
        Assert.Contains("key2", keys);
    }

    [Fact]
    public async Task Secrets_Are_Isolated_Between_Provider_Instances()
    {
        var provider1 = BuildProvider();
        var provider2 = BuildProvider();

        await provider1.SetSecretAsync("my-key", "my-value");

        Assert.Null(await provider2.GetSecretAsync("my-key"));
    }
}
