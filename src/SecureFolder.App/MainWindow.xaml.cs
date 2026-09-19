using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SecureFolder.App.ViewModels;
using SecureFolder.Core.Models;

namespace SecureFolder.App;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Minimize to tray instead of closing
        e.Cancel = true;
        Hide();
    }

    private void VaultCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is VaultInfo vault)
        {
            if (vault.IsUnlocked)
                ViewModel.OpenVaultFolderCommand.Execute(vault);
            else
                ViewModel.ShowUnlockDialogCommand.Execute(vault);
        }
    }

    private void VaultActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is VaultInfo vault)
        {
            if (vault.IsUnlocked)
                ViewModel.LockVaultCommand.Execute(vault);
            else
                ViewModel.ShowUnlockDialogCommand.Execute(vault);
        }
    }

    private void Overlay_Click(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.IsCreateDialogOpen) ViewModel.CancelCreateCommand.Execute(null);
        if (ViewModel.IsUnlockDialogOpen) ViewModel.CancelUnlockCommand.Execute(null);
        if (ViewModel.IsChangePasswordDialogOpen) ViewModel.CancelChangePasswordCommand.Execute(null);
    }

    // Password box bindings (PasswordBox doesn't support MVVM binding directly)
    private void CreatePassword_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb) ViewModel.CreateVaultPassword = pb.Password;
    }

    private void CreatePasswordConfirm_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb) ViewModel.CreateVaultPasswordConfirm = pb.Password;
    }

    private void UnlockPassword_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb) ViewModel.UnlockPassword = pb.Password;
    }

    private void CurrentPassword_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb) ViewModel.ChangePasswordCurrent = pb.Password;
    }

    private void NewPassword_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb) ViewModel.ChangePasswordNew = pb.Password;
    }

    private void NewPasswordConfirm_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb) ViewModel.ChangePasswordConfirm = pb.Password;
    }

    private void VaultOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is VaultInfo vault)
        {
            if (vault.IsUnlocked)
            {
                ViewModel.ShowChangePasswordDialogCommand.Execute(vault);
            }
            else
            {
                ViewModel.ShowRenameDialogCommand.Execute(vault);
            }
        }
    }
}
