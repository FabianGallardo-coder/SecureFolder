using FluentAssertions;
using SecureFolder.Core.Filesystem;
using SecureFolder.Core.Vault;
using SecureFolder.Core.Models;
using System.IO;

namespace SecureFolder.Tests.Filesystem;

public class ZeroByteFileTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "SecureFolderTests_ZeroByte", Guid.NewGuid().ToString("N"));

    public ZeroByteFileTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ZeroByteFile_ShouldPersistAfterUnmount()
    {
        // Arrange
        string vaultPath = Path.Combine(_tempDir, "test_vault.sfv");
        string password = "Password123!";

        // 1. Create Vault
        var vaultManager = new VaultManager(Path.Combine(_tempDir, "appdata"));
        var createResult = await vaultManager.CreateVaultAsync("Test Vault", _tempDir, password);
        var vaultInfo = createResult.Value!;

        // Simulate the mounting process
        var mountedVault = new MountedVault
        {
            Info = vaultInfo,
            Engine = new SecureFolder.Core.Crypto.AesGcmEngine(new byte[32]), // Mock engine
            FileIndex = new Dictionary<string, FileEntry>(),
            MountPoint = "Z:"
        };

        // We need a mock IMountProvider to avoid actual WinFsp calls in unit tests
        var mockProvider = new MockMountProvider();
        var fs = new SecureFolderFileSystem(mountedVault, 0, mockProvider);

        // Create a 0-byte file
        string filePath = "\\empty.txt";

        fs.Create(filePath, 0, 0, 0, null!, 0, out _, out _, out _, out _);

        // Trigger the bug
        fs.Unmount(); // This calls FlushDirtyFiles()

        // Now we check if it's still in the index
        bool existsInIndex = mountedVault.FileIndex.ContainsKey("empty.txt");

        existsInIndex.Should().BeTrue("porque un archivo de 0 bytes debe persistir en el índice");
    }

    private class MockMountProvider : IMountProvider
    {
        public Result<string> Mount(char driveLetter, SecureFolderFileSystem fs) => Result<string>.Ok("Z:");
        public Result Unmount() => Result.Ok();
        public int LastError => 0;
    }
}
