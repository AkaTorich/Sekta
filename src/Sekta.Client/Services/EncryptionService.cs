using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sekta.Client.Services;

public interface IEncryptionService
{
    /// <summary>
    /// Generate a new ECDH key pair (NIST P-256).
    /// Returns (publicKey, privateKey) as exportable byte arrays.
    /// </summary>
    (byte[] publicKey, byte[] privateKey) GenerateKeyPair();

    /// <summary>
    /// Derive a shared secret from our private key and their public key using ECDH + HKDF.
    /// </summary>
    byte[] DeriveSharedSecret(byte[] privateKey, byte[] theirPublicKey);

    /// <summary>
    /// Encrypt a plaintext message using AES-256-GCM with a key derived from the shared secret.
    /// Returns (ciphertext, nonce, tag).
    /// </summary>
    (byte[] ciphertext, byte[] nonce, byte[] tag) Encrypt(string plaintext, byte[] sharedSecret);

    /// <summary>
    /// Decrypt a ciphertext using AES-256-GCM.
    /// </summary>
    string Decrypt(byte[] ciphertext, byte[] nonce, byte[] tag, byte[] sharedSecret);

    /// <summary>
    /// Ratchet: derive the next chain key from the current one (Double Ratchet-inspired).
    /// </summary>
    byte[] RatchetKey(byte[] currentKey);

    /// <summary>
    /// Persist the key pair into MAUI SecureStorage.
    /// </summary>
    Task SaveKeyPair(byte[] publicKey, byte[] privateKey);

    /// <summary>
    /// Load a previously saved key pair from MAUI SecureStorage.
    /// Returns null if no key pair has been stored yet.
    /// </summary>
    Task<(byte[] publicKey, byte[] privateKey)?> LoadKeyPair();

    /// <summary>
    /// Load or generate keys and cache them in memory. Returns true on success.
    /// </summary>
    Task<bool> EnsureKeysLoadedAsync();

    /// <summary>The loaded public key (available after EnsureKeysLoadedAsync).</summary>
    byte[]? MyPublicKey { get; }

    /// <summary>The loaded private key (available after EnsureKeysLoadedAsync).</summary>
    byte[]? MyPrivateKey { get; }

    /// <summary>Check if a message content string is an E2E encrypted payload.</summary>
    bool IsEncryptedContent(string? content);

    /// <summary>Pack encrypted data into a JSON string for the Content field.</summary>
    string PackEncryptedContent(byte[] ciphertext, byte[] nonce, byte[] tag, byte[] senderPublicKey);

    /// <summary>Try to unpack an encrypted content string. Returns null if not encrypted.</summary>
    (byte[] ciphertext, byte[] nonce, byte[] tag, byte[] senderPublicKey)? TryUnpackEncryptedContent(string? content);

    /// <summary>Encrypt raw bytes (for files).</summary>
    (byte[] ciphertext, byte[] nonce, byte[] tag) EncryptBytes(byte[] data, byte[] sharedSecret);

    /// <summary>Decrypt raw bytes (for files).</summary>
    byte[] DecryptBytes(byte[] ciphertext, byte[] nonce, byte[] tag, byte[] sharedSecret);

    /// <summary>Check if content is encrypted file metadata.</summary>
    bool IsEncryptedFileContent(string? content);

    /// <summary>Pack encrypted file metadata into Content field.</summary>
    string PackEncryptedFileContent(byte[] nonce, byte[] tag, byte[] senderPublicKey, string? caption);

    /// <summary>Unpack encrypted file metadata.</summary>
    (byte[] nonce, byte[] tag, byte[] senderPublicKey, string? caption)? TryUnpackEncryptedFileContent(string? content);
}

public class EncryptionService : IEncryptionService
{
    private const string PublicKeyStorageKey = "e2e_public_key";
    private const string PrivateKeyStorageKey = "e2e_private_key";

    private const int NonceSizeBytes = 12; // 96 bits for AES-GCM
    private const int TagSizeBytes = 16;   // 128-bit authentication tag
    private const int KeySizeBytes = 32;   // 256-bit AES key

    private static readonly byte[] HkdfInfo = Encoding.UTF8.GetBytes("sekta-e2e-v1");
    private static readonly byte[] RatchetLabel = Encoding.UTF8.GetBytes("ratchet");

    // ──────────────────────────────────────────────
    //  Key generation
    // ──────────────────────────────────────────────

    public (byte[] publicKey, byte[] privateKey) GenerateKeyPair()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        // Export the full private parameters (includes the public point)
        var privateParams = ecdh.ExportECPrivateKey();

        // Export the public key in the X.509 SubjectPublicKeyInfo format
        var publicParams = ecdh.ExportSubjectPublicKeyInfo();

        return (publicParams, privateParams);
    }

    // ──────────────────────────────────────────────
    //  Key agreement
    // ──────────────────────────────────────────────

    public byte[] DeriveSharedSecret(byte[] privateKey, byte[] theirPublicKey)
    {
        using var ecdh = ECDiffieHellman.Create();
        ecdh.ImportECPrivateKey(privateKey, out _);

        using var peerEcdh = ECDiffieHellman.Create();
        peerEcdh.ImportSubjectPublicKeyInfo(theirPublicKey, out _);

        // Raw ECDH shared secret
        var rawSecret = ecdh.DeriveRawSecretAgreement(peerEcdh.PublicKey);

        // Run through HKDF to produce a uniform 256-bit key
        var derivedKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: rawSecret,
            outputLength: KeySizeBytes,
            salt: (byte[]?)null,
            info: HkdfInfo);

        // Clear the raw secret from memory
        CryptographicOperations.ZeroMemory(rawSecret);

        return derivedKey;
    }

    // ──────────────────────────────────────────────
    //  Authenticated encryption (AES-256-GCM)
    // ──────────────────────────────────────────────

    public (byte[] ciphertext, byte[] nonce, byte[] tag) Encrypt(string plaintext, byte[] sharedSecret)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];

        // Derive a per-message encryption key via HKDF using the nonce as salt,
        // so even if the same sharedSecret is reused the AES key differs per message.
        var messageKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: sharedSecret,
            outputLength: KeySizeBytes,
            salt: nonce,
            info: HkdfInfo);

        using var aes = new AesGcm(messageKey, TagSizeBytes);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        CryptographicOperations.ZeroMemory(messageKey);

        return (ciphertext, nonce, tag);
    }

    public string Decrypt(byte[] ciphertext, byte[] nonce, byte[] tag, byte[] sharedSecret)
    {
        var plaintextBytes = new byte[ciphertext.Length];

        var messageKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: sharedSecret,
            outputLength: KeySizeBytes,
            salt: nonce,
            info: HkdfInfo);

        using var aes = new AesGcm(messageKey, TagSizeBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);

        CryptographicOperations.ZeroMemory(messageKey);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    // ──────────────────────────────────────────────
    //  Symmetric ratchet
    // ──────────────────────────────────────────────

    public byte[] RatchetKey(byte[] currentKey)
    {
        // KDF chain step: nextKey = HMAC-SHA256(currentKey, "ratchet")
        using var hmac = new HMACSHA256(currentKey);
        return hmac.ComputeHash(RatchetLabel);
    }

    // ──────────────────────────────────────────────
    //  Secure storage (MAUI SecureStorage)
    // ──────────────────────────────────────────────

    public async Task SaveKeyPair(byte[] publicKey, byte[] privateKey)
    {
        await SecureStorage.Default.SetAsync(
            PublicKeyStorageKey, Convert.ToBase64String(publicKey));

        await SecureStorage.Default.SetAsync(
            PrivateKeyStorageKey, Convert.ToBase64String(privateKey));
    }

    public async Task<(byte[] publicKey, byte[] privateKey)?> LoadKeyPair()
    {
        var publicKeyBase64 = await SecureStorage.Default.GetAsync(PublicKeyStorageKey);
        var privateKeyBase64 = await SecureStorage.Default.GetAsync(PrivateKeyStorageKey);

        if (string.IsNullOrEmpty(publicKeyBase64) || string.IsNullOrEmpty(privateKeyBase64))
            return null;

        return (
            Convert.FromBase64String(publicKeyBase64),
            Convert.FromBase64String(privateKeyBase64));
    }

    // ──────────────────────────────────────────────
    //  Key lifecycle
    // ──────────────────────────────────────────────

    public byte[]? MyPublicKey { get; private set; }
    public byte[]? MyPrivateKey { get; private set; }

    public async Task<bool> EnsureKeysLoadedAsync()
    {
        if (MyPublicKey is not null && MyPrivateKey is not null)
            return true;

        try
        {
            var keys = await LoadKeyPair();
            if (keys is null)
            {
                var (pub, priv) = GenerateKeyPair();
                await SaveKeyPair(pub, priv);
                MyPublicKey = pub;
                MyPrivateKey = priv;
            }
            else
            {
                MyPublicKey = keys.Value.publicKey;
                MyPrivateKey = keys.Value.privateKey;
            }
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[E2E] Failed to load/generate keys: {ex.Message}");
            return false;
        }
    }

    // ──────────────────────────────────────────────
    //  Content packing (server-transparent)
    // ──────────────────────────────────────────────

    private const string E2ePrefix = "{\"e2e\":1,";

    public bool IsEncryptedContent(string? content)
        => content is not null && content.StartsWith(E2ePrefix, StringComparison.Ordinal);

    public string PackEncryptedContent(byte[] ciphertext, byte[] nonce, byte[] tag, byte[] senderPublicKey)
    {
        return JsonSerializer.Serialize(new
        {
            e2e = 1,
            c = Convert.ToBase64String(ciphertext),
            n = Convert.ToBase64String(nonce),
            t = Convert.ToBase64String(tag),
            k = Convert.ToBase64String(senderPublicKey)
        });
    }

    public (byte[] ciphertext, byte[] nonce, byte[] tag, byte[] senderPublicKey)? TryUnpackEncryptedContent(string? content)
    {
        if (!IsEncryptedContent(content))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(content!);
            var root = doc.RootElement;
            return (
                Convert.FromBase64String(root.GetProperty("c").GetString()!),
                Convert.FromBase64String(root.GetProperty("n").GetString()!),
                Convert.FromBase64String(root.GetProperty("t").GetString()!),
                Convert.FromBase64String(root.GetProperty("k").GetString()!)
            );
        }
        catch
        {
            return null;
        }
    }

    // ──────────────────────────────────────────────
    //  File encryption (AES-256-GCM on raw bytes)
    // ──────────────────────────────────────────────

    public (byte[] ciphertext, byte[] nonce, byte[] tag) EncryptBytes(byte[] data, byte[] sharedSecret)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[data.Length];
        var tag = new byte[TagSizeBytes];

        var messageKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: sharedSecret,
            outputLength: KeySizeBytes,
            salt: nonce,
            info: HkdfInfo);

        using var aes = new AesGcm(messageKey, TagSizeBytes);
        aes.Encrypt(nonce, data, ciphertext, tag);

        CryptographicOperations.ZeroMemory(messageKey);

        return (ciphertext, nonce, tag);
    }

    public byte[] DecryptBytes(byte[] ciphertext, byte[] nonce, byte[] tag, byte[] sharedSecret)
    {
        var plaintext = new byte[ciphertext.Length];

        var messageKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: sharedSecret,
            outputLength: KeySizeBytes,
            salt: nonce,
            info: HkdfInfo);

        using var aes = new AesGcm(messageKey, TagSizeBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        CryptographicOperations.ZeroMemory(messageKey);

        return plaintext;
    }

    // ──────────────────────────────────────────────
    //  Encrypted file metadata packing
    // ──────────────────────────────────────────────

    private const string E2eFilePrefix = "{\"e2e_file\":1,";

    public bool IsEncryptedFileContent(string? content)
        => content is not null && content.StartsWith(E2eFilePrefix, StringComparison.Ordinal);

    public string PackEncryptedFileContent(byte[] nonce, byte[] tag, byte[] senderPublicKey, string? caption)
    {
        return JsonSerializer.Serialize(new
        {
            e2e_file = 1,
            n = Convert.ToBase64String(nonce),
            t = Convert.ToBase64String(tag),
            k = Convert.ToBase64String(senderPublicKey),
            cap = caption
        });
    }

    public (byte[] nonce, byte[] tag, byte[] senderPublicKey, string? caption)? TryUnpackEncryptedFileContent(string? content)
    {
        if (!IsEncryptedFileContent(content))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(content!);
            var root = doc.RootElement;
            string? caption = null;
            if (root.TryGetProperty("cap", out var capEl) && capEl.ValueKind == JsonValueKind.String)
                caption = capEl.GetString();

            return (
                Convert.FromBase64String(root.GetProperty("n").GetString()!),
                Convert.FromBase64String(root.GetProperty("t").GetString()!),
                Convert.FromBase64String(root.GetProperty("k").GetString()!),
                caption
            );
        }
        catch
        {
            return null;
        }
    }
}
