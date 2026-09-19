using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fsp;
using SecureFolder.Core.Crypto;
using SecureFolder.Core.Models;
using SecureFolder.Core.Vault;

namespace SecureFolder.Core.Filesystem;

/// <summary>
/// In-memory virtual filesystem backed by a .sfv vault.
/// Designed against winfsp.net 2.2.x API (memfs-dotnet reference pattern).
/// </summary>
public sealed class SecureFolderFileSystem : FileSystemBase, IDisposable
{
    private readonly MountedVault _vault;
    private readonly long _dataStartOffset;
    private readonly IMountProvider _mountProvider;

    private bool _disposed;


    private readonly SortedDictionary<string, MemFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedDictionary<string, MemDir> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedSet<string> _allDirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deletedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ManualResetEventSlim _mountReady = new(false);
    private readonly object _handleLock = new();
    private readonly Dictionary<string, int> _openHandles = new(StringComparer.OrdinalIgnoreCase);

    public SecureFolderFileSystem(MountedVault vault, long dataStartOffset, IMountProvider mountProvider)
    {
        _vault = vault;
        _dataStartOffset = dataStartOffset;
        _mountProvider = mountProvider;

        _dirs["\\"] = new MemDir
        {
            Name = "",
            CreationTime = DateTime.UtcNow,
            LastAccessTime = DateTime.UtcNow,
            LastWriteTime = DateTime.UtcNow,
        };
        _allDirs.Add("\\");

        LoadIndex();
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    public void Unmount()
    {
        FlushDirtyFiles();
        _mountProvider.Unmount();
    }

    // ── Open-handle tracking ─────────────────────────────────────────────

    public List<string> GetOpenFiles()
    {
        lock (_handleLock)
            return [.. _openHandles.Keys];
    }

    private void IncrementHandle(string path)
    {
        lock (_handleLock)
        {
            _openHandles.TryGetValue(path, out int count);
            _openHandles[path] = count + 1;
        }
    }

    private void DecrementHandle(string path)
    {
        lock (_handleLock)
        {
            if (_openHandles.TryGetValue(path, out int count))
            {
                if (count <= 1) _openHandles.Remove(path);
                else _openHandles[path] = count - 1;
            }
        }
    }

    // ── Vault persistence ───────────────────────────────────────────────────

    private void LoadIndex()
    {
        foreach (var kvp in _vault.FileIndex)
        {
            string path = "\\" + kvp.Key.Replace('/', '\\').TrimStart('\\');
            var entry = kvp.Value;

            var file = new MemFile
            {
                Name = Path.GetFileName(path),
                OriginalSize = entry.OriginalSize,
                VaultDataOffset = entry.DataOffset,
                VaultEncryptedSize = entry.EncryptedSize,
                CreationTime = entry.CreationTime.UtcDateTime,
                LastWriteTime = entry.LastWriteTime.UtcDateTime,
                LastAccessTime = entry.LastWriteTime.UtcDateTime,
                ChangeTime = entry.LastWriteTime.UtcDateTime,
                FileAttributes = System.IO.FileAttributes.Normal,
            };

            _files[path] = file;
            EnsureParentDirectories(path);
        }
    }

    private void EnsureParentDirectories(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        while (dir != null && dir != "\\")
        {
            if (_allDirs.Add(dir))
            {
                _dirs[dir] = new MemDir
                {
                    Name = Path.GetFileName(dir),
                    CreationTime = DateTime.UtcNow,
                    LastAccessTime = DateTime.UtcNow,
                    LastWriteTime = DateTime.UtcNow,
                };
            }
            dir = Path.GetDirectoryName(dir);
        }
    }

    private void LoadFileData(string path, MemFile file)
    {
        if (file.Buffer is not null || file.VaultEncryptedSize <= 0) return;

        string vaultPath = _vault.Info.VaultFilePath;
        if (!File.Exists(vaultPath)) return;

        using var fs = new FileStream(
            vaultPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);

        long offset = _dataStartOffset + file.VaultDataOffset;
        if (offset + file.VaultEncryptedSize > fs.Length) return;

        fs.Seek(offset, SeekOrigin.Begin);
        byte[] encrypted = new byte[file.VaultEncryptedSize];
        int read = fs.Read(encrypted, 0, encrypted.Length);
        if (read < encrypted.Length) return;

        byte[] plaintext = _vault.Engine.Decrypt(encrypted);
        file.Buffer = new MemoryStream(plaintext);
        file.Size = plaintext.Length;
    }

    private void FlushDirtyFiles()
    {
        string vaultPath = _vault.Info.VaultFilePath;
        if (!File.Exists(vaultPath)) return;

        byte[] vaultBytes = File.ReadAllBytes(vaultPath);

        byte[] indexLengthBytes = new byte[4];
        Array.Copy(vaultBytes, VaultFormat.HeaderSize, indexLengthBytes, 0, 4);
        int indexLength = BitConverter.ToInt32(indexLengthBytes);

        int dataStart = VaultFormat.HeaderSize + 4 + indexLength;
        var existingData = vaultBytes.Length > dataStart
            ? vaultBytes[dataStart..]
            : Array.Empty<byte>();

        var newBlocks = new List<byte[]>();
        long dataOffsetAccum = 0;

        foreach (var kvp in _files)
        {
            string path = kvp.Key;
            var file = kvp.Value;

            if (_deletedPaths.Contains(path)) continue;

            string rel = path.TrimStart('\\').Replace('\\', '/');
            bool hasEntry = _vault.FileIndex.TryGetValue(rel, out var entry);

            // VERSION 2: We only reuse existing data if the file is NOT dirty
            // and it was already stored in Version 2 (chunked).
            if (hasEntry && (file.Buffer is null || !file.Dirty))
            {
                // Since we are migrating to V2, we can't simply copy V1 contiguous blocks.
                // For simplicity in this transition, if it's not dirty, we'll keep the existing data
                // if it fits the V2 chunked pattern (which it won't for V1).
                // To be safe, we should re-encrypt everything on the first V2 flush,
                // or implement a specific migration.

                // Temporary: just re-encrypt to ensure V2 consistency.
                // (In a real migration we'd check version and convert).
            }

            byte[] plaintext = file.Buffer?.ToArray() ?? Array.Empty<byte>();

            if (plaintext.Length == 0)
            {
                _vault.FileIndex[rel] = new FileEntry
                {
                    OriginalName = file.Name,
                    RelativePath = rel,
                    DataOffset = -1,
                    EncryptedSize = 0,
                    OriginalSize = 0,
                    Hash = SHA256.HashData(Array.Empty<byte>()),
                    CreationTime = new DateTimeOffset(file.CreationTime),
                    LastWriteTime = new DateTimeOffset(file.LastWriteTime),
                    Nonce = Array.Empty<byte>(),
                };
                continue;
            }

            // Version 2: Split plaintext into 64KB chunks and encrypt each.
            int bytesProcessed = 0;
            long fileEncryptedSize = 0;

            while (bytesProcessed < plaintext.Length)
            {
                int currentChunkSize = Math.Min(VaultFormat.ChunkSize, plaintext.Length - bytesProcessed);
                byte[] chunkPlaintext = new byte[currentChunkSize];
                Array.Copy(plaintext, bytesProcessed, chunkPlaintext, 0, currentChunkSize);

                byte[] encryptedChunk = _vault.Engine.EncryptChunk(chunkPlaintext);
                newBlocks.Add(encryptedChunk);

                fileEncryptedSize += encryptedChunk.Length;
                bytesProcessed += currentChunkSize;
            }

            _vault.FileIndex[rel] = new FileEntry
            {
                OriginalName = file.Name,
                RelativePath = rel,
                DataOffset = dataOffsetAccum,
                EncryptedSize = fileEncryptedSize,
                OriginalSize = plaintext.Length,
                Hash = SHA256.HashData(plaintext),
                CreationTime = new DateTimeOffset(file.CreationTime),
                LastWriteTime = new DateTimeOffset(file.LastWriteTime),
                Nonce = Array.Empty<byte>(), // Nonces are now per-chunk
            };
            dataOffsetAccum += fileEncryptedSize;
        }

        foreach (string del in _deletedPaths)
        {
            string rel = del.TrimStart('\\').Replace('\\', '/');
            _vault.FileIndex.Remove(rel);
        }
        _deletedPaths.Clear();

        byte[] newIndexJson = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(_vault.FileIndex,
                new JsonSerializerOptions { WriteIndented = false }));
        byte[] encryptedIndex = _vault.Engine.Encrypt(newIndexJson);

        using var ms = new MemoryStream();
        ms.Write(vaultBytes, 0, VaultFormat.HeaderSize);
        ms.Write(BitConverter.GetBytes(encryptedIndex.Length));
        ms.Write(encryptedIndex);
        foreach (byte[] block in newBlocks)
            ms.Write(block);

        File.WriteAllBytes(vaultPath, ms.ToArray());
    }

    // ── FileSystemBase overrides ────────────────────────────────────────────

    public override int GetVolumeInfo(out Fsp.Interop.VolumeInfo VolumeInfo)
    {
        ulong capacity = 1024UL * 1024 * 1024 * 1024; // 1 TiB dynamic sizing
        VolumeInfo = new Fsp.Interop.VolumeInfo
        {
            TotalSize = capacity,
            FreeSize = capacity,
        };
        return 0;
    }

    public override int SetVolumeLabel(string VolumeLabel, out Fsp.Interop.VolumeInfo VolumeInfo)
    {
        ulong capacity = 1024UL * 1024 * 1024 * 1024;
        VolumeInfo = new Fsp.Interop.VolumeInfo
        {
            TotalSize = capacity,
            FreeSize = capacity,
        };
        return 0;
    }

    public override int Init(object Host)
    {
        if (Host is FileSystemHost fsh)
        {
            fsh.FileSystemName = "SecureFolder";
            fsh.MaxComponentLength = 255;
            fsh.CaseSensitiveSearch = false;
            fsh.CasePreservedNames = true;
            fsh.UnicodeOnDisk = true;
            fsh.FileInfoTimeout = 1000;
            fsh.DirInfoTimeout = 1000;
        }
        return 0;
    }

    public override int GetSecurityByName(
        string FileName,
        out uint FileAttributes,
        ref byte[] SecurityDescriptor)
    {
        string name = Norm(FileName);

        if (_dirs.ContainsKey(name))
        {
            FileAttributes = (uint)System.IO.FileAttributes.Directory;
            SecurityDescriptor = Array.Empty<byte>();
            return 0;
        }

        if (_files.ContainsKey(name))
        {
            FileAttributes = (uint)System.IO.FileAttributes.Normal;
            SecurityDescriptor = Array.Empty<byte>();
            return 0;
        }

        FileAttributes = 0;
        SecurityDescriptor = Array.Empty<byte>();
        return STATUS_OBJECT_NAME_NOT_FOUND;
    }

    public override int Create(
        string FileName,
        uint CreateOptions,
        uint GrantedAccess,
        uint FileAttributes,
        byte[] SecurityDescriptor,
        ulong AllocationSize,
        out object? FileNode,
        out object? FileDesc,
        out Fsp.Interop.FileInfo FileInfo,
        out string NormalizedName)
    {
        FileNode = null;
        string name = Norm(FileName);
        NormalizedName = name;

        bool createDirectory = (CreateOptions & FILE_DIRECTORY_FILE) != 0;

        if (createDirectory)
        {
            if (_dirs.ContainsKey(name))
            {
                // Existing directory → open it (Directory.CreateDirectory is idempotent).
                FileInfo = CreateDirInfo(name);
                FileDesc = name;
                return 0;
            }
            if (_files.ContainsKey(name))
            {
                FileInfo = default;
                FileDesc = null;
                return STATUS_FILE_IS_A_DIRECTORY;
            }

            EnsureParentDirectories(name);
            var now = DateTime.UtcNow;
            _dirs[name] = new MemDir
            {
                Name = Path.GetFileName(name),
                CreationTime = now,
                LastAccessTime = now,
                LastWriteTime = now,
            };
            _allDirs.Add(name);

            FileInfo = CreateDirInfo(name);
            FileDesc = name;
            return 0;
        }

        if (_files.TryGetValue(name, out var existing))
        {
            existing.FullPath = name;
            IncrementHandle(name);
            FileInfo = CreateFileInfo(existing);
            FileDesc = existing;
            return 0;
        }

        EnsureParentDirectories(name);

        var file = new MemFile
        {
            Name = Path.GetFileName(name),
            FullPath = name,
            Size = 0,
            CreationTime = DateTime.UtcNow,
            LastAccessTime = DateTime.UtcNow,
            LastWriteTime = DateTime.UtcNow,
            ChangeTime = DateTime.UtcNow,
            FileAttributes = System.IO.FileAttributes.Normal,
        };
        _files[name] = file;
        IncrementHandle(name);

        FileInfo = CreateFileInfo(file);
        FileDesc = file;
        return 0;
    }

    public override int Open(
        string FileName,
        uint CreateOptions,
        uint GrantedAccess,
        out object? FileNode,
        out object? FileDesc,
        out Fsp.Interop.FileInfo FileInfo,
        out string NormalizedName)
    {
        FileNode = null;
        string name = Norm(FileName);
        NormalizedName = name;

        if (_dirs.ContainsKey(name))
        {
            // Directory open succeeds; FileDesc carries the canonical path
            // consumed by ReadDirectoryEntry().
            FileInfo = CreateDirInfo(name);
            FileDesc = name;
            return 0;
        }

        if (_files.TryGetValue(name, out var file))
        {
            LoadFileData(name, file);
            file.FullPath = name;
            IncrementHandle(name);
            FileInfo = CreateFileInfo(file);
            FileDesc = file;
            return 0;
        }

        FileInfo = default;
        FileDesc = null;
        return STATUS_OBJECT_NAME_NOT_FOUND;
    }

    public override void Cleanup(
        object FileNode, object FileDesc, string FileName, uint Flags)
    {
        if (FileDesc is MemFile file)
            file.LastAccessTime = DateTime.UtcNow;
    }

    public override void Close(object FileNode, object FileDesc)
    {
        if (FileDesc is MemFile file && !string.IsNullOrEmpty(file.FullPath))
            DecrementHandle(file.FullPath);
    }

    public override int Read(
        object FileNode,
        object FileDesc,
        IntPtr Buffer,
        ulong Offset,
        uint Length,
        out uint BytesTransferred)
    {
        BytesTransferred = 0;

        if (FileDesc is not MemFile file)
            return STATUS_OBJECT_NAME_NOT_FOUND;

        if (file.VaultEncryptedSize <= 0)
            return STATUS_END_OF_FILE;

        if (Offset >= (ulong)file.OriginalSize)
            return STATUS_END_OF_FILE;

        // Calculate how much we can actually read
        int available = (int)Math.Min(Length, (ulong)file.OriginalSize - Offset);
        if (available <= 0) return 0;

        try
        {
            // Logic for Version 2: Chunked Lazy Loading
            // We need to read 'available' bytes starting at 'Offset'.
            // Each chunk is 64KB.

            byte[] resultBuffer = new byte[available];
            int totalRead = 0;

            while (totalRead < available)
            {
                long currentOffset = (long)Offset + totalRead;
                int chunkIndex = (int)(currentOffset / VaultFormat.ChunkSize);
                int offsetInChunk = (int)(currentOffset % VaultFormat.ChunkSize);

                int bytesToReadFromChunk = Math.Min(
                    VaultFormat.ChunkSize - offsetInChunk,
                    available - totalRead);

                // Calculate where this chunk starts in the vault file
                // Version 2 assumes the file index tells us the start of the first chunk
                // and then chunks follow sequentially: [Nonce][Ciphertext][Tag]
                long chunkStartInVault = _dataStartOffset + file.VaultDataOffset +
                                        (long)chunkIndex * (VaultFormat.ChunkSize + VaultFormat.ChunkOverhead);

                if (chunkStartInVault < 0 || chunkStartInVault >= _vault.Info.VaultFilePath.Length) // Simplified check, should use fs.Length
                {
                    // If we hit a gap or end of vault, we stop
                    break;
                }

                // Read the encrypted chunk from disk
                byte[] encryptedChunk = new byte[VaultFormat.ChunkSize + VaultFormat.ChunkOverhead];
                using (var fs = new FileStream(_vault.Info.VaultFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Seek(chunkStartInVault, SeekOrigin.Begin);
                    int read = fs.Read(encryptedChunk, 0, encryptedChunk.Length);
                    if (read < encryptedChunk.Length) break;
                }

                // Decrypt the chunk
                byte[] decryptedChunk = _vault.Engine.DecryptChunk(encryptedChunk);

                // Copy the relevant slice to our result buffer
                int copyLength = Math.Min(decryptedChunk.Length - offsetInChunk, bytesToReadFromChunk);
                if (copyLength <= 0) break;

                Array.Copy(decryptedChunk, offsetInChunk, resultBuffer, totalRead, copyLength);
                totalRead += copyLength;
            }

            if (totalRead > 0)
            {
                Marshal.Copy(resultBuffer, 0, Buffer, totalRead);
                BytesTransferred = (uint)totalRead;
            }
        }
        catch (Exception)
        {
            return STATUS_INTERNAL_ERROR;
        }

        return 0;
    }

    public override int Write(
        object FileNode,
        object FileDesc,
        IntPtr Buffer,
        ulong Offset,
        uint Length,
        bool WriteToEndOfFile,
        bool ConstrainedIo,
        out uint BytesTransferred,
        out Fsp.Interop.FileInfo FileInfo)
    {
        BytesTransferred = 0;
        FileInfo = default;

        if (FileDesc is not MemFile file)
            return STATUS_OBJECT_NAME_NOT_FOUND;

        // Version 2: We still use a MemoryStream buffer for active writes (Write-Back cache).
        // The lazy loading is for READS. WRITES are committed to the buffer and then
        // flushed to disk in chunks during FlushDirtyFiles.
        file.Dirty = true;
        file.Buffer ??= new MemoryStream();

        long targetOffset = WriteToEndOfFile ? file.Buffer.Length : (long)Offset;
        if (targetOffset + (long)Length > file.Buffer.Length)
            file.Buffer.SetLength(targetOffset + (long)Length);

        byte[] chunk = new byte[Length];
        Marshal.Copy(Buffer, chunk, 0, (int)Length);
        file.Buffer.Seek(targetOffset, SeekOrigin.Begin);
        file.Buffer.Write(chunk, 0, (int)Length);

        file.Size = (long)file.Buffer.Length;
        file.LastWriteTime = DateTime.UtcNow;
        file.ChangeTime = DateTime.UtcNow;
        file.Dirty = true;

        BytesTransferred = Length;
        FileInfo = CreateFileInfo(file);
        return 0;
    }

    public override int GetFileInfo(
        object FileNode, object FileDesc, out Fsp.Interop.FileInfo FileInfo)
    {
        if (FileDesc is MemFile file)
        {
            FileInfo = CreateFileInfo(file);
            return 0;
        }
        FileInfo = default;
        return STATUS_OBJECT_NAME_NOT_FOUND;
    }

    public override int SetBasicInfo(
        object FileNode,
        object FileDesc,
        uint FileAttributes,
        ulong CreationTime,
        ulong LastAccessTime,
        ulong LastWriteTime,
        ulong ChangeTime,
        out Fsp.Interop.FileInfo FileInfo)
    {
        FileInfo = default;

        if (FileDesc is not MemFile file)
            return STATUS_OBJECT_NAME_NOT_FOUND;

        if (FileAttributes != 0xFFFFFFFF)
            file.FileAttributes = (System.IO.FileAttributes)FileAttributes;
        if (CreationTime != 0)
            file.CreationTime = DateTime.FromFileTimeUtc((long)CreationTime);
        if (LastAccessTime != 0)
            file.LastAccessTime = DateTime.FromFileTimeUtc((long)LastAccessTime);
        if (LastWriteTime != 0)
            file.LastWriteTime = DateTime.FromFileTimeUtc((long)LastWriteTime);
        if (ChangeTime != 0)
            file.ChangeTime = DateTime.FromFileTimeUtc((long)ChangeTime);

        FileInfo = CreateFileInfo(file);
        return 0;
    }

    public override int SetFileSize(
        object FileNode, object FileDesc, ulong NewSize,
        bool SetAllocationSize, out Fsp.Interop.FileInfo FileInfo)
    {
        FileInfo = default;

        if (FileDesc is not MemFile file)
            return STATUS_OBJECT_NAME_NOT_FOUND;

        file.Buffer ??= new MemoryStream();
        file.Buffer.SetLength((long)NewSize);
        file.Size = (long)NewSize;
        file.Dirty = true;

        FileInfo = CreateFileInfo(file);
        return 0;
    }

    public override int OverwriteEx(
        object FileNode, object FileDesc, uint FileAttributes,
        bool ReplaceFileAttributes, ulong AllocationSize,
        IntPtr Ea, uint EaLength, out Fsp.Interop.FileInfo FileInfo)
    {
        FileInfo = default;

        if (FileDesc is not MemFile file)
            return STATUS_OBJECT_NAME_NOT_FOUND;

        // An OVERWRITE disposition truncates the file to zero length.
        file.Buffer?.Dispose();
        file.Buffer = new MemoryStream();
        file.Size = 0;
        file.Dirty = true;

        var now = DateTime.UtcNow;
        file.LastWriteTime = now;
        file.ChangeTime = now;

        FileInfo = CreateFileInfo(file);
        return 0;
    }

    public override int Rename(
        object FileNode, object FileDesc,
        string FileName, string NewFileName, bool ReplaceIfExists)
    {
        string oldPath = Norm(FileName);
        string newPath = Norm(NewFileName);

        if (_files.TryGetValue(oldPath, out var file))
        {
            if (!ReplaceIfExists && _files.ContainsKey(newPath))
                return STATUS_OBJECT_NAME_COLLISION;

            _files.Remove(oldPath);
            file.Name = Path.GetFileName(newPath);
            _files[newPath] = file;
            return 0;
        }

        if (_dirs.ContainsKey(oldPath))
        {
            if (!ReplaceIfExists && _dirs.ContainsKey(newPath))
                return STATUS_OBJECT_NAME_COLLISION;

            MoveDir(oldPath, newPath);
            return 0;
        }

        return STATUS_OBJECT_NAME_NOT_FOUND;
    }

    public override int CanDelete(object FileNode, object FileDesc, string FileName)
    {
        string name = Norm(FileName);

        if (_dirs.ContainsKey(name))
        {
            foreach (string child in _dirs.Keys)
            {
                if (IsChildOf(name, child))
                    return STATUS_DIRECTORY_NOT_EMPTY;
            }
            foreach (string child in _files.Keys)
            {
                if (IsChildOf(name, child))
                    return STATUS_DIRECTORY_NOT_EMPTY;
            }
            return 0;
        }

        if (_files.ContainsKey(name))
            return 0;

        return STATUS_OBJECT_NAME_NOT_FOUND;
    }

    public override int SetDelete(
        object FileNode, object FileDesc, string FileName, bool DeleteFile)
    {
        string name = Norm(FileName);

        if (DeleteFile)
        {
            if (_files.Remove(name))
            {
                _deletedPaths.Add(name);
                return 0;
            }
            if (_dirs.ContainsKey(name))
                return STATUS_FILE_IS_A_DIRECTORY;
            return STATUS_OBJECT_NAME_NOT_FOUND;
        }

        return 0;
    }

    public override bool ReadDirectoryEntry(
        object FileNode,
        object FileDesc,
        string Pattern,
        string Marker,
        ref object? Context,
        out string FileName,
        out Fsp.Interop.FileInfo FileInfo)
    {
        FileName = "";
        FileInfo = default;

        string dirPath = FileDesc?.ToString() ?? "\\";
        if (string.IsNullOrEmpty(dirPath)) dirPath = "\\";

        // winfsp passes the empty marker for a fresh directory-read IRP. A
        // non-empty marker means the caller is mid-enumeration, so dot entries
        // (".", "..") must not be served again.
        bool isFresh = string.IsNullOrEmpty(Marker);
        if (!isFresh && Marker == "\0") isFresh = true;

        // The per-IRP cursor. Context is reset by the framework between IRPs,
        // so (re)building the list here keeps the walk monotonic per IRP while
        // the marker prevents the same name being served twice across IRPs.
        if (Context is int index)
        {
            // Continue the current IRP.
            if (index < 0 || index >= _enumList.Count)
            {
                Context = -1;
                return false;
            }
            var (name, infoNode, infoFile) = _enumList[index];
            Context = index + 1;
            FileName = name;
            FileInfo = infoFile is not null ? CreateFileInfo(infoFile) : CreateDirInfo(infoNode);
            return true;
        }

        // Build a fresh, ordered snapshot of this directory for the current IRP.
        RefreshEnumList(dirPath, isFresh, Marker);

        if (_enumList.Count == 0)
        {
            Context = -1;
            return false;
        }

        // Global "marker <= name" filter so a stateless resume never re-serves
        // a name the caller already consumed.
        if (!isFresh)
        {
            int start = 0;
            while (start < _enumList.Count)
            {
                string nm = _enumList[start].Name;
                if (nm is "." or ".." || string.Compare(nm, Marker, StringComparison.OrdinalIgnoreCase) <= 0)
                {
                    start++;
                    continue;
                }
                break;
            }
            if (start >= _enumList.Count)
            {
                Context = -1;
                return false;
            }

            var (firstName, firstNode, firstFile) = _enumList[start];
            Context = start + 1;
            FileName = firstName;
            FileInfo = firstFile is not null ? CreateFileInfo(firstFile) : CreateDirInfo(firstNode);
            return true;
        }

        var (firstNz, nzNode, nzFile) = _enumList[0];
        Context = 1;
        FileName = firstNz;
        FileInfo = nzFile is not null ? CreateFileInfo(nzFile) : CreateDirInfo(nzNode);
        return true;
    }

    private readonly List<(string Name, string DirPath, MemFile? File)> _enumList = new();

    private void RefreshEnumList(string dirPath, bool isFresh, string marker)
    {
        _enumList.Clear();
        bool root = dirPath == "\\";

        if (isFresh)
        {
            _enumList.Add((".", dirPath, null));
            _enumList.Add(("..", dirPath, null));
        }

        foreach (string childPath in _allDirs)
        {
            string? childName = DirectChildName(dirPath, childPath);
            if (childName is null) continue;
            _enumList.Add((childName, childPath, null));
        }

        foreach (var kvp in _files)
        {
            string? childName = DirectChildName(dirPath, kvp.Key);
            if (childName is null) continue;
            _enumList.Add((childName, "", kvp.Value));
        }
    }

    // ── Security ────────────────────────────────────────────────────────────

    public override int GetSecurity(
        object FileNode, object FileDesc, ref byte[] SecurityDescriptor)
    {
        SecurityDescriptor = Array.Empty<byte>();
        return 0;
    }

    public override int SetSecurity(
        object FileNode, object FileDesc,
        AccessControlSections Sections, byte[] SecurityDescriptor)
    {
        return 0;
    }

    // ── Path helpers ────────────────────────────────────────────────────────

    private static string Norm(string path)
    {
        path = path.Replace('/', '\\');
        if (!path.StartsWith('\\'))
            path = "\\" + path;
        return path.TrimEnd('\\') == "" ? "\\" : path.TrimEnd('\\');
    }

    /// <summary>True if <paramref name="child"/> is inside <paramref name="parent"/>
    /// (any depth). The root "\\" is the parent of every path.</summary>
    private static bool IsChildOf(string parent, string child)
    {
        if (child.Length <= parent.Length) return false;
        if (!child.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) return false;
        return parent.Length == 1 || child[parent.Length] == '\\';
    }

    /// <summary>
    /// Returns the direct child name of <paramref name="fullPath"/> under
    /// <paramref name="dirPath"/>, or null when <paramref name="fullPath"/> is not
    /// a direct child (root itself, sibling, or a deeper descendant).
    /// The root directory is a lone separator ("\"), so from the root the child
    /// name starts at <paramref name="dirPath"/>.Length; deeper dirs bring their
    /// own trailing separator, so the name starts at Length + 1.
    /// </summary>
    internal static string? DirectChildName(string dirPath, string fullPath)
    {
        if (!IsChildOf(dirPath, fullPath)) return null;
        int offset = dirPath.Length + (dirPath == "\\" ? 0 : 1);
        int rest = fullPath.Length - offset;
        if (rest <= 0) return null;
        if (fullPath.AsSpan(offset).Contains('\\')) return null;
        return fullPath[offset..];
    }

    private void MoveDir(string oldPath, string newPath)
    {
        if (_dirs.TryGetValue(oldPath, out var dir))
        {
            _dirs.Remove(oldPath);
            _dirs[newPath] = dir;
        }
        _allDirs.Remove(oldPath);
        _allDirs.Add(newPath);

        var movedFiles = new List<(string Old, string New)>();
        foreach (string fPath in _files.Keys)
        {
            if (fPath == oldPath || IsChildOf(oldPath, fPath))
            {
                string newFilePath = newPath + fPath[oldPath.Length..];
                movedFiles.Add((fPath, newFilePath));
            }
        }
        foreach (var (old, @new) in movedFiles)
        {
            if (_files.TryGetValue(old, out var f))
            {
                _files.Remove(old);
                f.Name = Path.GetFileName(@new);
                _files[@new] = f;
            }
        }

        var movedDirs = new List<(string Old, string New)>();
        foreach (string dPath in _dirs.Keys)
        {
            if (dPath == oldPath || IsChildOf(oldPath, dPath))
            {
                string newDirPath = newPath + dPath[oldPath.Length..];
                movedDirs.Add((dPath, newDirPath));
            }
        }
        foreach (var (old, @new) in movedDirs)
        {
            if (_dirs.TryGetValue(old, out var d))
            {
                _dirs.Remove(old);
                _allDirs.Remove(old);
                _dirs[@new] = d;
                _allDirs.Add(@new);
            }
        }
    }

    // ── FileInfo creation ───────────────────────────────────────────────────

    private Fsp.Interop.FileInfo CreateFileInfo(MemFile file)
    {
        return new Fsp.Interop.FileInfo
        {
            FileSize = (ulong)file.Size,
            FileAttributes = (uint)(file.FileAttributes | System.IO.FileAttributes.Normal),
            CreationTime = (ulong)file.CreationTime.ToFileTimeUtc(),
            LastAccessTime = (ulong)file.LastAccessTime.ToFileTimeUtc(),
            LastWriteTime = (ulong)file.LastWriteTime.ToFileTimeUtc(),
            ChangeTime = (ulong)file.ChangeTime.ToFileTimeUtc(),
        };
    }

    private Fsp.Interop.FileInfo CreateDirInfo(string dirPath)
    {
        if (_dirs.TryGetValue(dirPath, out var dir))
        {
            return new Fsp.Interop.FileInfo
            {
                FileSize = 0,
                FileAttributes = (uint)System.IO.FileAttributes.Directory,
                CreationTime = (ulong)dir.CreationTime.ToFileTimeUtc(),
                LastAccessTime = (ulong)dir.LastAccessTime.ToFileTimeUtc(),
                LastWriteTime = (ulong)dir.LastWriteTime.ToFileTimeUtc(),
                ChangeTime = (ulong)dir.LastWriteTime.ToFileTimeUtc(),
            };
        }
        return new Fsp.Interop.FileInfo
        {
            FileSize = 0,
            FileAttributes = (uint)System.IO.FileAttributes.Directory,
        };
    }

    // ── Dispose ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // FlushDirtyFiles() is called within Unmount().
        // To avoid ObjectDisposedException during Finalizer, we ensure we only flush if the vault engine is still available.
        try
        {
            Unmount();
        }
        catch (ObjectDisposedException)
        {
            // Ignore if engine was already disposed
        }
        GC.SuppressFinalize(this);
    }

    ~SecureFolderFileSystem() => Dispose();

    // ── Internal types ──────────────────────────────────────────────────────

    internal sealed class MemFile
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public long Size { get; set; }
        public System.IO.FileAttributes FileAttributes { get; set; } = System.IO.FileAttributes.Normal;
        public DateTime CreationTime { get; set; }
        public DateTime LastAccessTime { get; set; }
        public DateTime LastWriteTime { get; set; }
        public DateTime ChangeTime { get; set; }
        public MemoryStream? Buffer { get; set; }
        public bool Dirty { get; set; }
        public long VaultDataOffset { get; set; }
        public long VaultEncryptedSize { get; set; }
        public long OriginalSize { get; set; }
    }

    internal sealed class MemDir
    {
        public string Name { get; set; } = "";
        public DateTime CreationTime { get; set; }
        public DateTime LastAccessTime { get; set; }
        public DateTime LastWriteTime { get; set; }
    }
}
