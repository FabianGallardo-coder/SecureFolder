---
type: bug
topic: Con la app elevada (como administrador), el Explorador no ve la unidad montada
date: 2026-09-21
tags: [winfsp, elevation, uac, explorer, mount, defineDosDevice, qa]
---

## Summary

**FIJADO (aviso + guarda).** Si SecureFolder se ejecuta **elevada**, el vault se monta y `Z:\` es
accesible desde procesos elevados, pero el **Explorador (no elevado) muestra "Ubicación no
disponible"** al abrir la carpeta. No es un fallo de montaje ni del cifrado.

## Context

Reproducción del usuario: "cada vez que desbloqueas y queremos abrir el vault sale error no existe
z:/". Diagnóstico (`open-vault.ps1`, `unelev-test.ps1`):

- App **elevada** (`elevated=1`): `Test-Path "Z:\"` → `True` y `Get-ChildItem "Z:\"` lista los
  archivos desde PowerShell, pero el clic en la tarjeta abre una ventana `#32770`
  **"Ubicación no disponible"**.
- App **no elevada** (`elevated=0`, lanzada vía `explorer.exe`): el clic abre `Z:\` sin error
  (`Shell.Application` reporta `Disco local (Z:) -> file:///Z:/`).

## Decision / Finding

**Causa raíz:** WinFsp crea la letra de unidad con `DefineDosDevice`, que registra el mapeo en el
**espacio de nombres de dispositivos de la sesión de logon del proceso**. UAC otorga al token
elevado una sesión de logon distinta a la del shell interactivo. Por eso el Explorador (no elevado)
no ve las unidades montadas por un proceso elevado — y viceversa (mismo motivo por el que las
unidades de red mapeadas no aparecen en un CMD elevado).

El manifiesto de la app es `asInvoker` (`Assets\app.manifest`), así que un usuario que la abre
normalmente **no** tiene el problema; solo aparece si la ejecuta **como administrador** (o si se
lanza desde una shell elevada, como hace el harness de QA).

**Fix (2026-09-21):**
- `MainViewModel`: detecta elevación (`WindowsPrincipal.IsInRole(Administrator)`) al inicio y expone
  `IsElevated` + `ElevationWarning`.
- `MainWindow.xaml`: banner de aviso (fila nueva) visible cuando `IsElevated`.
- `OpenVaultFolder`: si `IsElevated`, no intenta abrir el Explorador; muestra un mensaje claro en
  la barra de estado en lugar del error críptico de Windows.

Verificado: app elevada → banner presente; app no elevada → banner ausente. Build 0/0, tests 45/45.

## Rationale

Se eligió avisar (no auto-relanzar) por ser un cambio mínimo y predecible: no toca el montaje ni
introduce edge cases de re-elevación. La guarda en `OpenVaultFolder` evita la confusión inmediata
al hacer clic.

## Consequences

- **QA con shell elevada:** todos los chequeos de "abrir carpeta" con Explorador deben hacerse con la
  app lanzada **no elevada** (p. ej. `Start-Process explorer.exe "<exe>"`), o la ventana de error
  "Ubicación no disponible" se cuenta como falsa nueva ventana y enmascara el resultado.
- El conteo de ventanas del E2E (step 3) era un **falso positivo**: la ventana de error es
  `CabinetWClass`. Un chequeo robusto debe detectar también el diálogo `#32770`.
- Si en el futuro se quiere soportar "abrir carpeta" con la app elevada, habría que montar la unidad
  en el namespace global (`\GLOBAL??\`) o desacoplar el host de WinFsp del proceso elevado.

## References

- `src\SecureFolder.App\ViewModels\MainViewModel.cs` (`IsRunningElevated`, `IsElevated`, `OpenVaultFolder`)
- `src\SecureFolder.App\MainWindow.xaml` (banner de aviso, `IsElevated` → `BoolToVisibleConverter`)
- `src\SecureFolder.App\Assets\app.manifest` (`requestedExecutionLevel level="asInvoker"`)
- QA: `%TEMP%\opencode\qa\unelev-test.ps1`, `%TEMP%\opencode\qa\check-banner.ps1`, `open-vault.ps1`
