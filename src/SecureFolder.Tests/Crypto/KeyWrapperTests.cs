using System.Security.Cryptography;
using FluentAssertions;
using SecureFolder.Core.Crypto;

namespace SecureFolder.Tests.Crypto;

public class KeyWrapperTests
{
    private static byte[] RandomKey(int size) => SecureRandom.GenerateBytes(size);

    [Fact]
    public void WrapKey_ThenUnwrap_RoundTrips()
    {
        byte[] dek = RandomKey(32);
        byte[] kek = RandomKey(32);

        byte[] wrapped = KeyWrapper.WrapKey(dek, kek);

        wrapped.Should().HaveCount(40);

        byte[] unwrapped = KeyWrapper.UnwrapKey(wrapped, kek);
        unwrapped.Should().Equal(dek);
    }

    [Fact]
    public void WrapKey_WithWrongKek_FailsUnwrap()
    {
        byte[] dek = RandomKey(32);
        byte[] kek = RandomKey(32);
        byte[] wrongKek = RandomKey(32);

        byte[] wrapped = KeyWrapper.WrapKey(dek, kek);

        var act = () => KeyWrapper.UnwrapKey(wrapped, wrongKek);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void WrapKey_WrappedOutputIsDeterministicForSameInput()
    {
        byte[] dek = RandomKey(32);
        byte[] kek = RandomKey(32);

        byte[] first = KeyWrapper.WrapKey(dek, kek);
        byte[] second = KeyWrapper.WrapKey(dek, kek);

        first.Should().Equal(second);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(48)]
    public void WrapKey_WithNon32ByteDek_Throws(int keySize)
    {
        byte[] dek = RandomKey(keySize);
        byte[] kek = RandomKey(32);

        var act = () => KeyWrapper.WrapKey(dek, kek);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WrapKey_WithInvalidDekSize_Throws()
    {
        byte[] key = RandomKey(16);
        byte[] kek = RandomKey(32);

        var act = () => KeyWrapper.WrapKey(key, kek);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UnwrapKey_WithInvalidSize_Throws()
    {
        byte[] wrapped = RandomKey(16);
        byte[] kek = RandomKey(32);

        var act = () => KeyWrapper.UnwrapKey(wrapped, kek);
        act.Should().Throw<ArgumentException>();
    }
}