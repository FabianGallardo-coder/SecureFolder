using System.Collections.ObjectModel;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureFolder.Core.Models;
using SecureFolder.Core.Vault;

namespace SecureFolder.App.ViewModels;

/// <summary>
/// Main window ViewModel — manages the list of vaults.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly VaultManager _vaultManager;
    private readonly DispatcherTimer _autoLockTimer;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<VaultInfo> _vaults = [];

    [ObservableProperty]
    private VaultInfo? _selectedVault;

    [ObservableProperty]
    private bool _isCreateDialogOpen;

    [ObservableProperty]
    private bool _isUnlockDialogOpen;

    [ObservableProperty]
    private bool _isChangePasswordDialogOpen;

    [ObservableProperty]
    private bool _isDeleteConfirmationDialogOpen;

    [ObservableProperty]
    private bool _isRenameDialogOpen;

    [ObservableProperty]
    private string _renameVaultName = "";

    [ObservableProperty]
    private bool _deletePhysicalFile = false;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private string _elevationWarning = "";

    [ObservableProperty]
    private string _createVaultName = "";

    [ObservableProperty]
    private string _createVaultPath = "";

    [ObservableProperty]
    private string _createVaultPassword = "";

    [ObservableProperty]
    private string _createVaultPasswordConfirm = "";

    [ObservableProperty]
    private string _unlockPassword = "";

    [ObservableProperty]
    private string _changePasswordCurrent = "";

    [ObservableProperty]
    private string _changePasswordNew = "";

    [ObservableProperty]
    private string _changePasswordConfirm = "";

    public MainViewModel()
    {
        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SecureFolder");
        _vaultManager = new VaultManager(appData);

        // Si la app corre elevada, WinFsp crea la letra de unidad en la sesión de
        // logon del proceso; el Explorador (no elevado) no puede verla y falla con
        // "Ubicación no disponible". Avisamos al inicio.
        IsElevated = IsRunningElevated();
        if (IsElevated)
        {
            ElevationWarning =
                "Ejecutando como administrador: la unidad virtual no será visible en el " +
                "Explorador. Cerrá la aplicación y abrila sin permisos de administrador.";
        }

        RefreshVaultList();

        // Auto-lock timer — checks every 30 seconds
        _autoLockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _autoLockTimer.Tick += AutoLockTimer_Tick;
        _autoLockTimer.Start();
    }

    [RelayCommand]
    private void ShowCreateDialog()
    {
        CreateVaultName = "";
        CreateVaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "SecureFolders");
        CreateVaultPassword = "";
        CreateVaultPasswordConfirm = "";
        IsCreateDialogOpen = true;
    }

    [RelayCommand]
    private void CancelCreate()
    {
        IsCreateDialogOpen = false;
    }

    [RelayCommand]
    private async Task CreateVaultAsync()
    {
        if (string.IsNullOrWhiteSpace(CreateVaultName))
        {
            StatusMessage = "Introduce un nombre.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CreateVaultPassword))
        {
            StatusMessage = "Introduce una contraseña.";
            return;
        }

        if (CreateVaultPassword != CreateVaultPasswordConfirm)
        {
            StatusMessage = "Las contraseñas no coinciden.";
            return;
        }

        if (CreateVaultPassword.Length < 8)
        {
            StatusMessage = "La contraseña debe tener al menos 8 caracteres.";
            return;
        }

        StatusMessage = "Creando carpeta segura...";

        var result = await _vaultManager.CreateVaultAsync(
            CreateVaultName,
            CreateVaultPath,
            CreateVaultPassword);

        if (result.IsSuccess)
        {
            IsCreateDialogOpen = false;
            RefreshVaultList();
            StatusMessage = $"Carpeta segura '{CreateVaultName}' creada correctamente.";
        }
        else
        {
            StatusMessage = result.Error ?? "No se pudo crear la carpeta segura.";
        }
    }

    [RelayCommand]
    private void ShowUnlockDialog(VaultInfo vault)
    {
        SelectedVault = vault;
        UnlockPassword = "";
        IsUnlockDialogOpen = true;
    }

    [RelayCommand]
    private void CancelUnlock()
    {
        IsUnlockDialogOpen = false;
        SelectedVault = null;
    }

    [RelayCommand]
    private async Task UnlockVaultAsync()
    {
        if (SelectedVault is null || string.IsNullOrWhiteSpace(UnlockPassword))
        {
            StatusMessage = "Introduce la contraseña.";
            return;
        }

        StatusMessage = "Desbloqueando carpeta segura...";

        var result = await _vaultManager.UnlockVaultAsync(SelectedVault.Id, UnlockPassword);

        if (result.IsSuccess)
        {
            IsUnlockDialogOpen = false;
            StatusMessage = $"Carpeta segura '{SelectedVault.Name}' desbloqueada. La unidad {SelectedVault.DriveLetter}:\\ ya está disponible.";
        }
        else
        {
            // Vaciar la contraseña para que el reintento no concatene con la anterior.
            UnlockPassword = "";
            StatusMessage = result.Error ?? "Contraseña incorrecta.";
        }
    }

    [RelayCommand]
    private async Task LockVaultAsync(VaultInfo vault)
    {
        StatusMessage = "Bloqueando carpeta segura...";

        var result = await _vaultManager.LockVaultAsync(vault.Id);

        if (result.IsSuccess)
        {
            StatusMessage = $"Carpeta segura '{vault.Name}' bloqueada.";
        }
        else
        {
            StatusMessage = result.Error ?? "No se pudo bloquear la carpeta segura.";
        }
    }

    [RelayCommand]
    private void OpenVaultFolder(VaultInfo vault)
    {
        if (!vault.IsUnlocked || !vault.DriveLetter.HasValue)
            return;

        // Con la app elevada el Explorador (no elevado) no ve la unidad montada.
        if (IsElevated)
        {
            StatusMessage =
                "No se puede abrir la carpeta desde el Explorador mientras la aplicación " +
                "corre como administrador. Reiniciá la aplicación sin permisos de administrador.";
            return;
        }

        string path = $"{vault.DriveLetter}:\\";
        try
        {
            // ShellExecute sobre la raíz de una unidad WinFsp no abre nada (el volumen
            // virtual no expone un ítem de shell asociable). Lanzar explorer.exe con la
            // ruta como argumento sí abre la ventana del explorador.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo abrir la carpeta: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ShowChangePasswordDialog(VaultInfo vault)
    {
        SelectedVault = vault;
        ChangePasswordCurrent = "";
        ChangePasswordNew = "";
        ChangePasswordConfirm = "";
        IsChangePasswordDialogOpen = true;
    }

    [RelayCommand]
    private void CancelChangePassword()
    {
        IsChangePasswordDialogOpen = false;
        SelectedVault = null;
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (SelectedVault is null) return;

        if (string.IsNullOrWhiteSpace(ChangePasswordCurrent) ||
            string.IsNullOrWhiteSpace(ChangePasswordNew))
        {
            StatusMessage = "Rellena todos los campos.";
            return;
        }

        if (ChangePasswordNew != ChangePasswordConfirm)
        {
            StatusMessage = "Las contraseñas nuevas no coinciden.";
            return;
        }

        if (ChangePasswordNew.Length < 8)
        {
            StatusMessage = "La contraseña nueva debe tener al menos 8 caracteres.";
            return;
        }

        StatusMessage = "Cambiando contraseña...";

        var result = await _vaultManager.ChangePasswordAsync(
            SelectedVault.Id,
            ChangePasswordCurrent,
            ChangePasswordNew);

        if (result.IsSuccess)
        {
            IsChangePasswordDialogOpen = false;
            StatusMessage = "Contraseña cambiada correctamente.";
        }
        else
        {
            StatusMessage = result.Error ?? "No se pudo cambiar la contraseña.";
        }
    }

    [RelayCommand]
    private void ShowRenameDialog(VaultInfo vault)
    {
        SelectedVault = vault;
        RenameVaultName = vault.Name;
        IsRenameDialogOpen = true;
    }

    [RelayCommand]
    private void CancelRename()
    {
        IsRenameDialogOpen = false;
        SelectedVault = null;
    }

    [RelayCommand]
    private async Task RenameVaultAsync()
    {
        if (SelectedVault is null || string.IsNullOrWhiteSpace(RenameVaultName))
        {
            StatusMessage = "Introduce un nombre válido.";
            return;
        }

        if (RenameVaultName == SelectedVault.Name)
        {
            IsRenameDialogOpen = false;
            return;
        }

        StatusMessage = "Renombrando carpeta segura...";

        var result = await _vaultManager.RenameVaultAsync(SelectedVault.Id, RenameVaultName);

        if (result.IsSuccess)
        {
            IsRenameDialogOpen = false;
            RefreshVaultList();
            StatusMessage = $"Carpeta segura renombrada a '{RenameVaultName}' correctamente.";
        }
        else
        {
            StatusMessage = result.Error ?? "No se pudo renombrar la carpeta segura.";
        }
    }

    [RelayCommand]
    private void ShowDeleteConfirmation(VaultInfo vault)
    {
        SelectedVault = vault;
        DeletePhysicalFile = false;
        IsDeleteConfirmationDialogOpen = true;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        IsDeleteConfirmationDialogOpen = false;
        SelectedVault = null;
    }

    [RelayCommand]
    private void RemoveVault(VaultInfo vault)
    {
        var result = _vaultManager.RemoveVault(vault.Id, DeletePhysicalFile);
        if (result.IsSuccess)
        {
            IsDeleteConfirmationDialogOpen = false;
            RefreshVaultList();
            string msg = DeletePhysicalFile
                ? $"Carpeta segura '{vault.Name}' eliminada definitivamente del disco."
                : $"Carpeta segura '{vault.Name}' eliminada de la lista.";
            StatusMessage = msg;
        }
        else
        {
            StatusMessage = result.Error ?? "No se pudo eliminar la carpeta segura.";
        }
    }

    [RelayCommand]
    private async Task LockAllVaultsAsync()
    {
        int locked = 0;
        foreach (var vault in Vaults.Where(v => v.IsUnlocked))
        {
            var result = await _vaultManager.LockVaultAsync(vault.Id);
            if (result.IsSuccess) locked++;
        }
        StatusMessage = $"Se bloquearon {locked} carpeta(s) segura(s).";
    }

    private void RefreshVaultList()
    {
        var current = _vaultManager.Vaults;
        Vaults.Clear();
        foreach (var vault in current)
        {
            Vaults.Add(vault);
        }
    }

    private async void AutoLockTimer_Tick(object? sender, EventArgs e)
    {
        foreach (var vault in Vaults.Where(v => v.IsUnlocked && v.AutoLockTimeoutMinutes > 0))
        {
            if (vault.LastUnlockedAt.HasValue)
            {
                var elapsed = DateTimeOffset.UtcNow - vault.LastUnlockedAt.Value;
                if (elapsed.TotalMinutes >= vault.AutoLockTimeoutMinutes)
                {
                    await _vaultManager.LockVaultAsync(vault.Id);
                    StatusMessage = $"Carpeta segura '{vault.Name}' bloqueada automáticamente por inactividad.";
                }
            }
        }
    }

    private static bool IsRunningElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _autoLockTimer.Stop();
            _vaultManager.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
