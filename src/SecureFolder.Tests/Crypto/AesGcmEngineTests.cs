using System.Text;
using FluentAssertions;
using SecureFolder.Core.Crypto;

namespace SecureFolder.Tests.Crypto;

public class AesGcmEngineTests
{
    [Fact]
    public void Encrypt_ThenDecrypt_RoundTrips()
    {
        byte[] key = SecureRandom.GenerateKey();
        using var engine = new AesGcmEngine(key);

        byte[] plaintext = Encoding.UTF8.GetBytes("SecureFolder test data 1234567890");

        byte[] encrypted = engine.Encrypt(plaintext);
        byte[] decrypted = engine.Decrypt(encrypted);

        decrypted.Should().Equal(plaintext);
    }

    [Fact]
    public void Encrypt_ProducesNonceCiphertextTagLayout()
    {
        byte[] key = SecureRandom.GenerateKey();
        using var engine = new AesGcmEngine(key);

        byte[] plaintext = Encoding.UTF8.GetBytes("hello");
        byte[] encrypted = engine.Encrypt(plaintext);

        encrypted.Should().HaveCount(
            AesGcmEngine.NonceSize + plaintext.Length + AesGcmEngine.TagSize);
    }

    [Fact]
    public void Encrypt_GeneratesUniqueNonces()
    {
        byte[] key = SecureRandom.GenerateKey();
        using var engine = new AesGcmEngine(key);

        byte[] plaintext = Encoding.UTF8.GetBytes("same input");

        byte[] first = engine.Encrypt(plaintext);
        byte[] second = engine.Encrypt(plaintext);

        first.Should().NotEqual(second);
    }

    [Fact]
    public void Decrypt_WithTamperedCiphertext_Throws()
    {
        byte[] key = SecureRandom.GenerateKey();
        using var engine = new AesGcmEngine(key);

        byte[] plaintext = Encoding.UTF8.GetBytes("authenticated payload");
        byte[] encrypted = engine.Encrypt(plaintext);
        encrypted[encrypted.Length - 1] ^= 0x01;

        var act = () => engine.Decrypt(encrypted);
        act.Should().Throw<System.Security.Cryptography.CryptographicException>();
    }

    [Fact]
    public void Decrypt_WithWrongKey_Throws()
    {
        byte[] key = SecureRandom.GenerateKey();
        byte[] wrongKey = SecureRandom.GenerateKey();
        using var engine = new AesGcmEngine(key);
        using var wrongEngine = new AesGcmEngine(wrongKey);

        byte[] encrypted = engine.Encrypt(Encoding.UTF8.GetBytes("secret"));

        var act = () => wrongEngine.Decrypt(encrypted);
        act.Should().Throw<System.Security.Cryptography.CryptographicException>();
    }

    [Fact]
    public void EncryptSeparate_ThenDecryptSeparate_RoundTrips()
    {
        byte[] key = SecureRandom.GenerateKey();
        using var engine = new AesGcmEngine(key);

        byte[] plaintext = Encoding.UTF8.GetBytes("separate components");
        var (nonce, ciphertext, tag) = engine.EncryptSeparate(plaintext);

        nonce.Should().HaveCount(AesGcmEngine.NonceSize);
        tag.Should().HaveCount(AesGcmEngine.TagSize);

        byte[] decrypted = engine.DecryptSeparate(nonce, ciphertext, tag);
        decrypted.Should().Equal(plaintext);
    }

    [Fact]
    public void Encrypt_WithAssociatedData_RequiresSameAdOnDecrypt()
    {
        byte[] key = SecureRandom.GenerateKey();
        using var engine = new AesGcmEngine(key);

        byte[] aad = Encoding.UTF8.GetBytes("header-context");
        byte[] encrypted = engine.Encrypt(Encoding.UTF8.GetBytes("data"), aad);

        byte[] ok = engine.Decrypt(encrypted, aad);
        ok.Should().Equal(Encoding.UTF8.GetBytes("data"));

        var act = () => engine.Decrypt(encrypted, Encoding.UTF8.GetBytes("different"));
        act.Should().Throw<System.Security.Cryptography.CryptographicException>();
    }

    [Fact]
    public void Constructor_WithNon32ByteKey_Throws()
    {
        var act = () => new AesGcmEngine(new byte[16]);
        act.Should().Throw<ArgumentException>();
    }
}