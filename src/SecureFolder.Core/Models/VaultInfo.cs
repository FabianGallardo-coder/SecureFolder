using System.ComponentModel;

namespace SecureFolder.Core.Models;

/// <summary>
/// Represents a vault's metadata and configuration.
/// </summary>
public sealed class VaultInfo : INotifyPropertyChanged
{
    private bool _isUnlocked;
    private char? _driveLetter;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Unique identifier for this vault.</summary>
    public required string Id { get; init; }

    /// <summary>User-friendly name.</summary>
    public required string Name { get; set; }

    /// <summary>Full path to the .sfv vault file on disk.</summary>
    public required string VaultFilePath { get; init; }

    /// <summary>Whether the vault is currently unlocked (mounted).</summary>
    public bool IsUnlocked
    {
        get => _isUnlocked;
        set
        {
            if (_isUnlocked == value) return;
            _isUnlocked = value;
            Raise(nameof(IsUnlocked));
            Raise(nameof(StatusText));
            Raise(nameof(IconEmoji));
        }
    }

    /// <summary>Drive letter assigned when unlocked (e.g., 'X').</summary>
    public char? DriveLetter
    {
        get => _driveLetter;
        set
        {
            if (_driveLetter == value) return;
            _driveLetter = value;
            Raise(nameof(DriveLetter));
            Raise(nameof(StatusText));
        }
    }

    /// <summary>Timestamp of last unlock.</summary>
    public DateTimeOffset? LastUnlockedAt { get; set; }

    /// <summary>Timestamp of last lock.</summary>
    public DateTimeOffset? LastLockedAt { get; set; }

    /// <summary>Auto-lock timeout in minutes. 0 = disabled.</summary>
    public int AutoLockTimeoutMinutes { get; set; }

    /// <summary>Whether to auto-lock on Windows shutdown/logoff.</summary>
    public bool AutoLockOnShutdown { get; set; } = true;

    public string StatusText => IsUnlocked
        ? $"Desbloqueada ({DriveLetter}:\\)"
        : "Bloqueada";

    public string IconEmoji => IsUnlocked ? "\U0001F513" : "\U0001F512";
}

/// <summary>
/// Represents an encrypted file entry in the vault's file index.
/// </summary>
public sealed class FileEntry
{
    /// <summary>Original filename (stored encrypted).</summary>
    public required string OriginalName { get; init; }

    /// <summary>Relative path within the vault (stored encrypted).</summary>
    public required string RelativePath { get; init; }

    /// <summary>Offset of first data block in the vault file.</summary>
    public long DataOffset { get; init; }

    /// <summary>Total size of encrypted data blocks for this file.</summary>
    public long EncryptedSize { get; set; }

    /// <summary>Original uncompressed size of the file.</summary>
    public long OriginalSize { get; set; }

    /// <summary>SHA-256 hash of original file for integrity verification.</summary>
    public required byte[] Hash { get; init; }

    /// <summary>File creation time (UTC).</summary>
    public DateTimeOffset CreationTime { get; init; }

    /// <summary>Last modification time (UTC).</summary>
    public DateTimeOffset LastWriteTime { get; init; }

    /// <summary>Nonce used for encrypting this file's data blocks.</summary>
    public required byte[] Nonce { get; init; }
}

/// <summary>
/// Represents the current state of a mounted vault.
/// </summary>
public sealed class MountedVault : IDisposable
{
    private bool _disposed;

    public required VaultInfo Info { get; init; }
    public required Crypto.AesGcmEngine Engine { get; init; }
    public required Dictionary<string, FileEntry> FileIndex { get; init; }
    public required string MountPoint { get; init; }
    public Filesystem.SecureFolderFileSystem? FileSystem { get; set; }

    /// <summary>
    /// Unmounts the virtual filesystem and persists all dirty data to the vault file.
    /// Must be called before disposing the engine.
    /// </summary>
    public void Unmount()
    {
        FileSystem?.Unmount();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            FileSystem?.Dispose();
            Engine?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~MountedVault()
    {
        Dispose();
    }
}
