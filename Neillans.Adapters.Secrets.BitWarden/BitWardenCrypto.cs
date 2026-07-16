using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Neillans.Adapters.Secrets.BitWarden;

/// <summary>
/// Client-side key derivation and EncString decryption for the Bitwarden/VaultWarden
/// vault (Password Manager) API. Vault items are end-to-end encrypted; the server only ever
/// returns ciphertext, so the adapter must reconstruct the key hierarchy locally:
///
///   masterKey      = KDF(masterPassword, normalize(email))                 (32 bytes)
///   stretched      = HKDF-Expand(masterKey, "enc"/"mac")                   (32 + 32 bytes)
///   userSymKey     = AES-CBC-HMAC-decrypt(profile.Key, stretched)          (64 bytes: enc+mac)
///   userPrivateKey = AES-CBC-HMAC-decrypt(profile.PrivateKey, userSymKey)  (PKCS8 RSA DER)
///   orgSymKey      = RSA-OAEP-decrypt(org.Key, userPrivateKey)             (64 bytes: enc+mac)
///   cipher fields  = AES-CBC-HMAC-decrypt(field, user or org sym key)
/// </summary>
internal static class BitWardenCrypto
{
    // KDF type numbers as returned by the prelogin endpoint.
    public const int KdfPbkdf2Sha256 = 0;
    public const int KdfArgon2id = 1;

    /// <summary>Bitwarden uses the trimmed, lower-cased email as the KDF salt material.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Derives the 32-byte master key from the master password and email.</summary>
    public static byte[] DeriveMasterKey(
        string masterPassword, string email, int kdfType,
        int iterations, int memoryMiB, int parallelism)
    {
        var saltText = NormalizeEmail(email);
        var password = Encoding.UTF8.GetBytes(masterPassword);

        switch (kdfType)
        {
            case KdfPbkdf2Sha256:
            {
                var salt = Encoding.UTF8.GetBytes(saltText);
                return Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
            }
            case KdfArgon2id:
            {
                // Argon2 needs a fixed-length salt, so Bitwarden uses SHA-256(email).
                var salt = SHA256.HashData(Encoding.UTF8.GetBytes(saltText));
                using var argon2 = new Argon2id(password)
                {
                    Salt = salt,
                    DegreeOfParallelism = parallelism,
                    MemorySize = memoryMiB * 1024, // Konscious expects KiB
                    Iterations = iterations
                };
                return argon2.GetBytes(32);
            }
            default:
                throw new NotSupportedException($"Unsupported KDF type {kdfType}");
        }
    }

    /// <summary>Stretches the 32-byte master key into a 32-byte AES key + 32-byte MAC key.</summary>
    public static (byte[] enc, byte[] mac) StretchMasterKey(byte[] masterKey)
    {
        var enc = HKDF.Expand(HashAlgorithmName.SHA256, masterKey, 32, Encoding.UTF8.GetBytes("enc"));
        var mac = HKDF.Expand(HashAlgorithmName.SHA256, masterKey, 32, Encoding.UTF8.GetBytes("mac"));
        return (enc, mac);
    }

    /// <summary>
    /// Decrypts a symmetric EncString ("type.iv|ct|mac"). Supports type 0 (AES-CBC, no MAC) and
    /// type 2 (AES-256-CBC + HMAC-SHA256). The 64-byte vault keys split into enc[0..32]/mac[32..64].
    /// </summary>
    public static byte[] DecryptSymmetric(string encString, byte[] encKey, byte[] macKey)
    {
        var (type, parts) = ParseEncString(encString);
        var iv = Convert.FromBase64String(parts[0]);
        var ct = Convert.FromBase64String(parts[1]);

        if (type == 2)
        {
            if (parts.Length < 3)
                throw new CryptographicException("EncString type 2 is missing its MAC segment.");
            var mac = Convert.FromBase64String(parts[2]);

            using var hmac = new HMACSHA256(macKey);
            hmac.TransformBlock(iv, 0, iv.Length, null, 0);
            hmac.TransformFinalBlock(ct, 0, ct.Length);
            if (!CryptographicOperations.FixedTimeEquals(hmac.Hash!, mac))
                throw new CryptographicException("MAC validation failed - wrong key (bad master password?) or corrupted data.");
        }
        else if (type != 0)
        {
            throw new NotSupportedException($"Unsupported symmetric EncString type {type}.");
        }

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = encKey;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(ct, 0, ct.Length);
    }

    public static string DecryptSymmetricToString(string encString, byte[] encKey, byte[] macKey)
        => Encoding.UTF8.GetString(DecryptSymmetric(encString, encKey, macKey));

    /// <summary>
    /// Decrypts an asymmetric (RSA-OAEP) EncString using a PKCS8 private key. Used to unwrap
    /// organization keys. Types: 3/5 = OAEP-SHA256, 4/6 = OAEP-SHA1.
    /// </summary>
    public static byte[] DecryptAsymmetric(string encString, byte[] privateKeyPkcs8)
    {
        var (type, parts) = ParseEncString(encString);
        var data = Convert.FromBase64String(parts[0]);

        var padding = type switch
        {
            3 or 5 => RSAEncryptionPadding.OaepSHA256,
            4 or 6 => RSAEncryptionPadding.OaepSHA1,
            _ => throw new NotSupportedException($"Unsupported asymmetric EncString type {type}.")
        };

        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);
        return rsa.Decrypt(data, padding);
    }

    private static (int type, string[] parts) ParseEncString(string encString)
    {
        if (string.IsNullOrEmpty(encString))
            throw new CryptographicException("Empty EncString.");

        var dot = encString.IndexOf('.');
        int type;
        string payload;
        if (dot >= 0 && int.TryParse(encString[..dot], out type))
            payload = encString[(dot + 1)..];
        else
        {
            // Legacy strings with no type prefix are treated as type 0.
            type = 0;
            payload = encString;
        }

        return (type, payload.Split('|'));
    }
}
