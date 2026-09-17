using System.Security.Cryptography;

namespace SecureFolder.Core.Crypto;

/// <summary>
/// AES-256-GCM authenticated encryption engine.
/// Provides confidentiality and integrity for all encrypted data.
/// </summary>
public sealed class AesGcmEngine : IDisposable
{
    private readonly byte[] _key;
    private bool _disposed;

    public const int NonceSize = 12;  // 96 bits
    public const int TagSize = 16;    // 128 bits

    public AesGcmEngine(byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("AES-256-GCM requires a 32-byte key.", nameof(key));

        _key = new byte[32];
        key.CopyTo(_key, 0);
    }

    /// <summary>
    /// Encrypts plaintext with AES-256-GCM and returns [nonce | ciphertext | tag].
    /// </summary>
    /// <param name="plaintext">Data to encrypt.</param>
    /// <param name="associatedData">Optional additional authenticated data (not encrypted, but authenticated).</param>
    /// <returns>Combined byte array: nonce (12B) + ciphertext + tag (16B).</returns>
    public byte[] Encrypt(byte[] plaintext, byte[]? associatedData = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        byte[] nonce = SecureRandom.GenerateNonce();
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        byte[] result = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(result, 0);
        ciphertext.CopyTo(result, NonceSize);
        tag.CopyTo(result, NonceSize + ciphertext.Length);

        return result;
    }

    /// <summary>
    /// Decrypts data encrypted with AES-256-GCM.
    /// Input format: [nonce (12B) | ciphertext | tag (16B)].
    /// </summary>
    /// <param name="encryptedData">Combined encrypted data.</param>
    /// <param name="associatedData">Optional additional authenticated data.</param>
    /// <returns>Decrypted plaintext.</returns>
    /// <exception cref="CryptographicException">Thrown if authentication fails (wrong key or tampered data).</exception>
    public byte[] Decrypt(byte[] encryptedData, byte[]? associatedData = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (encryptedData.Length < NonceSize + TagSize)
            throw new ArgumentException("Encrypted data too short.");

        int ciphertextLength = encryptedData.Length - NonceSize - TagSize;
        byte[] nonce = encryptedData.AsSpan(0, NonceSize).ToArray();
        byte[] ciphertext = encryptedData.AsSpan(NonceSize, ciphertextLength).ToArray();
        byte[] tag = encryptedData.AsSpan(NonceSize + ciphertextLength, TagSize).ToArray();

        byte[] plaintext = new byte[ciphertextLength];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

        return plaintext;
    }

    /// <summary>
    /// Encrypts data and returns components separately.
    /// Useful for vault format where nonce is stored independently.
    /// </summary>
    public (byte[] Nonce, byte[] Ciphertext, byte[] Tag) EncryptSeparate(
        byte[] plaintext, byte[]? associatedData = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        byte[] nonce = SecureRandom.GenerateNonce();
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return (nonce, ciphertext, tag);
    }

    /// <summary>
    /// Decrypts from separate nonce, ciphertext, and tag components.
    /// </summary>
    public byte[] DecryptSeparate(
        byte[] nonce, byte[] ciphertext, byte[] tag, byte[]? associatedData = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        byte[] plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

        return plaintext;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_key);
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~AesGcmEngine()
    {
        Dispose();
    }
}
