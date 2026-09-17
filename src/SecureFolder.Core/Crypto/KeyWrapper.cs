using System.Security.Cryptography;

namespace SecureFolder.Core.Crypto;

/// <summary>
/// AES-256 Key Wrapping (RFC 3394).
/// Used to wrap/unwrap the Data Encryption Key (DEK) with the Key Encryption Key (KEK).
/// This allows password changes without re-encrypting all file data.
/// .NET does not ship an AES-KW implementation, so RFC 3394 is implemented here.
/// </summary>
public static class KeyWrapper
{
    private const int Rfc3394Block = 8;

    private static readonly byte[] DefaultIv =
    {
        0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6
    };

    /// <summary>
    /// Wraps (encrypts) a data encryption key using a key encryption key.
    /// </summary>
    /// <param name="dek">Data Encryption Key to wrap (32 bytes).</param>
    /// <param name="kek">Key Encryption Key derived from password (32 bytes).</param>
    /// <returns>Wrapped key (40 bytes = 32 + 8 AES key wrap overhead).</returns>
    public static byte[] WrapKey(byte[] dek, byte[] kek)
    {
        if (dek.Length != 32)
            throw new ArgumentException("DEK must be 32 bytes.", nameof(dek));
        if (kek.Length != 32)
            throw new ArgumentException("KEK must be 32 bytes.", nameof(kek));

        return AesKeyWrap(kek, dek);
    }

    /// <summary>
    /// Unwraps (decrypts) a data encryption key using a key encryption key.
    /// </summary>
    /// <param name="wrappedDek">Wrapped DEK (40 bytes).</param>
    /// <param name="kek">Key Encryption Key derived from password (32 bytes).</param>
    /// <returns>Unwrapped DEK (32 bytes).</returns>
    /// <exception cref="CryptographicException">Thrown if KEK is incorrect.</exception>
    public static byte[] UnwrapKey(byte[] wrappedDek, byte[] kek)
    {
        if (wrappedDek.Length != 40)
            throw new ArgumentException("Wrapped DEK must be 40 bytes.", nameof(wrappedDek));
        if (kek.Length != 32)
            throw new ArgumentException("KEK must be 32 bytes.", nameof(kek));

        return AesKeyUnwrap(kek, wrappedDek);
    }

    private static byte[] AesKeyWrap(byte[] kek, byte[] key)
    {
        int n = key.Length / Rfc3394Block;

        byte[] a = (byte[])DefaultIv.Clone();
        byte[] r = (byte[])key.Clone();

        using var aes = Aes.Create();
        aes.Key = kek;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor();

        byte[] block = new byte[16];

        for (int j = 0; j < 6; j++)
        {
            for (int i = 1; i <= n; i++)
            {
                Buffer.BlockCopy(a, 0, block, 0, Rfc3394Block);
                Buffer.BlockCopy(r, (i - 1) * Rfc3394Block, block, Rfc3394Block, Rfc3394Block);

                _ = encryptor.TransformBlock(block, 0, 16, block, 0);

                // A = MSB(64, B) ^ t ; R[i] = LSB(64, B)
                ulong t = (ulong)((n * j) + i);
                XorInPlace(block, t);
                Buffer.BlockCopy(block, 0, a, 0, Rfc3394Block);
                Buffer.BlockCopy(block, Rfc3394Block, r, (i - 1) * Rfc3394Block, Rfc3394Block);
            }
        }

        byte[] wrapped = new byte[(n + 1) * Rfc3394Block];
        Buffer.BlockCopy(a, 0, wrapped, 0, Rfc3394Block);
        for (int i = 1; i <= n; i++)
            Buffer.BlockCopy(r, (i - 1) * Rfc3394Block, wrapped, i * Rfc3394Block, Rfc3394Block);

        return wrapped;
    }

    private static byte[] AesKeyUnwrap(byte[] kek, byte[] wrapped)
    {
        int n = (wrapped.Length / Rfc3394Block) - 1;

        byte[] a = new byte[Rfc3394Block];
        byte[] r = new byte[n * Rfc3394Block];
        Buffer.BlockCopy(wrapped, 0, a, 0, Rfc3394Block);
        for (int i = 1; i <= n; i++)
            Buffer.BlockCopy(wrapped, i * Rfc3394Block, r, (i - 1) * Rfc3394Block, Rfc3394Block);

        using var aes = Aes.Create();
        aes.Key = kek;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var decryptor = aes.CreateDecryptor();

        byte[] block = new byte[16];

        for (int j = 5; j >= 0; j--)
        {
            for (int i = n; i >= 1; i--)
            {
                ulong t = (ulong)((n * j) + i);
                XorInPlace(a, t);

                Buffer.BlockCopy(a, 0, block, 0, Rfc3394Block);
                Buffer.BlockCopy(r, (i - 1) * Rfc3394Block, block, Rfc3394Block, Rfc3394Block);

                _ = decryptor.TransformBlock(block, 0, 16, block, 0);

                Buffer.BlockCopy(block, 0, a, 0, Rfc3394Block);
                Buffer.BlockCopy(block, Rfc3394Block, r, (i - 1) * Rfc3394Block, Rfc3394Block);
            }
        }

        if (!CryptographicOperations.FixedTimeEquals(a, DefaultIv))
            throw new CryptographicException("Invalid KEK or corrupted wrapped key.");

        return r;
    }

    private static void XorInPlace(byte[] dest, ulong value)
    {
        // XOR dest (8 bytes) with the 64-bit big-endian representation of value.
        for (int i = Rfc3394Block - 1; i >= 0; i--)
        {
            dest[i] ^= (byte)(value & 0xFF);
            value >>= 8;
        }
    }
}