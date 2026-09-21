---
type: bug
topic: PasswordBox conserva la contraseña al reabrir un diálogo (falla el 2.º desbloqueo)
date: 2026-09-21
tags: [ui, wpf, passwordbox, mvvm, unlock, e2e]
---

## Summary

**FIJADO.** El segundo desbloqueo de un vault (bloquear → volver a desbloquear) fallaba: el
diálogo se abría, se escribía la contraseña correcta y no montaba la unidad. La causa no era el
cifrado ni WinFsp: los `PasswordBox` de WPF **no se limpian al cerrar/reabrir** el diálogo.

## Context

Síntoma reproducido en QA E2E (`e2e-full.ps1`, step 8 "Unlock again"):

1. Primer desbloqueo: la caja `UnlockPasswordBox` está vacía → se escribe `pass1234` → monta `Z:`. ✔
2. Se bloquea el vault.
3. Segundo desbloqueo: el diálogo se reabre, se escribe `pass1234` → **no monta** y el diálogo se
   queda abierto. ✘

La caja seguía conteniendo `pass1234` del primer desbloqueo, así que al reescribir quedaba
`pass1234pass1234` → contraseña incorrecta. El error no se veía porque `Get-StatusMessage` filtraba
`'Mis carpetas'` pero el header real es `'Mis carpetas seguras'`, y el mensaje de error quedaba
enmascarado.

## Decision / Finding

**Causa raíz:** `PasswordBox.Password` no es una `DependencyProperty` y no se enlaza en MVVM. El
código va en una sola dirección (`PasswordChanged` → ViewModel, ver `MainWindow.xaml.cs`), pero el
control **nunca se vacía**. Los resets que hace el ViewModel (`ShowCreateDialog`,
`ShowUnlockDialog`, `ShowChangePasswordDialog`) ponen sus strings a `""`, lo que **no** afecta al
control. Al reabrir el diálogo, el `PasswordBox` conserva el texto anterior.

**Fix (2026-09-21)** en `MainWindow.xaml.cs`:
- Suscribirse a `ViewModel.PropertyChanged` en el constructor.
- Al pasar a `true`:
  - `IsCreateDialogOpen` → `CreatePasswordBox.Clear()` + `CreatePasswordConfirmBox.Clear()`
  - `IsUnlockDialogOpen` → `UnlockPasswordBox.Clear()`

**Ampliación (2026-09-21):** tras un desbloqueo **fallido** el diálogo queda abierto y la caja
conservaba la contraseña, así que reintentar sin borrar concatenaba (`incorrecta` + `pass1234`).
Se limpia también cuando el ViewModel resetea `UnlockPassword` (caso
`nameof(MainViewModel.UnlockPassword) when string.IsNullOrEmpty(...)`) y `UnlockVaultAsync` pone
`UnlockPassword = ""` en el camino de fallo. Verificado: intento con contraseña incorrecta →
reintento correcto **sin** limpiar el campo → desbloquea.

Verificado con QA E2E físico: **steps 1–9 PASS**, incluida la persistencia (archivos sobreviven
lock/re-unlock). Suite 45/45, build 0/0.

## Rationale

Limpiar el control cuando el diálogo se abre sincroniza el estado de la vista con el reset que ya
hace el ViewModel. Se eligió el code-behind (no un `Behavior`/`IsVisibleChanged`) por ser el punto
donde ya viven los handlers de `PasswordBox` en este proyecto.

## Consequences

- Re-desbloquear tras bloquear funciona y conserva los archivos.
- Crear un segundo vault ya no arrastra la contraseña anterior en los campos.
- El diálogo de "Cambiar contraseña" no existe aún en XAML (solo handlers muertos); si se añade,
  aplicar el mismo patrón.
- Bug menor no relacionado, ya documentado: bloque duplicado en `VaultManager.UnlockVaultAsync`
  (~L141-150), idempotente.

## References

- `src\SecureFolder.App\MainWindow.xaml.cs` (`ViewModel_PropertyChanged`, constructor)
- `src\SecureFolder.App\ViewModels\MainViewModel.cs` (`ShowCreateDialog` L93, `ShowUnlockDialog` L157)
- `src\SecureFolder.App\MainWindow.xaml` (`CreatePasswordBox` L246, `UnlockPasswordBox` L284)
- QA: `%TEMP%\opencode\qa\e2e-full.ps1`, `%TEMP%\opencode\qa\qa-helpers.ps1` (`Get-StatusMessage`)
