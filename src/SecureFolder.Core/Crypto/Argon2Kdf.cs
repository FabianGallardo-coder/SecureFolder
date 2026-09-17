using System.Security.Cryptography;
using Konscious.Security.Cryptography;

namespace SecureFolder.Core.Crypto;

/// <summary>
/// Argon2id key derivation function.
/// Derives a 256-bit encryption key from a password and salt.
/// </summary>
public static class Argon2Kdf
{
    /// <summary>
    /// Default KDF parameters optimized for interactive use on modern hardware.
    /// Memory: 64 MB, Iterations: 3, Parallelism: 4 threads.
    /// Estimated time: 200-500ms on modern CPU.
    /// </summary>
    public const int DefaultMemorySize = 64 * 1024; // 64 MB in KB
    public const int DefaultIterations = 3;
    public const int DefaultParallelism = 4;
    public const int KeyLength = 32; // 256 bits

    /// <summary>
    /// Derives a 256-bit key from a password using Argon2id.
    /// </summary>
    /// <param name="password">The user's password (UTF-8 encoded).</param>
    /// <param name="salt">Cryptographic salt (16 bytes recommended).</param>
    /// <param name="memorySize">Memory cost in KB (default: 65536 = 64 MB).</param>
    /// <param name="iterations">Time cost (default: 3).</param>
    /// <param name="parallelism">Parallelism degree (default: 4).</param>
    /// <returns>Derived 32-byte key.</returns>
    public static byte[] DeriveKey(
        byte[] password,
        byte[] salt,
        int memorySize = DefaultMemorySize,
        int iterations = DefaultIterations,
        int parallelism = DefaultParallelism)
    {
        using var argon2 = new Argon2id(password)
        {
            Salt = salt,
            DegreeOfParallelism = parallelism,
            MemorySize = memorySize,
            Iterations = iterations
        };

        return argon2.GetBytes(KeyLength);
    }

    /// <summary>
    /// Derives a key from a password string using Argon2id.
    /// </summary>
    public static byte[] DeriveKey(
        string password,
        byte[] salt,
        int memorySize = DefaultMemorySize,
        int iterations = DefaultIterations,
        int parallelism = DefaultParallelism)
    {
        byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
        try
        {
            return DeriveKey(passwordBytes, salt, memorySize, iterations, parallelism);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    /// <summary>
    /// KDF parameters used for key derivation.
    /// Stored in the vault header for verification on unlock.
    /// </summary>
    public record KdfParameters(
        int MemorySize,
        int Iterations,
        int Parallelism)
    {
        public static KdfParameters Default => new(DefaultMemorySize, DefaultIterations, DefaultParallelism);

        public byte[] Serialize()
        {
            byte[] data = new byte[12];
            BitConverter.TryWriteBytes(data.AsSpan(0, 4), MemorySize);
            BitConverter.TryWriteBytes(data.AsSpan(4, 4), Iterations);
            BitConverter.TryWriteBytes(data.AsSpan(8, 4), Parallelism);
            return data;
        }

        public static KdfParameters Deserialize(byte[] data)
        {
            if (data.Length < 12)
                throw new ArgumentException("KDF parameters data too short.");

            int memorySize = BitConverter.ToInt32(data, 0);
            int iterations = BitConverter.ToInt32(data, 4);
            int parallelism = BitConverter.ToInt32(data, 8);
            return new KdfParameters(memorySize, iterations, parallelism);
        }
    }
}
