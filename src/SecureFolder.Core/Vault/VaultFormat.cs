using System.Security.Cryptography;
using System.Text;
using SecureFolder.Core.Crypto;
using SecureFolder.Core.Models;

namespace SecureFolder.Core.Vault;

/// <summary>
/// Handles reading and writing the .sfv vault file format.
///
/// Format:
/// ┌──────────────────────────────────┐
/// │           VAULT HEADER           │
/// │  Magic: "SFVL" (4B)             │
/// │  Version: uint16 (2B)           │
/// │  Salt: 16B (Argon2id)           │
/// │  KDF Params: 12B                │
/// │  Wrapped DEK: 40B (AES-256-KW)  │
/// │  Verification Blob: 32B          │
/// │  HMAC-SHA256: 32B               │
/// ├──────────────────────────────────┤
/// │          FILE INDEX              │
/// │  (encrypted with DEK via GCM)   │
/// ├──────────────────────────────────┤
/// │          DATA BLOCKS             │
/// │  Per-file encrypted chunks       │
/// └──────────────────────────────────┘
/// </summary>
public static class VaultFormat
{
    public const string Magic = "SFVL";
    public const ushort CurrentVersion = 2;


    // Header field sizes
    public const int MagicSize = 4;
    public const int VersionSize = 2;
    public const int SaltSize = 16;
    public const int KdfParamsSize = 12;
    public const int WrappedDekSize = 40;
    public const int VerificationBlobSize = 32;
    public const int HeaderHmacSize = 32;

    public const int HeaderSize = MagicSize + VersionSize + SaltSize + KdfParamsSize
                                + WrappedDekSize + VerificationBlobSize + HeaderHmacSize;
    // = 138 bytes

    public const int ChunkSize = 64 * 1024; // 64 KB data chunks
    public const int ChunkOverhead = AesGcmEngine.NonceSize + 4 + AesGcmEngine.TagSize; // 32 bytes

    // Verification payload: must be exactly VerificationBlobSize - nonce - tag = 4 bytes.
    public const string VerificationPlaintext = "SFLV";

    /// <summary>
    /// Creates a new vault file with the given password.
    /// </summary>
    public static async Task CreateVaultAsync(
        string vaultFilePath,
        string password,
        string vaultName,
        CancellationToken ct = default)
    {
        byte[] salt = SecureRandom.GenerateSalt();
        var kdfParams = Argon2Kdf.KdfParameters.Default;

        // Derive KEK from password
        byte[] kek = Argon2Kdf.DeriveKey(password, salt,
            kdfParams.MemorySize, kdfParams.Iterations, kdfParams.Parallelism);

        // Generate random DEK
        byte[] dek = SecureRandom.GenerateKey();

        // Wrap DEK with KEK
        byte[] wrappedDek = KeyWrapper.WrapKey(dek, kek);

        // Create verification blob (must fit exactly in VerificationBlobSize: nonce + plaintext + tag)
        byte[] verificationPlaintext = Encoding.UTF8.GetBytes(VerificationPlaintext);
        byte[] verificationNonce = SecureRandom.GenerateNonce();
        byte[] verificationCiphertext = new byte[verificationPlaintext.Length];
        byte[] verificationTag = new byte[AesGcmEngine.TagSize];

        using (var aes = new AesGcm(dek, AesGcmEngine.TagSize))
        {
            aes.Encrypt(verificationNonce, verificationPlaintext, verificationCiphertext, verificationTag);
        }

        byte[] verificationBlob = new byte[verificationNonce.Length + verificationCiphertext.Length + verificationTag.Length];
        verificationNonce.CopyTo(verificationBlob, 0);
        verificationCiphertext.CopyTo(verificationBlob, AesGcmEngine.NonceSize);
        verificationTag.CopyTo(verificationBlob, AesGcmEngine.NonceSize + verificationPlaintext.Length);

        // Build header
        byte[] header = new byte[HeaderSize];
        int offset = 0;

        Encoding.ASCII.GetBytes(Magic).CopyTo(header, offset); offset += MagicSize;
        BitConverter.TryWriteBytes(header.AsSpan(offset), CurrentVersion); offset += VersionSize;
        salt.CopyTo(header, offset); offset += SaltSize;
        kdfParams.Serialize().CopyTo(header, offset); offset += KdfParamsSize;
        wrappedDek.CopyTo(header, offset); offset += WrappedDekSize;
        verificationBlob.CopyTo(header, offset); offset += VerificationBlobSize;

        // Compute HMAC over header (excluding HMAC field itself)
        byte[] headerWithoutHmac = header.AsSpan(0, HeaderSize - HeaderHmacSize).ToArray();
        byte[] headerHmac = HmacHelper.ComputeHmac(kek, headerWithoutHmac);
        headerHmac.CopyTo(header, HeaderSize - HeaderHmacSize);

// Empty file index (encrypted)
byte[] emptyIndex = Encoding.UTF8.GetBytes("{}");
        byte[] encryptedIndex = EncryptIndex(emptyIndex, dek);

        // Write vault file
        await using var fs = new FileStream(vaultFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
        await fs.WriteAsync(header, ct);
        await fs.WriteAsync(BitConverter.GetBytes(encryptedIndex.Length), ct); // index length
        await fs.WriteAsync(encryptedIndex, ct);

        // Cleanup sensitive data
        CryptographicOperations.ZeroMemory(kek);
        CryptographicOperations.ZeroMemory(dek);
    }

    /// <summary>
    /// Opens and verifies a vault file, returning the DEK and metadata.
    /// </summary>
    public static async Task<VaultOpenResult> OpenVaultAsync(
        string vaultFilePath,
        string password,
        CancellationToken ct = default)
    {
        await using var fs = new FileStream(vaultFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);

        if (fs.Length < HeaderSize)
            throw new InvalidOperationException("El archivo de la carpeta segura es demasiado pequeño o está dañado.");

        // Read header
        byte[] header = new byte[HeaderSize];
        await fs.ReadExactlyAsync(header, ct);

        // Verify magic
        string magic = Encoding.ASCII.GetString(header, 0, MagicSize);
        if (magic != Magic)
            throw new InvalidOperationException("Formato de archivo de carpeta segura no válido.");

        // Read version
        ushort version = BitConverter.ToUInt16(header, MagicSize);
        if (version > CurrentVersion)
            throw new InvalidOperationException($"Versión de carpeta segura no compatible: {version}");

        // Read salt
        byte[] salt = header.AsSpan(MagicSize + VersionSize, SaltSize).ToArray();

        // Read KDF params
        byte[] kdfParamsData = header.AsSpan(MagicSize + VersionSize + SaltSize, KdfParamsSize).ToArray();
        var kdfParams = Argon2Kdf.KdfParameters.Deserialize(kdfParamsData);

        // Read wrapped DEK
        byte[] wrappedDek = header.AsSpan(MagicSize + VersionSize + SaltSize + KdfParamsSize, WrappedDekSize).ToArray();

        // Read verification blob
        int verificationOffset = MagicSize + VersionSize + SaltSize + KdfParamsSize + WrappedDekSize;
        byte[] verificationBlob = header.AsSpan(verificationOffset, VerificationBlobSize).ToArray();

        // Read header HMAC
        byte[] storedHmac = header.AsSpan(HeaderSize - HeaderHmacSize, HeaderHmacSize).ToArray();

        // Derive KEK from password
        byte[] kek = Argon2Kdf.DeriveKey(password, salt,
            kdfParams.MemorySize, kdfParams.Iterations, kdfParams.Parallelism);

        // Verify HMAC
        byte[] headerWithoutHmac = header.AsSpan(0, HeaderSize - HeaderHmacSize).ToArray();
        byte[] computedHmac = HmacHelper.ComputeHmac(kek, headerWithoutHmac);
        if (!CryptographicOperations.FixedTimeEquals(computedHmac, storedHmac))
        {
            CryptographicOperations.ZeroMemory(kek);
            throw new InvalidOperationException("Contraseña incorrecta.");
        }

        // Unwrap DEK
        byte[] dek;
        try
        {
            dek = KeyWrapper.UnwrapKey(wrappedDek, kek);
        }
        catch (Exception)
        {
            CryptographicOperations.ZeroMemory(kek);
            throw new InvalidOperationException("Contraseña incorrecta.");
        }

        // Verify DEK with verification blob
        try
        {
            byte[] vNonce = verificationBlob.AsSpan(0, AesGcmEngine.NonceSize).ToArray();
            byte[] vCiphertext = verificationBlob.AsSpan(AesGcmEngine.NonceSize, VerificationBlobSize - AesGcmEngine.NonceSize - AesGcmEngine.TagSize).ToArray();
            byte[] vTag = verificationBlob.AsSpan(AesGcmEngine.NonceSize + vCiphertext.Length, AesGcmEngine.TagSize).ToArray();

            using var aes = new AesGcm(dek, AesGcmEngine.TagSize);
            byte[] vPlaintext = new byte[vCiphertext.Length];
            aes.Decrypt(vNonce, vCiphertext, vTag, vPlaintext);

            string verificationText = Encoding.UTF8.GetString(vPlaintext);
            if (verificationText != VerificationPlaintext)
                throw new InvalidOperationException("La verificación falló.");
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(kek);
            CryptographicOperations.ZeroMemory(dek);
            throw new InvalidOperationException("Contraseña incorrecta.");
        }

        // Read encrypted file index
        byte[] indexLengthBytes = new byte[4];
        await fs.ReadExactlyAsync(indexLengthBytes, ct);
        int indexLength = BitConverter.ToInt32(indexLengthBytes);

        byte[] encryptedIndex = new byte[indexLength];
        await fs.ReadExactlyAsync(encryptedIndex, ct);

        byte[] indexBytes = DecryptIndex(encryptedIndex, dek);
        string indexJson = Encoding.UTF8.GetString(indexBytes);

        return new VaultOpenResult
        {
            Dek = dek,
            Salt = salt,
            KdfParameters = kdfParams,
            VaultName = "",
            IndexJson = indexJson,
            DataStartOffset = HeaderSize + 4 + indexLength
        };
    }

    /// <summary>
    /// Changes the password on a vault file by re-wrapping the DEK.
    /// </summary>
    public static async Task ChangePasswordAsync(
        string vaultFilePath,
        string currentPassword,
        string newPassword,
        CancellationToken ct = default)
    {
        // Open with current password
        var openResult = await OpenVaultAsync(vaultFilePath, currentPassword, ct);

        // Derive new KEK from new password
        byte[] newSalt = SecureRandom.GenerateSalt();
        var kdfParams = Argon2Kdf.KdfParameters.Default;
        byte[] newKek = Argon2Kdf.DeriveKey(newPassword, newSalt,
            kdfParams.MemorySize, kdfParams.Iterations, kdfParams.Parallelism);

        // Re-wrap DEK with new KEK
        byte[] newWrappedDek = KeyWrapper.WrapKey(openResult.Dek, newKek);

        // Rebuild header
        byte[] newHeader = new byte[HeaderSize];
        int offset = 0;

        Encoding.ASCII.GetBytes(Magic).CopyTo(newHeader, offset); offset += MagicSize;
        BitConverter.TryWriteBytes(newHeader.AsSpan(offset), CurrentVersion); offset += VersionSize;
        newSalt.CopyTo(newHeader, offset); offset += SaltSize;
        kdfParams.Serialize().CopyTo(newHeader, offset); offset += KdfParamsSize;
        newWrappedDek.CopyTo(newHeader, offset); offset += WrappedDekSize;

        // Recreate verification blob
        byte[] verificationPlaintext = Encoding.UTF8.GetBytes(VerificationPlaintext);
        using var aesVerify = new AesGcm(openResult.Dek, AesGcmEngine.TagSize);
        byte[] vNonce = SecureRandom.GenerateNonce();
        byte[] vCiphertext = new byte[verificationPlaintext.Length];
        byte[] vTag = new byte[AesGcmEngine.TagSize];
        aesVerify.Encrypt(vNonce, verificationPlaintext, vCiphertext, vTag);

        byte[] verificationBlob = new byte[AesGcmEngine.NonceSize + vCiphertext.Length + AesGcmEngine.TagSize];
        vNonce.CopyTo(verificationBlob, 0);
        vCiphertext.CopyTo(verificationBlob, AesGcmEngine.NonceSize);
        vTag.CopyTo(verificationBlob, AesGcmEngine.NonceSize + vCiphertext.Length);
        verificationBlob.CopyTo(newHeader, offset);

        // Compute HMAC
        byte[] headerWithoutHmac = newHeader.AsSpan(0, HeaderSize - HeaderHmacSize).ToArray();
        byte[] headerHmac = HmacHelper.ComputeHmac(newKek, headerWithoutHmac);
        headerHmac.CopyTo(newHeader, HeaderSize - HeaderHmacSize);

        // Read existing data (skip header + index)
        byte[] allData = await File.ReadAllBytesAsync(vaultFilePath, ct);
        byte[] remainingData = allData.AsSpan((int)openResult.DataStartOffset).ToArray();

        // Rewrite file with new header + same data
        await using var fs = new FileStream(vaultFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
        await fs.WriteAsync(newHeader, ct);

        // Read and re-encrypt file index with new DEK (same DEK, so index doesn't change)
        int indexLength = BitConverter.ToInt32(allData, HeaderSize);
        await fs.WriteAsync(BitConverter.GetBytes(indexLength), ct);
        await fs.WriteAsync(allData.AsSpan(HeaderSize + 4, indexLength).ToArray(), ct);

        // Copy remaining data blocks (remainingData starts at DataStartOffset)
        await fs.WriteAsync(remainingData, ct);

        // Cleanup
        CryptographicOperations.ZeroMemory(openResult.Dek);
        CryptographicOperations.ZeroMemory(newKek);
        CryptographicOperations.ZeroMemory(newWrappedDek);
    }

    private static byte[] EncryptIndex(byte[] indexData, byte[] dek)
    {
        using var engine = new AesGcmEngine(dek);
        return engine.Encrypt(indexData);
    }

    private static byte[] DecryptIndex(byte[] encryptedIndex, byte[] dek)
    {
        using var engine = new AesGcmEngine(dek);
        return engine.Decrypt(encryptedIndex);
    }
}

/// <summary>
/// Result of opening a vault file.
/// </summary>
public sealed class VaultOpenResult
{
    public required byte[] Dek { get; init; }
    public required byte[] Salt { get; init; }
    public required Argon2Kdf.KdfParameters KdfParameters { get; init; }
    public required string VaultName { get; init; }
    public required string IndexJson { get; init; }
    public long DataStartOffset { get; init; }
}
