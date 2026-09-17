using System.Collections.ObjectModel;
using System.IO;
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
    private string _statusMessage = "";

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
            StatusMessage = "Please enter a name.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CreateVaultPassword))
        {
            StatusMessage = "Please enter a password.";
            return;
        }

        if (CreateVaultPassword != CreateVaultPasswordConfirm)
        {
            StatusMessage = "Passwords do not match.";
            return;
        }

        if (CreateVaultPassword.Length < 8)
        {
            StatusMessage = "Password must be at least 8 characters.";
            return;
        }

        StatusMessage = "Creating vault...";

        var result = await _vaultManager.CreateVaultAsync(
            CreateVaultName,
            CreateVaultPath,
            CreateVaultPassword);

        if (result.IsSuccess)
        {
            IsCreateDialogOpen = false;
            RefreshVaultList();
            StatusMessage = $"Vault '{CreateVaultName}' created successfully.";
        }
        else
        {
            StatusMessage = result.Error ?? "Failed to create vault.";
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
            StatusMessage = "Please enter the password.";
            return;
        }

        StatusMessage = "Unlocking vault...";

        var result = await _vaultManager.UnlockVaultAsync(SelectedVault.Id, UnlockPassword);

        if (result.IsSuccess)
        {
            IsUnlockDialogOpen = false;
            RefreshVaultList();
            StatusMessage = $"Vault '{SelectedVault.Name}' unlocked. Drive {SelectedVault.DriveLetter}:\\ is now available.";
        }
        else
        {
            StatusMessage = result.Error ?? "Incorrect password.";
        }
    }

    [RelayCommand]
    private async Task LockVaultAsync(VaultInfo vault)
    {
        StatusMessage = "Locking vault...";

        var result = await _vaultManager.LockVaultAsync(vault.Id);

        if (result.IsSuccess)
        {
            RefreshVaultList();
            StatusMessage = $"Vault '{vault.Name}' locked.";
        }
        else
        {
            StatusMessage = result.Error ?? "Failed to lock vault.";
        }
    }

    [RelayCommand]
    private void OpenVaultFolder(VaultInfo vault)
    {
        if (vault.IsUnlocked && vault.DriveLetter.HasValue)
        {
            string path = $"{vault.DriveLetter}:\\";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
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
            StatusMessage = "Please fill in all fields.";
            return;
        }

        if (ChangePasswordNew != ChangePasswordConfirm)
        {
            StatusMessage = "New passwords do not match.";
            return;
        }

        if (ChangePasswordNew.Length < 8)
        {
            StatusMessage = "New password must be at least 8 characters.";
            return;
        }

        StatusMessage = "Changing password...";

        var result = await _vaultManager.ChangePasswordAsync(
            SelectedVault.Id,
            ChangePasswordCurrent,
            ChangePasswordNew);

        if (result.IsSuccess)
        {
            IsChangePasswordDialogOpen = false;
            StatusMessage = "Password changed successfully.";
        }
        else
        {
            StatusMessage = result.Error ?? "Failed to change password.";
        }
    }

    [RelayCommand]
    private void RemoveVault(VaultInfo vault)
    {
        var result = _vaultManager.RemoveVault(vault.Id);
        if (result.IsSuccess)
        {
            RefreshVaultList();
            StatusMessage = $"Vault '{vault.Name}' removed.";
        }
        else
        {
            StatusMessage = result.Error ?? "Failed to remove vault.";
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
        RefreshVaultList();
        StatusMessage = $"Locked {locked} vault(s).";
    }

    private void RefreshVaultList()
    {
        Vaults = new ObservableCollection<VaultInfo>(_vaultManager.Vaults);
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
                    RefreshVaultList();
                    StatusMessage = $"Vault '{vault.Name}' auto-locked due to inactivity.";
                }
            }
        }
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
