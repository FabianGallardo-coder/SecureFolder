using FluentAssertions;
using SecureFolder.Core.Vault;
using SecureFolder.Core.Models;
using System.IO;

namespace SecureFolder.Tests.Vault;

public class VaultLifecycleTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "SecureFolderTests_Lifecycle", Guid.NewGuid().ToString("N"));

    public VaultLifecycleTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    private VaultManager CreateManager() =>
        new(Path.Combine(_tempDir, "appdata"));

    [Fact]
    public async Task RemoveVault_DeletePhysicalFile_RemovesFileFromDisk()
    {
        // Arrange
        using var manager = CreateManager();
        string storage = Path.Combine(_tempDir, "vaults");
        Directory.CreateDirectory(storage);

        var created = await manager.CreateVaultAsync("DeleteMe", storage, "Password123!");
        string filePath = created.Value!.VaultFilePath;
        File.Exists(filePath).Should().BeTrue();

        // Act
        var result = manager.RemoveVault(created.Value.Id, deleteFile: true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        File.Exists(filePath).Should().BeFalse("porque se solicitó el borrado físico");
        manager.Vaults.Should().BeEmpty();
    }

    [Fact]
    public async Task RenameVault_UpdatesPhysicalFileAndConfig()
    {
        // Arrange
        using var manager = CreateManager();
        string storage = Path.Combine(_tempDir, "vaults");
        Directory.CreateDirectory(storage);

        var created = await manager.CreateVaultAsync("OldName", storage, "Password123!");
        var info = created.Value!;
        string oldPath = info.VaultFilePath;
        File.Exists(oldPath).Should().BeTrue();

        // Act
        var result = await manager.RenameVaultAsync(info.Id, "NewName");

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Check physical file
        string expectedNewPath = oldPath.Replace("OldName", "NewName");
        File.Exists(oldPath).Should().BeFalse("el archivo antiguo debe desaparecer");
        File.Exists(expectedNewPath).Should().BeTrue("el archivo nuevo debe existir");

        // Check config
        var updatedInfo = manager.Vaults.Single();
        updatedInfo.Name.Should().Be("NewName");
        updatedInfo.VaultFilePath.Should().Be(expectedNewPath);
    }
}
