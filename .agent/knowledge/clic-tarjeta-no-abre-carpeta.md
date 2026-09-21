---
type: bug
topic: Clic en tarjeta de vault desbloqueado no abre la carpeta
date: 2026-09-21
tags: [ui, shell, winfsp, mount, explorer]
---

## Summary

**FIJADO.** Con el vault desbloqueado, hacer clic en el cuerpo de la tarjeta no abría nada: el
`Process.Start` con `UseShellExecute=true` sobre la raíz de la unidad WinFsp (`Z:\`) no lanza
ninguna ventana. La tarjeta **bloqueada** sí abría el diálogo de desbloqueo (correcto).

## Context

Síntoma del usuario: "si presiono un vault en la app, no abre nada". QA físico (clic con
`SendInput`, UIA para localizar la tarjeta) reprodujo:
- Clic en tarjeta **bloqueada** → aparece "Desbloquear carpeta" (`unlock_dialog_open=True`). OK.
- Clic en tarjeta **desbloqueada** → ninguna ventana nueva del Explorador.

Pruebas comparativas con la unidad `Z:` montada (EnumWindows, clase `CabinetWClass`):

| Prueba | Lanzamiento | ¿Abre ventana? |
|--------|-------------|----------------|
| A | `Start-Process C:\Users\Fabian` | Sí |
| B | `Start-Process "Z:\"` (ShellExecute) | **No** |
| C | `explorer.exe "Z:\"` | Sí |
| D | clic físico en tarjeta desbloqueada (código actual) | **No** |

## Decision / Finding

**Causa raíz:** `ShellExecute` sobre la raíz de una unidad WinFsp no se resuelve como ítem de
shell (el volumen virtual no expone un namespace asociable), así que
`Process.Start(new ProcessStartInfo { FileName = "Z:\\", UseShellExecute = true })` no produce
efecto. Lanzar `explorer.exe` con la ruta como argumento sí abre la ventana.

**Fix (2026-09-21)** en `MainViewModel.OpenVaultFolder`:
- Lanzar `explorer.exe` pasando `"{letra}:\\"` en `Arguments` (en vez de usar la ruta como
  `FileName`).
- Envolver en `try/catch` y reflejar el error en `StatusMessage`.

Verificado con QA físico: clic en tarjeta desbloqueada → nueva ventana `CabinetWClass`
(`RESULT=PASS new_explorer_windows=1`). Suite 45/45, build 0/0.

## Rationale

`explorer.exe <ruta>` delega en el proceso shell real, que sí entiende la ruta de la unidad
montada; `ShellExecute` directo depende del namespace de shell del volumen, que WinFsp no
registra igual que un disco físico.

## Consequences

- Clic en tarjeta desbloqueada abre el Explorador en la unidad del vault.
- Clic en tarjeta bloqueada sigue mostrando el diálogo de desbloqueo.
- Nota menor detectada (no corregida): en `VaultManager.UnlockVaultAsync` el bloque
  `IsUnlocked/DriveLetter/LastUnlockedAt/_mountedVaults` está **duplicado** (líneas ~141-150);
  es idempotente pero conviene limpiarlo.

## References

- `src\SecureFolder.App\ViewModels\MainViewModel.cs` (`OpenVaultFolder`, ~L213)
- `src\SecureFolder.App\MainWindow.xaml.cs` (`VaultCard_Click`, L31)
- QA: `%TEMP%\opencode\qa\clickcard{,2,3,4}.ps1`
