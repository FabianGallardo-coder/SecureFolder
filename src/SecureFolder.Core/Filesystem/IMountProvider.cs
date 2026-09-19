using System;
using SecureFolder.Core.Models;

namespace SecureFolder.Core.Filesystem;

/// <summary>
/// Interface for mounting a virtual filesystem.
/// Allows the Core to be agnostic of the underlying provider (e.g., WinFsp).
/// </summary>
public interface IMountProvider
{
    /// <summary>
    /// Mounts the filesystem on the specified drive letter.
    /// </summary>
    /// <returns>A result containing the mount point or an error.</returns>
    Result<string> Mount(char driveLetter, SecureFolderFileSystem fs);

    /// <summary>
    /// Unmounts the filesystem.
    /// </summary>
    Result Unmount();

    /// <summary>
    /// Returns the last error encountered during mount/unmount operations.
    /// </summary>
    int LastError { get; }
}
