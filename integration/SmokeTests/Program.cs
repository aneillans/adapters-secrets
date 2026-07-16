using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Neillans.Adapters.Secrets.BitWarden;
using Neillans.Adapters.Secrets.Core;
using Neillans.Adapters.Secrets.HashiCorpVault;
using Neillans.Adapters.Secrets.Infisical;

// -----------------------------------------------------------------------------
// Standalone, offline smoke harness for the BitWarden/VaultWarden, Infisical, and
// HashiCorp Vault adapters. It drives the real providers against locally running containers that
// have been seeded by ../bootstrap. Nothing here talks to a hosted service.
//
// Configuration comes entirely from environment variables (see ../.env.example):
//   SMOKE_SEED_JSON  - JSON object of the seeded { key: value } pairs to verify.
//   BITWARDEN_*      - VaultWarden connection + personal API key (optional block).
//   INFISICAL_*      - Infisical machine-identity connection (optional block).
//   ALLOW_MUTATING_TESTS=true - also exercise Infisical write/delete.
//
// A provider block is skipped (not failed) when its variables are absent, so you
// can smoke one system at a time. Exit code is the number of failed checks.
// -----------------------------------------------------------------------------

var runner = new Runner();

var seedJson = Env("SMOKE_SEED_JSON");
if (string.IsNullOrWhiteSpace(seedJson))
{
    Console.Error.WriteLine("SMOKE_SEED_JSON is required (the expected { key: value } pairs). See integration/.env.example.");
    return 2;
}

Dictionary<string, string> expected;
try
{
    expected = JsonSerializer.Deserialize<Dictionary<string, string>>(seedJson!)
               ?? throw new InvalidOperationException("SMOKE_SEED_JSON deserialized to null");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"SMOKE_SEED_JSON is not valid JSON: {ex.Message}");
    return 2;
}

if (expected.Count == 0)
{
    Console.Error.WriteLine("SMOKE_SEED_JSON contained no entries.");
    return 2;
}

Console.WriteLine($"Expecting {expected.Count} seeded secret(s): {string.Join(", ", expected.Keys)}");
Console.WriteLine();

await SmokeBitWarden(runner, expected);
await SmokeInfisical(runner, expected);
await SmokeVault(runner, expected);

return runner.Summarize();

// ---- VaultWarden / BitWarden ------------------------------------------------

async Task SmokeBitWarden(Runner r, IReadOnlyDictionary<string, string> want)
{
    if (!Present("BITWARDEN_SERVER_URL", "BITWARDEN_CLIENT_ID", "BITWARDEN_CLIENT_SECRET",
                 "BITWARDEN_EMAIL", "BITWARDEN_MASTER_PASSWORD"))
    {
        r.Skip("VaultWarden", "BITWARDEN_* environment variables not set");
        return;
    }

    r.Section("VaultWarden");

    var services = new ServiceCollection();
    services.AddBitWardenSecretsProvider(o =>
    {
        o.ServerUrl = Env("BITWARDEN_SERVER_URL")!;
        o.ClientId = Env("BITWARDEN_CLIENT_ID")!;
        o.ClientSecret = Env("BITWARDEN_CLIENT_SECRET")!;
        o.Email = Env("BITWARDEN_EMAIL")!;
        o.MasterPassword = Env("BITWARDEN_MASTER_PASSWORD")!;
        var identity = Env("BITWARDEN_IDENTITY_URL");
        if (!string.IsNullOrWhiteSpace(identity)) o.IdentityUrl = identity;
    });
    var provider = r.Construct("VaultWarden", () => services.BuildServiceProvider().GetRequiredService<ISecretsProvider>());
    if (provider is null) return;

    await r.Check("ListSecretsAsync returns the seeded keys", async () =>
    {
        var keys = (await provider.ListSecretsAsync()).ToHashSet();
        foreach (var k in want.Keys)
            Assert(keys.Contains(k), $"listed keys did not contain '{k}' (got: {string.Join(", ", keys)})");
    });

    foreach (var (key, value) in want)
    {
        await r.Check($"GetSecretAsync(\"{key}\") == expected value", async () =>
        {
            var actual = await provider.GetSecretAsync(key);
            Assert(actual == value, $"expected '{value}', got '{actual ?? "<null>"}'");
        });
    }

    await r.Check("GetSecretAsync(<missing>) returns null", async () =>
    {
        var actual = await provider.GetSecretAsync($"definitely-not-seeded-{Guid.NewGuid():N}");
        Assert(actual is null, $"expected null, got '{actual}'");
    });

    await r.Check("SetSecretAsync throws (read-only adapter)", async () =>
        await AssertThrows<SecretsProviderException>(() => provider.SetSecretAsync("k", "v")));

    await r.Check("DeleteSecretAsync throws (read-only adapter)", async () =>
        await AssertThrows<SecretsProviderException>(() => provider.DeleteSecretAsync("k")));
}

// ---- Infisical --------------------------------------------------------------

async Task SmokeInfisical(Runner r, IReadOnlyDictionary<string, string> want)
{
    if (!Present("INFISICAL_CLIENT_ID", "INFISICAL_CLIENT_SECRET", "INFISICAL_PROJECT_ID", "INFISICAL_ENVIRONMENT"))
    {
        r.Skip("Infisical", "INFISICAL_* environment variables not set");
        return;
    }

    r.Section("Infisical");

    var services = new ServiceCollection();
    services.AddInfisicalSecretsProvider(o =>
    {
        o.ClientId = Env("INFISICAL_CLIENT_ID")!;
        o.ClientSecret = Env("INFISICAL_CLIENT_SECRET")!;
        o.ProjectId = Env("INFISICAL_PROJECT_ID")!;
        o.Environment = Env("INFISICAL_ENVIRONMENT")!;
        o.SiteUrl = Env("INFISICAL_SITE_URL") ?? "https://app.infisical.com";
        o.SecretPath = Env("INFISICAL_SECRET_PATH");
    });
    var provider = r.Construct("Infisical", () => services.BuildServiceProvider().GetRequiredService<ISecretsProvider>());
    if (provider is null) return;

    await r.Check("ListSecretsAsync returns the seeded keys", async () =>
    {
        var keys = (await provider.ListSecretsAsync()).ToHashSet();
        foreach (var k in want.Keys)
            Assert(keys.Contains(k), $"listed keys did not contain '{k}' (got: {string.Join(", ", keys)})");
    });

    foreach (var (key, value) in want)
    {
        await r.Check($"GetSecretAsync(\"{key}\") == expected value", async () =>
        {
            var actual = await provider.GetSecretAsync(key);
            Assert(actual == value, $"expected '{value}', got '{actual ?? "<null>"}'");
        });
    }

    await r.Check("GetSecretAsync(<missing>) returns null", async () =>
    {
        var actual = await provider.GetSecretAsync($"definitely-not-seeded-{Guid.NewGuid():N}");
        Assert(actual is null, $"expected null, got '{actual}'");
    });

    if (Env("ALLOW_MUTATING_TESTS") == "true")
    {
        var key = $"smoke-mutating-{Guid.NewGuid():N}";
        var val = Guid.NewGuid().ToString();
        await r.Check("Set -> Get -> Delete round-trips a new secret", async () =>
        {
            await provider.SetSecretAsync(key, val);
            var fetched = await provider.GetSecretAsync(key);
            Assert(fetched == val, $"after set, expected '{val}', got '{fetched ?? "<null>"}'");

            await provider.DeleteSecretAsync(key);
            var afterDelete = await provider.GetSecretAsync(key);
            Assert(afterDelete is null, $"after delete, expected null, got '{afterDelete}'");
        });
    }
    else
    {
        r.Note("Skipping Infisical write/delete round-trip (set ALLOW_MUTATING_TESTS=true to enable).");
    }
}

// ---- HashiCorp Vault --------------------------------------------------------

async Task SmokeVault(Runner r, IReadOnlyDictionary<string, string> want)
{
    if (!Present("VAULT_ADDR", "VAULT_TOKEN"))
    {
        r.Skip("HashiCorp Vault", "VAULT_* environment variables not set");
        return;
    }

    r.Section("HashiCorp Vault");

    var services = new ServiceCollection();
    services.AddHashiCorpVaultSecretsProvider(o =>
    {
        o.VaultAddress = Env("VAULT_ADDR")!;
        o.Token = Env("VAULT_TOKEN")!;
        o.MountPoint = Env("VAULT_MOUNT") ?? "secret";
        o.BasePath = Env("VAULT_BASE_PATH") ?? string.Empty;
        o.ValueKey = Env("VAULT_VALUE_KEY") ?? "value";
    });
    var provider = r.Construct("HashiCorp Vault", () => services.BuildServiceProvider().GetRequiredService<ISecretsProvider>());
    if (provider is null) return;

    await r.Check("ListSecretsAsync returns the seeded keys", async () =>
    {
        var keys = (await provider.ListSecretsAsync()).ToHashSet();
        foreach (var k in want.Keys)
            Assert(keys.Contains(k), $"listed keys did not contain '{k}' (got: {string.Join(", ", keys)})");
    });

    foreach (var (key, value) in want)
    {
        await r.Check($"GetSecretAsync(\"{key}\") == expected value", async () =>
        {
            var actual = await provider.GetSecretAsync(key);
            Assert(actual == value, $"expected '{value}', got '{actual ?? "<null>"}'");
        });
    }

    await r.Check("GetSecretAsync(<missing>) returns null", async () =>
    {
        var actual = await provider.GetSecretAsync($"definitely-not-seeded-{Guid.NewGuid():N}");
        Assert(actual is null, $"expected null, got '{actual}'");
    });

    if (Env("ALLOW_MUTATING_TESTS") == "true")
    {
        var key = $"smoke-mutating-{Guid.NewGuid():N}";
        var val = Guid.NewGuid().ToString();
        await r.Check("Set -> Get -> Delete round-trips a new secret", async () =>
        {
            await provider.SetSecretAsync(key, val);
            var fetched = await provider.GetSecretAsync(key);
            Assert(fetched == val, $"after set, expected '{val}', got '{fetched ?? "<null>"}'");

            await provider.DeleteSecretAsync(key);
            var afterDelete = await provider.GetSecretAsync(key);
            Assert(afterDelete is null, $"after delete, expected null, got '{afterDelete}'");
        });
    }
    else
    {
        r.Note("Skipping Vault write/delete round-trip (set ALLOW_MUTATING_TESTS=true to enable).");
    }
}

// ---- helpers ----------------------------------------------------------------

static string? Env(string name) => Environment.GetEnvironmentVariable(name);
static bool Present(params string[] names) => names.All(n => !string.IsNullOrWhiteSpace(Env(n)));

static void Assert(bool condition, string message)
{
    if (!condition) throw new SmokeFailure(message);
}

static async Task AssertThrows<TException>(Func<Task> action) where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }
    catch (Exception ex)
    {
        throw new SmokeFailure($"expected {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message}");
    }
    throw new SmokeFailure($"expected {typeof(TException).Name}, but no exception was thrown");
}

sealed class SmokeFailure(string message) : Exception(message);

sealed class Runner
{
    private int _passed;
    private int _failed;
    private int _skipped;

    public void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"== {title} ==");
    }

    public void Skip(string title, string reason)
    {
        _skipped++;
        Console.WriteLine();
        Write(ConsoleColor.DarkYellow, $"SKIP {title}: {reason}");
    }

    public void Note(string message) => Write(ConsoleColor.DarkGray, $"  - {message}");

    public void Fail(string description, Exception ex)
    {
        _failed++;
        Write(ConsoleColor.Red, $"  FAIL  {description}");
        Write(ConsoleColor.Red, $"        {ex.GetType().Name}: {ex.Message}");
    }

    /// <summary>
    /// Constructs a provider, turning a construction failure (e.g. a provider that authenticates in
    /// its constructor) into a reported failure rather than an unhandled crash that would skip every
    /// remaining provider.
    /// </summary>
    public ISecretsProvider? Construct(string section, Func<ISecretsProvider> factory)
    {
        try
        {
            return factory();
        }
        catch (Exception ex)
        {
            Fail($"{section}: construct provider", ex);
            return null;
        }
    }

    public async Task Check(string description, Func<Task> body)
    {
        try
        {
            await body();
            _passed++;
            Write(ConsoleColor.Green, $"  PASS  {description}");
        }
        catch (Exception ex)
        {
            _failed++;
            var detail = ex is SmokeFailure ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            Write(ConsoleColor.Red, $"  FAIL  {description}");
            Write(ConsoleColor.Red, $"        {detail}");
        }
    }

    public int Summarize()
    {
        Console.WriteLine();
        var color = _failed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
        Write(color, $"Result: {_passed} passed, {_failed} failed, {_skipped} block(s) skipped.");
        return _failed;
    }

    private static void Write(ConsoleColor color, string message)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = previous;
    }
}
