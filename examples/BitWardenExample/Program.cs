using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BitWardenExample;
using Microsoft.Extensions.Configuration;

// ---------------------------------------------------------------------------
// BitWarden / VaultWarden connection + decryption diagnostic.
//
// Proves out the "personal API key + client-side decryption" approach against a
// real server: prelogin -> derive master key -> authenticate -> /api/sync ->
// unwrap the user (and organization) keys -> decrypt vault items to plaintext.
//
// Configure in ONE of these ways (later wins):
//   1. appsettings.local.json   (copy from the .example file; gitignored)
//   2. Environment variables: BITWARDEN_SERVER_URL, BITWARDEN_CLIENT_ID,
//      BITWARDEN_CLIENT_SECRET, BITWARDEN_EMAIL, BITWARDEN_MASTER_PASSWORD
//
// NOTE: reading vault items requires a PERSONAL API key (client id "user.<guid>",
// NOT "organization.*") plus the account email and master password. The master
// password is the only source of the decryption key - a token cannot decrypt.
// ---------------------------------------------------------------------------

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var bw = config.GetSection("BitWarden");
var serverUrl = FirstNonEmpty(Env("BITWARDEN_SERVER_URL"), bw["ServerUrl"]);
var clientId = FirstNonEmpty(Env("BITWARDEN_CLIENT_ID"), bw["ClientId"]);
var clientSecret = FirstNonEmpty(Env("BITWARDEN_CLIENT_SECRET"), bw["ClientSecret"]);
var email = FirstNonEmpty(Env("BITWARDEN_EMAIL"), bw["Email"]);
var masterPassword = FirstNonEmpty(Env("BITWARDEN_MASTER_PASSWORD"), bw["MasterPassword"]);
var identityUrlCfg = FirstNonEmpty(Env("BITWARDEN_IDENTITY_URL"), bw["IdentityUrl"]);

Console.WriteLine("=== BitWarden / VaultWarden connection + decryption diagnostic ===\n");

// ---- Validate configuration --------------------------------------------------
var missing = new List<string>();
if (string.IsNullOrWhiteSpace(serverUrl)) missing.Add("ServerUrl");
if (string.IsNullOrWhiteSpace(clientId)) missing.Add("ClientId");
if (string.IsNullOrWhiteSpace(clientSecret)) missing.Add("ClientSecret");
if (string.IsNullOrWhiteSpace(email)) missing.Add("Email");
if (string.IsNullOrWhiteSpace(masterPassword)) missing.Add("MasterPassword");
if (missing.Count > 0)
{
    Fail($"Missing required settings: {string.Join(", ", missing)}. " +
         "Set them in appsettings.local.json or the BITWARDEN_* environment variables.");
    return;
}

if (!clientId!.StartsWith("user.", StringComparison.OrdinalIgnoreCase))
{
    Fail($"ClientId is '{clientId}'. This approach needs a PERSONAL API key (client id 'user.<guid>'). " +
         "An Organization API Key ('organization.*') can only reach the Public API and cannot read vault items.");
    return;
}

var serverBase = serverUrl!.TrimEnd('/');
var identityUrl = (string.IsNullOrWhiteSpace(identityUrlCfg) ? $"{serverBase}/identity" : identityUrlCfg).TrimEnd('/');

Console.WriteLine("Resolved configuration:");
Console.WriteLine($"  ServerUrl      : {serverBase}");
Console.WriteLine($"  IdentityUrl    : {identityUrl}");
Console.WriteLine($"  Email          : {email}");
Console.WriteLine($"  ClientId       : {clientId}");
Console.WriteLine($"  ClientSecret   : {Mask(clientSecret)}");
Console.WriteLine($"  MasterPassword : {Mask(masterPassword)}");
Console.WriteLine();

var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

try
{
    // ---- Step 1: prelogin (KDF params) --------------------------------------
    Console.WriteLine($"[1/6] POST {identityUrl}/accounts/prelogin");
    PreloginResponse prelogin;
    {
        using var resp = await http.PostAsJsonAsync($"{identityUrl}/accounts/prelogin", new { email });
        var body = await resp.Content.ReadAsStringAsync();
        Console.WriteLine($"      -> {(int)resp.StatusCode} {resp.StatusCode}");
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"      Response body:\n{Indent(Truncate(body, 1500))}");
            Fail("Prelogin failed - check the server/identity URL and that the email exists.");
            return;
        }
        prelogin = JsonSerializer.Deserialize<PreloginResponse>(body, jsonOpts) ?? new PreloginResponse();
        var kdfName = prelogin.Kdf == BitwardenCrypto.KdfArgon2id ? "Argon2id" : "PBKDF2-SHA256";
        Console.WriteLine($"      KDF={kdfName} (type {prelogin.Kdf}), iterations={prelogin.KdfIterations}" +
                          (prelogin.Kdf == BitwardenCrypto.KdfArgon2id ? $", memory={prelogin.KdfMemory}MiB, parallelism={prelogin.KdfParallelism}" : ""));
    }
    Console.WriteLine();

    // ---- Step 2: derive master key ------------------------------------------
    Console.WriteLine("[2/6] Deriving master key from master password + email...");
    var masterKey = BitwardenCrypto.DeriveMasterKey(
        masterPassword!, email!, prelogin.Kdf,
        prelogin.KdfIterations,
        prelogin.KdfMemory ?? 0,
        prelogin.KdfParallelism ?? 0);
    var (stretchEnc, stretchMac) = BitwardenCrypto.StretchMasterKey(masterKey);
    Console.WriteLine("      Master key derived and stretched (enc + mac).");
    Console.WriteLine();

    // ---- Step 3: authenticate (personal API key) ----------------------------
    Console.WriteLine($"[3/6] POST {identityUrl}/connect/token  (client_credentials, scope api)");
    string accessToken;
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId!,
            ["client_secret"] = clientSecret!,
            ["scope"] = "api",
            ["deviceType"] = "21",
            ["deviceName"] = "Neillans.Adapters.Secrets.BitWarden.Diagnostic",
            ["deviceIdentifier"] = Guid.NewGuid().ToString()
        };
        using var resp = await http.PostAsync($"{identityUrl}/connect/token", new FormUrlEncodedContent(form));
        var body = await resp.Content.ReadAsStringAsync();
        Console.WriteLine($"      -> {(int)resp.StatusCode} {resp.StatusCode}");
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"      Response body:\n{Indent(Truncate(body, 1500))}");
            Fail("Authentication failed - check the personal API key client id/secret.");
            return;
        }
        using var doc = JsonDocument.Parse(body);
        accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
        Console.WriteLine($"      Access token acquired ({Mask(accessToken)}).");
    }
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    Console.WriteLine();

    // ---- Step 4: sync (encrypted vault) -------------------------------------
    Console.WriteLine($"[4/6] GET {serverBase}/api/sync?excludeDomains=true");
    SyncResponse sync;
    {
        using var resp = await http.GetAsync($"{serverBase}/api/sync?excludeDomains=true");
        var body = await resp.Content.ReadAsStringAsync();
        Console.WriteLine($"      -> {(int)resp.StatusCode} {resp.StatusCode}");
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"      Response body:\n{Indent(Truncate(body, 1500))}");
            Fail("Sync failed.");
            return;
        }
        sync = JsonSerializer.Deserialize<SyncResponse>(body, jsonOpts) ?? new SyncResponse();
        Console.WriteLine($"      Retrieved {sync.Ciphers?.Count ?? 0} cipher(s), " +
                          $"{sync.Profile?.Organizations?.Count ?? 0} organization(s).");
    }
    Console.WriteLine();

    // ---- Step 5: unwrap keys and decrypt ------------------------------------
    Console.WriteLine("[5/6] Unwrapping keys and decrypting vault items...");
    if (sync.Profile?.Key is not { Length: > 0 })
    {
        Fail("Sync response had no profile key - cannot decrypt.");
        return;
    }

    // User symmetric key (64 bytes: enc[0..32] + mac[32..64]).
    var userSymKey = BitwardenCrypto.DecryptSymmetric(sync.Profile.Key, stretchEnc, stretchMac);
    Console.WriteLine($"      User symmetric key unwrapped ({userSymKey.Length} bytes).");

    // Organization keys, unwrapped via the user's RSA private key.
    var orgKeys = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    if (sync.Profile.Organizations is { Count: > 0 } && sync.Profile.PrivateKey is { Length: > 0 })
    {
        var privateKey = BitwardenCrypto.DecryptSymmetric(sync.Profile.PrivateKey, userSymKey[..32], userSymKey[32..64]);
        foreach (var org in sync.Profile.Organizations)
        {
            if (string.IsNullOrEmpty(org.Id) || string.IsNullOrEmpty(org.Key)) continue;
            try
            {
                orgKeys[org.Id] = BitwardenCrypto.DecryptAsymmetric(org.Key, privateKey);
                Console.WriteLine($"      Organization key unwrapped for {org.Id}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      WARN: could not unwrap org key {org.Id}: {ex.Message}");
            }
        }
    }
    Console.WriteLine();

    // Decrypt and display.
    var ciphers = sync.Ciphers ?? new List<CipherDto>();
    Console.WriteLine($"Decrypted {ciphers.Count} item(s):");
    var shown = 0;
    foreach (var cipher in ciphers)
    {
        byte[] key;
        if (!string.IsNullOrEmpty(cipher.OrganizationId))
        {
            if (!orgKeys.TryGetValue(cipher.OrganizationId, out key!))
            {
                Console.WriteLine($"  - [no org key] {cipher.Id}");
                continue;
            }
        }
        else
        {
            key = userSymKey;
        }

        var enc = key[..32];
        var mac = key[32..64];

        var name = TryDecrypt(cipher.Name, enc, mac);
        var password = TryDecrypt(cipher.Login?.Password, enc, mac);
        var notes = TryDecrypt(cipher.Notes, enc, mac);

        var valuePreview = password is not null ? "password=***"
            : notes is not null ? "notes=***"
            : "(no login password / notes)";
        Console.WriteLine($"  - {name ?? "(name failed to decrypt)"}  [{valuePreview}]");

        if (++shown >= 25) { Console.WriteLine($"  ... ({ciphers.Count - shown} more)"); break; }
    }

    Console.WriteLine("\n✅ Standalone decrypt chain works.");
    Console.WriteLine();

    // ---- Step 6: exercise the real adapter (parity check) -------------------
    Console.WriteLine("[6/6] Exercising the real BitWardenSecretsProvider (the shipped adapter)...");
    var provider = new Neillans.Adapters.Secrets.BitWarden.BitWardenSecretsProvider(
        Microsoft.Extensions.Options.Options.Create(new Neillans.Adapters.Secrets.BitWarden.BitWardenOptions
        {
            ServerUrl = serverBase,
            ClientId = clientId!,
            ClientSecret = clientSecret!,
            Email = email!,
            MasterPassword = masterPassword!,
            IdentityUrl = identityUrlCfg
        }));

    var names = (await provider.ListSecretsAsync()).ToList();
    Console.WriteLine($"      adapter.ListSecretsAsync() returned {names.Count} secret(s).");
    foreach (var n in names.Take(10))
        Console.WriteLine($"        - {n}");
    if (names.Count > 0)
    {
        var value = await provider.GetSecretAsync(names[0]);
        Console.WriteLine($"      adapter.GetSecretAsync(\"{names[0]}\") -> {(value != null ? "*** (value retrieved)" : "null")}");
    }

    Console.WriteLine("\n✅ Success - the shipped adapter authenticates and decrypts against your server.");
}
catch (Exception ex)
{
    Console.WriteLine($"\nEXCEPTION: {Describe(ex)}");
    Fail("The diagnostic threw - see the step output above to pinpoint the failing stage.");
}

// ---- helpers ----------------------------------------------------------------
static string? TryDecrypt(string? encString, byte[] enc, byte[] mac)
{
    if (string.IsNullOrEmpty(encString)) return null;
    try { return BitwardenCrypto.DecryptSymmetricToString(encString, enc, mac); }
    catch { return null; }
}

static string? Env(string name) => Environment.GetEnvironmentVariable(name);

static string FirstNonEmpty(params string?[] values)
    => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

static string Mask(string? s)
{
    if (string.IsNullOrEmpty(s)) return "(empty)";
    if (s.Length <= 6) return new string('*', s.Length);
    return $"{s[..3]}…{s[^3..]} (len {s.Length})";
}

static string Truncate(string s, int max)
    => string.IsNullOrEmpty(s) ? "(empty)" : (s.Length <= max ? s : s[..max] + $"\n… [truncated, {s.Length} chars total]");

static string Indent(string s)
    => string.Join('\n', s.Split('\n').Select(l => "        " + l));

static string Describe(Exception ex)
{
    var parts = new List<string>();
    for (var e = ex; e != null; e = e.InnerException)
        parts.Add($"{e.GetType().Name}: {e.Message}");
    return string.Join("\n         -> ", parts);
}

static void Fail(string message) => Console.WriteLine($"\n❌ {message}");

// ---- DTOs -------------------------------------------------------------------
file sealed class PreloginResponse
{
    public int Kdf { get; set; }
    public int KdfIterations { get; set; } = 600_000;
    public int? KdfMemory { get; set; }
    public int? KdfParallelism { get; set; }
}

file sealed class SyncResponse
{
    public ProfileDto? Profile { get; set; }
    public List<CipherDto>? Ciphers { get; set; }
}

file sealed class ProfileDto
{
    public string? Key { get; set; }
    public string? PrivateKey { get; set; }
    public List<OrgDto>? Organizations { get; set; }
}

file sealed class OrgDto
{
    public string? Id { get; set; }
    public string? Key { get; set; }
}

file sealed class CipherDto
{
    public string? Id { get; set; }
    public string? OrganizationId { get; set; }
    public string? Name { get; set; }
    public string? Notes { get; set; }
    public int Type { get; set; }
    public LoginDto? Login { get; set; }
    public List<FieldDto>? Fields { get; set; }
}

file sealed class LoginDto
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

file sealed class FieldDto
{
    public string? Name { get; set; }
    public string? Value { get; set; }
}
