using System.ComponentModel;
using FluentAssertions;
using SecureFolder.Core.Models;

namespace SecureFolder.Tests.Models;

public class VaultInfoTests
{
    private static VaultInfo NewVault() => new()
    {
        Id = "id",
        Name = "Vault",
        VaultFilePath = @"C:\x\v.sfv",
    };

    [Fact]
    public void IsUnlocked_RaisesPropertyChanged_ForStateAndComputedText()
    {
        var vault = NewVault();
        var raised = new List<string>();
        ((INotifyPropertyChanged)vault).PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vault.IsUnlocked = true;

        raised.Should().Contain(nameof(VaultInfo.IsUnlocked));
        raised.Should().Contain(nameof(VaultInfo.StatusText));
        raised.Should().Contain(nameof(VaultInfo.IconEmoji));
    }

    [Fact]
    public void StatusText_ReflectsDriveLetter_AndRaisesOnChange()
    {
        var vault = NewVault();
        var raised = new List<string>();
        ((INotifyPropertyChanged)vault).PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vault.IsUnlocked = true;
        vault.DriveLetter = 'Z';

        vault.StatusText.Should().Be(@"Desbloqueada (Z:\)");
        raised.Should().Contain(nameof(VaultInfo.StatusText));
    }

    [Fact]
    public void SettingSameValue_DoesNotRaise()
    {
        var vault = NewVault();
        var count = 0;
        ((INotifyPropertyChanged)vault).PropertyChanged += (_, _) => count++;

        vault.IsUnlocked = false;

        count.Should().Be(0);
    }
}
