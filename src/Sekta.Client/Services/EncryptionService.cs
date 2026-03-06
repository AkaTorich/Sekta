using System.Security.Cryptography;
using System.Text;

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
}
