using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureFolder.Core.Crypto;
using SecureFolder.Core.Filesystem;
using SecureFolder.Core.Models;

namespace SecureFolder.Core.Vault;

/// <summary>
/// Manages vault lifecycle: create, open, close, lock, unlock.
/// </summary>
public sealed class VaultManager : IDisposable
{
    private readonly Dictionary<string, MountedVault> _mountedVaults = new();
    private readonly List<VaultInfo> _vaults = new();
    private readonly string _configPath;
    private bool _disposed;

    public IReadOnlyList<VaultInfo> Vaults => _vaults.AsReadOnly();
    public IReadOnlyDictionary<string, MountedVault> MountedVaults => _mountedVaults;

    public VaultManager(string appDataPath)
    {
        _configPath = Path.Combine(appDataPath, "vaults.json");
        LoadVaultList();
    }

    /// <summary>
    /// Creates a new vault with the given password.
    /// </summary>
    public async Task<Result<VaultInfo>> CreateVaultAsync(
        string name,
        string storagePath,
        string password,
        CancellationToken ct = default)
    {
        try
        {
            string id = Guid.NewGuid().ToString("N");
            string vaultFileName = $"{SanitizeFileName(name)}.sfv";
            string vaultFilePath = Path.Combine(storagePath, vaultFileName);

            if (File.Exists(vaultFilePath))
                return Result<VaultInfo>.Fail("Ya existe un archivo de carpeta segura en esta ubicación.");

            // Ensure directory exists
            Directory.CreateDirectory(storagePath);

            // Create the vault file
            await VaultFormat.CreateVaultAsync(vaultFilePath, password, name, ct);

            var info = new VaultInfo
            {
                Id = id,
                Name = name,
                VaultFilePath = vaultFilePath
            };

            _vaults.Add(info);
            SaveVaultList();

            return Result<VaultInfo>.Ok(info);
        }
        catch (Exception ex)
        {
            return Result<VaultInfo>.Fail($"No se pudo crear la carpeta segura: {ex.Message}");
        }
    }

    /// <summary>
    /// Unlocks a vault with the given password and mounts it as a virtual drive.
    /// </summary>
    public async Task<Result<MountedVault>> UnlockVaultAsync(
        string vaultId,
        string password,
        CancellationToken ct = default)
    {
        try
        {
            var info = _vaults.FirstOrDefault(v => v.Id == vaultId);
            if (info is null)
                return Result<MountedVault>.Fail("Carpeta segura no encontrada.");

            if (info.IsUnlocked)
                return Result<MountedVault>.Fail("La carpeta segura ya está desbloqueada.");

            if (!File.Exists(info.VaultFilePath))
                return Result<MountedVault>.Fail("No se encontró el archivo de la carpeta segura en el disco.");

            // Open and verify the vault
            var openResult = await VaultFormat.OpenVaultAsync(info.VaultFilePath, password, ct);

            // Parse file index (tolerant of empty index as "[]" or "{}")
            var fileIndex = new Dictionary<string, FileEntry>();
            try
            {
                fileIndex = JsonSerializer.Deserialize<Dictionary<string, FileEntry>>(
                    openResult.IndexJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new Dictionary<string, FileEntry>();
            }
            catch (JsonException)
            {
                // Empty or legacy index format — treat as no files
            }

            // Create the crypto engine
            var engine = new AesGcmEngine(openResult.Dek);

            // Find available drive letter
            char driveLetter = FindAvailableDriveLetter();
            string mountPoint = $"{driveLetter}:\\";

            // Create mounted vault
            var mounted = new MountedVault
            {
                Info = info,
                Engine = engine,
                FileIndex = fileIndex,
                MountPoint = mountPoint,
            };

            // Create virtual filesystem (in-memory state + vault loading)
            var fs = new SecureFolderFileSystem(mounted, openResult.DataStartOffset);
            mounted.FileSystem = fs;

            // Mount (synchronous — runs WinFsp dispatcher on a background thread)
            fs.Mount(driveLetter);

            // Verify mount succeeded
            int mountError = fs.LastMountError;
            if (mountError != 0)
            {
                fs.Dispose();
                engine.Dispose();
                return Result<MountedVault>.Fail(
                    $"No se pudo montar la unidad (error 0x{mountError:X8}). " +
                    "Asegúrese de que WinFsp está instalado: https://winfsp.dev/rel/");
            }

            mounted.Info.IsUnlocked = true;
            mounted.Info.DriveLetter = driveLetter;
            mounted.Info.LastUnlockedAt = DateTimeOffset.UtcNow;
            _mountedVaults[info.Id] = mounted;

            SaveVaultList();
            return Result<MountedVault>.Ok(mounted);
        }
        catch (InvalidOperationException ex)
        {
            return Result<MountedVault>.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return Result<MountedVault>.Fail($"No se pudo desbloquear la carpeta segura: {ex.Message}");
        }
    }

    /// <summary>
    /// Locks a vault: unmounts the virtual drive and re-encrypts data.
    /// </summary>
    public async Task<Result> LockVaultAsync(
        string vaultId,
        CancellationToken ct = default)
    {
        try
        {
            if (!_mountedVaults.TryGetValue(vaultId, out var mounted))
                return Result.Fail("La carpeta segura no está desbloqueada.");

            // Check for open files via the FS handle tracker
            var openFiles = mounted.FileSystem?.GetOpenFiles() ?? [];
            if (openFiles.Count > 0)
            {
                string fileList = string.Join("\n", openFiles.Take(10));
                return Result.Fail($"Los siguientes archivos siguen abiertos:\n{fileList}\n\nCiérralos antes de bloquear.");
            }

            // Unmount virtual filesystem (flushes dirty files + rewrites vault)
            mounted.Unmount();

            mounted.Info.IsUnlocked = false;
            mounted.Info.DriveLetter = null;
            mounted.Info.LastLockedAt = DateTimeOffset.UtcNow;

            _mountedVaults.Remove(vaultId);
            mounted.Dispose();

            SaveVaultList();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"No se pudo bloquear la carpeta segura: {ex.Message}");
        }
    }

    /// <summary>
    /// Changes the password of a vault.
    /// </summary>
    public async Task<Result> ChangePasswordAsync(
        string vaultId,
        string currentPassword,
        string newPassword,
        CancellationToken ct = default)
    {
        try
        {
            var info = _vaults.FirstOrDefault(v => v.Id == vaultId);
            if (info is null)
                return Result.Fail("Carpeta segura no encontrada.");

            if (info.IsUnlocked)
                return Result.Fail("No se puede cambiar la contraseña con la carpeta desbloqueada. Bloquéala primero.");

            await VaultFormat.ChangePasswordAsync(info.VaultFilePath, currentPassword, newPassword, ct);
            return Result.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return Result.Fail($"No se pudo cambiar la contraseña: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes a vault from the list (does not delete the file).
    /// </summary>
    public Result RemoveVault(string vaultId)
    {
        if (_mountedVaults.ContainsKey(vaultId))
            return Result.Fail("No se puede eliminar una carpeta montada. Bloquéala primero.");

        var info = _vaults.FirstOrDefault(v => v.Id == vaultId);
        if (info is null)
            return Result.Fail("Carpeta segura no encontrada.");

        _vaults.Remove(info);
        SaveVaultList();
        return Result.Ok();
    }

    /// <summary>
    /// Detects files currently open on the given drive letter.
    /// </summary>
    public static List<string> DetectOpenFiles(char driveLetter)
    {
        var openFiles = new List<string>();

        try
        {
            // Use handle.exe or handle64.exe if available, otherwise use WMI
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "handle.exe",
                    Arguments = $"{driveLetter}:",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            foreach (string line in output.Split('\n'))
            {
                if (line.Contains($"{driveLetter}:\\") && !line.Contains("handle.exe"))
                {
                    openFiles.Add(line.Trim());
                }
            }
        }
        catch
        {
            // handle.exe not available — try alternative approach
            try
            {
                var processes = Process.GetProcesses();
                foreach (var proc in processes)
                {
                    try
                    {
                        if (proc.MainModule?.FileName?.StartsWith($"{driveLetter}:\\") == true)
                        {
                            openFiles.Add($"{proc.ProcessName} ({proc.Id})");
                        }
                    }
                    catch { /* Access denied — skip */ }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch { /* Best effort */ }
        }

        return openFiles;
    }

    private static char FindAvailableDriveLetter()
    {
        var drives = DriveInfo.GetDrives().Select(d => d.Name.ToUpper()[0]).ToHashSet();
        for (char c = 'Z'; c >= 'D'; c--)
        {
            if (!drives.Contains(c))
                return c;
        }
        throw new InvalidOperationException("No hay letras de unidad disponibles.");
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    private void SaveVaultList()
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (dir != null) Directory.CreateDirectory(dir);

            var entries = _vaults.Select(v => new VaultConfigEntry
            {
                Id = v.Id,
                Name = v.Name,
                VaultFilePath = v.VaultFilePath,
                AutoLockTimeoutMinutes = v.AutoLockTimeoutMinutes,
                AutoLockOnShutdown = v.AutoLockOnShutdown
            }).ToList();

            string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch { /* Best effort — don't crash on config save failure */ }
    }

    private void LoadVaultList()
    {
        try
        {
            if (!File.Exists(_configPath)) return;

            string json = File.ReadAllText(_configPath);
            var entries = JsonSerializer.Deserialize<List<VaultConfigEntry>>(json);
            if (entries is null) return;

            foreach (var entry in entries)
            {
                _vaults.Add(new VaultInfo
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    VaultFilePath = entry.VaultFilePath,
                    AutoLockTimeoutMinutes = entry.AutoLockTimeoutMinutes,
                    AutoLockOnShutdown = entry.AutoLockOnShutdown
                });
            }
        }
        catch { /* Best effort — start fresh on corrupt config */ }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (var vault in _mountedVaults.Values)
            {
                vault.Dispose();
            }
            _mountedVaults.Clear();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    private sealed class VaultConfigEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string VaultFilePath { get; set; } = "";
        public int AutoLockTimeoutMinutes { get; set; }
        public bool AutoLockOnShutdown { get; set; } = true;
    }
}
