using FluentAssertions;
using SecureFolder.Core.Models;
using SecureFolder.Core.Vault;

namespace SecureFolder.Tests.Vault;

public class VaultManagerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "SecureFolderTests", Guid.NewGuid().ToString("N"));

    public VaultManagerTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* Best effort */ }
    }

    private VaultManager CreateManager() =>
        new(Path.Combine(_tempDir, "appdata"));

    [Fact]
    public async Task CreateVault_CreatesFileAndAddsToList()
    {
        using var manager = CreateManager();
        string storage = Path.Combine(_tempDir, "vaults");

        var result = await manager.CreateVaultAsync("My Vault", storage, "Password123!");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.VaultFilePath.Should().EndWith(".sfv");
        File.Exists(result.Value.VaultFilePath).Should().BeTrue();
        manager.Vaults.Should().ContainSingle(v => v.Name == "My Vault");
    }

    [Fact]
    public async Task CreateVault_DuplicateLocation_Fails()
    {
        using var manager = CreateManager();
        string storage = Path.Combine(_tempDir, "vaults");

        var first = await manager.CreateVaultAsync("My Vault", storage, "Password123!");
        var second = await manager.CreateVaultAsync("My Vault", storage, "OtherPass123!");

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ChangePassword_OnClosedVault_Works()
    {
        using var manager = CreateManager();
        string storage = Path.Combine(_tempDir, "vaults");

        var created = await manager.CreateVaultAsync("My Vault", storage, "Password123!");
        created.IsSuccess.Should().BeTrue();

        var info = manager.Vaults.Single(v => v.Name == "My Vault");
        var result = await manager.ChangePasswordAsync(info.Id, "Password123!", "NewPassword456!");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_Fails()
    {
        using var manager = CreateManager();
        string storage = Path.Combine(_tempDir, "vaults");

        await manager.CreateVaultAsync("My Vault", storage, "Password123!");
        var info = manager.Vaults.Single();

        var result = await manager.ChangePasswordAsync(info.Id, "Nope!", "NewPassword456!");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveVault_RemovesFromList_ButKeepsFile()
    {
        using var manager = CreateManager();
        string storage = Path.Combine(_tempDir, "vaults");
        var created = await manager.CreateVaultAsync("My Vault", storage, "Password123!");

        var info = manager.Vaults.Single();
        string filePath = info.VaultFilePath;

        var result = manager.RemoveVault(info.Id);

        result.IsSuccess.Should().BeTrue();
        manager.Vaults.Should().BeEmpty();
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public async Task Vaults_ArePersistedAcrossManagerInstances()
    {
        string appData = Path.Combine(_tempDir, "appdata");
        string storage = Path.Combine(_tempDir, "vaults");

        using (var manager = new VaultManager(appData))
        {
            await manager.CreateVaultAsync("Persisted Vault", storage, "Password123!");
        }

        using (var manager2 = new VaultManager(appData))
        {
            manager2.Vaults.Should().ContainSingle(v => v.Name == "Persisted Vault");
        }
    }
}