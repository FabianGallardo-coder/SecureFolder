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

    [Fact]
    public async Task ImportVault_ExistingFile_AddsToListPersistsAndOpens()
    {
        // Simula un vault "de otra PC" en un USB: existe el archivo, pero no está
        // registrado en vaults.json de esta máquina.
        string appData = Path.Combine(_tempDir, "appdata");
        string usb = Path.Combine(_tempDir, "usb");
        Directory.CreateDirectory(usb);
        string vaultPath = Path.Combine(usb, "Viaje.sfv");
        await VaultFormat.CreateVaultAsync(vaultPath, "Password123!", "Viaje");

        using (var manager = new VaultManager(appData))
        {
            var result = await manager.ImportVaultAsync(vaultPath);

            result.IsSuccess.Should().BeTrue();
            result.Value!.Name.Should().Be("Viaje");
            manager.Vaults.Should().ContainSingle(v => v.VaultFilePath == Path.GetFullPath(vaultPath));

            // El registro persiste en vaults.json de "esta máquina"
            manager.Dispose();
            using var reloaded = new VaultManager(appData);
            reloaded.Vaults.Should().ContainSingle(v => v.Name == "Viaje");
        }

        // El vault importado se abre con su contraseña (lectura completa sin montar)
        var open = await VaultFormat.OpenVaultAsync(vaultPath, "Password123!");
        open.IndexJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ImportVault_Duplicate_Fails()
    {
        using var manager = CreateManager();
        string vaultPath = Path.Combine(_tempDir, "duplicado.sfv");
        await VaultFormat.CreateVaultAsync(vaultPath, "Password123!", "Duplicado");

        var first = await manager.ImportVaultAsync(vaultPath);
        var second = await manager.ImportVaultAsync(vaultPath);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeFalse();
        manager.Vaults.Should().HaveCount(1);
    }

    [Fact]
    public async Task ImportVault_NotARealVault_Fails()
    {
        using var manager = CreateManager();
        string fake = Path.Combine(_tempDir, "fake.sfv");
        await File.WriteAllTextAsync(fake, "esto no es un vault .sfv");

        var result = await manager.ImportVaultAsync(fake);

        result.IsSuccess.Should().BeFalse();
        manager.Vaults.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportVault_WrongExtension_Fails()
    {
        using var manager = CreateManager();
        string file = Path.Combine(_tempDir, "nota.txt");
        await File.WriteAllTextAsync(file, "hola");

        var result = await manager.ImportVaultAsync(file);

        result.IsSuccess.Should().BeFalse();
        manager.Vaults.Should().BeEmpty();
    }
}