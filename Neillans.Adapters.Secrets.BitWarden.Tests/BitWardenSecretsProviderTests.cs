using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Neillans.Adapters.Secrets.BitWarden;
using Neillans.Adapters.Secrets.Core;
using Xunit;

namespace Neillans.Adapters.Secrets.BitWarden.Tests;

public class BitWardenSecretsProviderTests
{
    private static ISecretsProvider? BuildProvider()
    {
        if (!RequiredPresent("BITWARDEN_SERVER_URL", "BITWARDEN_CLIENT_ID", "BITWARDEN_CLIENT_SECRET",
                             "BITWARDEN_EMAIL", "BITWARDEN_MASTER_PASSWORD"))
            return null;

        var services = new ServiceCollection();
        services.AddBitWardenSecretsProvider(options =>
        {
            options.ServerUrl = Env("BITWARDEN_SERVER_URL")!;
            options.ClientId = Env("BITWARDEN_CLIENT_ID")!;
            options.ClientSecret = Env("BITWARDEN_CLIENT_SECRET")!;
            options.Email = Env("BITWARDEN_EMAIL")!;
            options.MasterPassword = Env("BITWARDEN_MASTER_PASSWORD")!;
        });
        return services.BuildServiceProvider().GetRequiredService<ISecretsProvider>();
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);
    private static bool RequiredPresent(params string[] vars) => vars.All(v => !string.IsNullOrWhiteSpace(Env(v)));

    private static ISecretsProvider Resolve(Action<BitWardenOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddBitWardenSecretsProvider(configure);
        return services.BuildServiceProvider().GetRequiredService<ISecretsProvider>();
    }

    private static void ConfigureValid(BitWardenOptions options)
    {
        options.ServerUrl = "https://vault.example.com";
        options.ClientId = "user.f7b1234d-5f59-4444-8b5a-12345678efac";
        options.ClientSecret = "secret";
        options.Email = "you@example.com";
        options.MasterPassword = "pw";
    }

    // ---- Options validation (offline) ---------------------------------------

    [Fact]
    public void Organization_ClientId_Is_Rejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => Resolve(options =>
        {
            ConfigureValid(options);
            options.ClientId = "organization.f7b1234d-5f59-4444-8b5a-12345678efac";
        }));
        Assert.Contains("personal API key", ex.Message);
    }

    [Fact]
    public void MasterPassword_Is_Required()
    {
        Assert.Throws<ArgumentException>(() => Resolve(options =>
        {
            ConfigureValid(options);
            options.MasterPassword = "";
        }));
    }

    [Fact]
    public void Email_Is_Required()
    {
        Assert.Throws<ArgumentException>(() => Resolve(options =>
        {
            ConfigureValid(options);
            options.Email = "";
        }));
    }

    [Fact]
    public void Valid_Personal_ApiKey_Options_Construct_Successfully()
    {
        var provider = Resolve(ConfigureValid);
        Assert.NotNull(provider);
    }

    // ---- Crypto round-trip (offline) ----------------------------------------

    [Fact]
    public void DecryptSymmetric_RoundTrips_A_Type2_EncString()
    {
        var encKey = RandomNumberGenerator.GetBytes(32);
        var macKey = RandomNumberGenerator.GetBytes(32);
        const string plaintext = "correct horse battery staple";

        var encString = EncryptType2(plaintext, encKey, macKey);

        var decrypted = BitWardenCrypto.DecryptSymmetricToString(encString, encKey, macKey);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void DecryptSymmetric_Throws_On_Wrong_Mac_Key()
    {
        var encKey = RandomNumberGenerator.GetBytes(32);
        var macKey = RandomNumberGenerator.GetBytes(32);
        var encString = EncryptType2("secret", encKey, macKey);

        var wrongMac = RandomNumberGenerator.GetBytes(32);
        Assert.Throws<CryptographicException>(() =>
            BitWardenCrypto.DecryptSymmetricToString(encString, encKey, wrongMac));
    }

    [Fact]
    public void StretchMasterKey_Is_Deterministic_And_32_32()
    {
        var masterKey = RandomNumberGenerator.GetBytes(32);
        var (enc1, mac1) = BitWardenCrypto.StretchMasterKey(masterKey);
        var (enc2, mac2) = BitWardenCrypto.StretchMasterKey(masterKey);

        Assert.Equal(32, enc1.Length);
        Assert.Equal(32, mac1.Length);
        Assert.Equal(enc1, enc2);
        Assert.Equal(mac1, mac2);
        Assert.NotEqual(enc1, mac1);
    }

    /// <summary>Builds a Bitwarden type-2 EncString ("2.iv|ct|mac") for round-trip tests.</summary>
    private static string EncryptType2(string plaintext, byte[] encKey, byte[] macKey)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = encKey;
        aes.GenerateIV();
        var iv = aes.IV;

        using var encryptor = aes.CreateEncryptor();
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var ct = encryptor.TransformFinalBlock(pt, 0, pt.Length);

        using var hmac = new HMACSHA256(macKey);
        hmac.TransformBlock(iv, 0, iv.Length, null, 0);
        hmac.TransformFinalBlock(ct, 0, ct.Length);
        var mac = hmac.Hash!;

        return $"2.{Convert.ToBase64String(iv)}|{Convert.ToBase64String(ct)}|{Convert.ToBase64String(mac)}";
    }

    // ---- Behaviour (offline) ------------------------------------------------

    [Fact]
    public async Task Set_Secret_Throws_NotSupported()
    {
        var provider = Resolve(ConfigureValid);
        await Assert.ThrowsAsync<SecretsProviderException>(() => provider.SetSecretAsync("k", "v"));
    }

    [Fact]
    public async Task Delete_Secret_Throws_NotSupported()
    {
        var provider = Resolve(ConfigureValid);
        await Assert.ThrowsAsync<SecretsProviderException>(() => provider.DeleteSecretAsync("any"));
    }

    // ---- Live integration (only run when env vars are configured) ------------

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
    public async Task Can_Get_Secret_If_Configured()
    {
        var secretKey = Env("TEST_SECRET_KEY");
        var provider = BuildProvider();
        if (provider is null || string.IsNullOrWhiteSpace(secretKey)) return;

        var value = await provider.GetSecretAsync(secretKey!);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Secret '{secretKey}' was not found or empty.");
    }
}
