using System.Security.Cryptography;
using System.Text;

namespace SecureFolder.Core.Vault;

/// <summary>
/// HMAC-SHA256 helper for vault header integrity verification.
/// </summary>
public static class HmacHelper
{
    /// <summary>
    /// Computes HMAC-SHA256 over the given data using the provided key.
    /// </summary>
    public static byte[] ComputeHmac(byte[] key, byte[] data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(data);
    }

    /// <summary>
    /// Verifies an HMAC-SHA256 using constant-time comparison.
    /// </summary>
    public static bool VerifyHmac(byte[] key, byte[] data, byte[] expectedHmac)
    {
        byte[] computed = ComputeHmac(key, data);
        return CryptographicOperations.FixedTimeEquals(computed, expectedHmac);
    }
}
