using System.Security.Cryptography;

namespace SecureFolder.Core.Crypto;

/// <summary>
/// Cryptographically secure random number generation.
/// All random values in the application MUST go through this class.
/// </summary>
public static class SecureRandom
{
    /// <summary>
    /// Generates a cryptographically secure random byte array.
    /// </summary>
    public static byte[] GenerateBytes(int length)
    {
        byte[] buffer = new byte[length];
        RandomNumberGenerator.Fill(buffer);
        return buffer;
    }

    /// <summary>
    /// Generates a cryptographically secure random salt for Argon2id.
    /// </summary>
    public static byte[] GenerateSalt() => GenerateBytes(16);

    /// <summary>
    /// Generates a cryptographically secure random nonce for AES-GCM (96-bit).
    /// </summary>
    public static byte[] GenerateNonce() => GenerateBytes(12);

    /// <summary>
    /// Generates a cryptographically secure random 256-bit key.
    /// </summary>
    public static byte[] GenerateKey() => GenerateBytes(32);
}
