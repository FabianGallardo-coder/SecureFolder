using System;
using Fsp;
using SecureFolder.Core.Models;

namespace SecureFolder.Core.Filesystem;

/// <summary>
/// WinFsp implementation of the IMountProvider.
/// Handles the low-level WinFsp host and dispatcher thread.
/// </summary>
public sealed class WinFspMountProvider : IMountProvider, IDisposable
{
    private FileSystemHost? _host;
    private Thread? _dispatchThread;
    private int _lastError;
    private readonly ManualResetEventSlim _mountReady = new(false);
    private bool _disposed;

    public int LastError => _lastError;

    public Result<string> Mount(char driveLetter, SecureFolderFileSystem fs)
    {
        _mountReady.Reset();
        _lastError = 0;

        string mountPoint = $"{driveLetter}:";

        _host = new FileSystemHost(fs)
        {
            FileSystemName = "SecureFolder",
            MaxComponentLength = 255,
            CaseSensitiveSearch = false,
            CasePreservedNames = true,
            UnicodeOnDisk = true,
            VolumeSerialNumber = 0x5346564C,
            VolumeCreationTime = (ulong)DateTime.UtcNow.ToFileTimeUtc(),
            FileInfoTimeout = 1000,
            DirInfoTimeout = 1000,
            PostCleanupWhenModifiedOnly = true,
            FlushAndPurgeOnCleanup = true,
        };

        _dispatchThread = new Thread(() =>
        {
            int status = _host.Mount(mountPoint, null!, false, 0);
            if (status < 0)
            {
                _lastError = status;
                _mountReady.Set();
            }
        })
        {
            IsBackground = true,
            Name = "WinFsp-Mount",
        };
        _dispatchThread.Start();

        if (!_mountReady.Wait(TimeSpan.FromSeconds(10)))
        {
            Unmount();
            return Result<string>.Fail("Se agotó el tiempo de montaje de WinFsp. Asegúrate de que WinFsp está instalado.");
        }

        if (_lastError != 0)
        {
            Unmount();
            return Result<string>.Fail($"Falló el montaje de WinFsp (estado=0x{_lastError:X8}).");
        }

        return Result<string>.Ok(mountPoint);
    }

    public Result Unmount()
    {
        try
        {
            _host?.Unmount();
            _dispatchThread?.Join(5000);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Error al desmontar: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Unmount();
        _host?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
