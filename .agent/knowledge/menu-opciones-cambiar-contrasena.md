---
type: bug
topic: Menú de opciones de la tarjeta y diálogo "Cambiar contraseña"
date: 2026-09-21
tags: [ui, wpf, uia, vault, change-password]
---

## Summary

El botón `⚙` de la tarjeta y la operación "Cambiar contraseña" estaban rotos en dos capas.
FIJADO: se añadió el overlay que faltaba y se corrigió la precondición invertida; el `⚙` ahora
abre un `ContextMenu` con las acciones válidas.

## Síntoma

- Con el vault **desbloqueado**, pulsar `⚙` no mostraba nada (diálogo invisible).
- "Cambiar contraseña" no era usable en ningún caso.
- "Eliminar carpeta segura" tenía overlay y comandos pero **ningún disparador** (UI inalcanzable).

## Causa

1. **Faltaba el overlay XAML** de "Cambiar contraseña": `MainViewModel` tenía
   `IsChangePasswordDialogOpen`, `ShowChangePasswordDialog`, `ChangePasswordAsync` y los handlers
   de `PasswordBox` (`CurrentPassword_Changed`, etc.), pero `MainWindow.xaml` no tenía ningún
   diálogo enlazado a `IsChangePasswordDialogOpen` → `IsChangePasswordDialogOpen = true` ponía el
   flag sin mostrar nada.
2. **Condición del `⚙` invertida**: `VaultOptionsButton_Click` ofrecía "Cambiar contraseña" cuando
   el vault estaba **desbloqueado**, pero el Core lo prohíbe en ese estado:
   `VaultManager.ChangePasswordAsync` → `if (info.IsUnlocked) Fail("No se puede cambiar la
   contraseña con la carpeta desbloqueada. Bloquéala primero.")`. Lo mismo en `RenameVaultAsync` y
   `RemoveVault`. Es decir, el menú ofrecía justo la operación que el Core rechaza.
3. **PasswordBox del overlay**: al ser un overlay que persiste en el árbol visual, conservaba el
   texto entre aperturas (mismo bug que `passwordbox-no-se-limpia.md`).

## Fix (2026-09-21)

- `MainWindow.xaml`: `Button.ContextMenu` en `⚙` con `Renombrar` / `Cambiar contraseña` /
  `Eliminar carpeta segura`; overlay "Cambiar contraseña" con 3 `PasswordBox`.
- `MainWindow.xaml.cs`:
  - `VaultOptionsButton_Click`: si `vault.IsUnlocked` → aviso en `StatusMessage` y **no** abre el
    menú; si está bloqueado → abre el `ContextMenu` (`PlacementTarget`, `Placement=Bottom`,
    `DataContext = vault`).
  - `OptionsRename_Click` / `OptionsChangePassword_Click` / `OptionsDelete_Click`: `MenuItem` →
    comando del ViewModel (el `DataContext` se asigna explícitamente al abrir).
  - `ViewModel_PropertyChanged`: limpiar las 3 cajas al abrir (`IsChangePasswordDialogOpen`) y al
    resetear `ChangePasswordCurrent`.
- `MainViewModel.ChangePasswordAsync`: en el fallo se pone `ChangePasswordCurrent = ""` para que el
  reintento no concatene.

## Verificación QA (UIA físico, app NO elevada)

`%TEMP%\opencode\qa\options-menu-test.ps1` + `options-rename-test.ps1`:
- Bloqueado → `⚙` → menú con `Renombrar`, `Cambiar contraseña`, `Eliminar carpeta segura` ✓
- `Eliminar` abre la confirmación (se canceló; no borra) ✓ · `Renombrar` abre su diálogo ✓
- Cambiar contraseña `pass1234`→`pass5678` → "Contraseña cambiada correctamente." → desbloquea con
  `pass5678` ✓ → se restauró a `pass1234` y desbloquea ✓
- Desbloqueado → `⚙` muestra el aviso y **no** abre el menú ✓

## Notas para QA

- **El `⚠` no se testea con `SetValue`**: los `PasswordBox` no exponen `ValuePattern`; usar
  `SetFocus()` + `SendKeys` (seleccionar todo + borrar antes de escribir).
- **`ContextMenu` es una ventana aparte**: no está en el árbol UIA de la ventana; buscar los
  `MenuItem` desde `AutomationElement.RootElement`.
- **Scripts PowerShell 5.1 sin BOM se leen como ANSI**: literales con acentos o glifos (p. ej. `⚙`)
  se corrompen. Los scripts QA deben ser **ASCII-only** (identificar el engranaje por "no es
  Desbloquear/Bloquear" y matchear textos por substrings sin acentos).

## References

- `passwordbox-no-se-limpia.md`, `clic-tarjeta-no-abre-carpeta.md`
- `src/SecureFolder.Core/Vault/VaultManager.cs` (`ChangePasswordAsync`/`RenameVaultAsync`/`RemoveVault`)
