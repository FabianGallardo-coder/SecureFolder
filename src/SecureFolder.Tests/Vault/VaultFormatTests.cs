using System.Text;
using FluentAssertions;
using SecureFolder.Core.Vault;

namespace SecureFolder.Tests.Vault;

public class VaultFormatTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "SecureFolderTests", Guid.NewGuid().ToString("N"));

    public VaultFormatTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* Best effort */ }
    }

    private string VaultPath(string name = "test.sfv") => Path.Combine(_tempDir, name);

    [Fact]
    public async Task Create_ThenOpen_WithCorrectPassword_ReturnsDekAndIndex()
    {
        string path = VaultPath();

        await VaultFormat.CreateVaultAsync(path, "Password123!", "My Vault");

        var result = await VaultFormat.OpenVaultAsync(path, "Password123!");

        result.Dek.Should().HaveCount(32);
        result.DataStartOffset.Should().BeGreaterThan(0);
        result.IndexJson.Should().Be("{}");
    }

    [Fact]
    public async Task Open_WithWrongPassword_Throws()
    {
        string path = VaultPath();
        await VaultFormat.CreateVaultAsync(path, "CorrectPassword!", "My Vault");

        var act = async () => await VaultFormat.OpenVaultAsync(path, "WrongPassword!");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Open_CorruptedOrNonVaultFile_Throws()
    {
        string path = VaultPath();
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("not a vault"));

        var act = async () => await VaultFormat.OpenVaultAsync(path, "Password123!");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ChangePassword_ThenOpen_WithNewPassword_Works()
    {
        string path = VaultPath();
        await VaultFormat.CreateVaultAsync(path, "OriginalPassword!", "My Vault");

        await VaultFormat.ChangePasswordAsync(path, "OriginalPassword!", "NewPassword123!");

        var result = await VaultFormat.OpenVaultAsync(path, "NewPassword123!");
        result.Dek.Should().HaveCount(32);

        var act = async () => await VaultFormat.OpenVaultAsync(path, "OriginalPassword!");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrent_Throws()
    {
        string path = VaultPath();
        await VaultFormat.CreateVaultAsync(path, "OriginalPassword!", "My Vault");

        var act = async () => await VaultFormat.ChangePasswordAsync(
            path, "WrongCurrent!", "NewPassword123!");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Open_WithModifiedHeader_Tamper_IsDetected()
    {
        string path = VaultPath();
        await VaultFormat.CreateVaultAsync(path, "Password123!", "My Vault");

        byte[] fileBytes = await File.ReadAllBytesAsync(path);
        fileBytes[VaultFormat.MagicSize + VaultFormat.VersionSize] ^= 0x01; // flip salt byte
        await File.WriteAllBytesAsync(path, fileBytes);

        var act = async () => await VaultFormat.OpenVaultAsync(path, "Password123!");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Create_TwoVaults_HaveDifferentHeaders()
    {
        string path1 = VaultPath("a.sfv");
        string path2 = VaultPath("b.sfv");

        await VaultFormat.CreateVaultAsync(path1, "Password123!", "A");
        await VaultFormat.CreateVaultAsync(path2, "Password123!", "B");

        byte[] h1 = (await File.ReadAllBytesAsync(path1)).AsSpan(0, VaultFormat.HeaderSize).ToArray();
        byte[] h2 = (await File.ReadAllBytesAsync(path2)).AsSpan(0, VaultFormat.HeaderSize).ToArray();

        h1.Should().NotEqual(h2, "each vault must use a fresh salt and wrapped DEK");
    }
}