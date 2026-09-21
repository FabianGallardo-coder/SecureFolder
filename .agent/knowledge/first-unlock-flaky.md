---
type: bug
topic: El primer desbloqueo de la sesión a veces falla en silencio
date: 2026-09-18
tags: [unlock, mount, flaky, qa]
---

## Summary

**CAUSA HALLADA y FIJADA en código; QA Release 45/45 OK. Pendiente solo repro E2E físico.** El
primer desbloqueo después de arrancar la app a veces no monta Z: y no muestra error (falla
silenciosa). Los desbloqueos posteriores suelen funcionar. Observado en QA repetido (sesión A
fallaba una vez; la sesión B siempre OK).

## Context

QA automatizado con UIA: se crea un vault, se desbloquea por primera vez y `Test-Path Z:\`
daba `False` sin excepción visible. Correr el flujo de nuevo dentro de la misma sesión
funcionaba. Síntoma real: el mount arrancaba pero el vault **se desmontaba solo ~10-12 s
después** (timeout del `Wait(10s)` por `_mountReady` en `WinFspMountProvider`).

## Decision / Finding

**Causa raíz:** en el refactor del FS se perdió el override de `Mounted(object)` en
`SecureFolderFileSystem`. El evento interno `MountedSuccessfully` existía pero nunca se
disparaba: el único camino que lo invocaba era el de fallo (`MountedFailed`). Por lo tanto la
señal de éxito del montaje nunca llegaba al `TaskCompletionSource`/`_mountReady` esperado por
`WinFspMountProvider.Mount`, el `Wait(10 s)` expiraba y el provider desmontaba el vuelo.

**Fix (2026-09-20):**
- Restaurado `public override void Mounted(object Host)` en `SecureFolderFileSystem.cs` que
  invoca `MountedSuccessfully?.Invoke()` y llama a `base.Mounted(Host)`.
- `WinFspMountProvider` se suscribe/desuscribe correctamente a `MountedSuccessfully`
  (`Unmount` y `Dispose` eliminan el handler) y setea `_mountReady` con el resultado.
- Test de regresión `FirstUnlockTests` (el primer desbloqueo **desmonta a los ~12 s** en vez de
  quedarse montado), añadido a la suite: 45 tests totales, todos verdes.

## Rationale

(Indeterminado — sin decisión tomada. El comportamiento actual se trata como bug de pérdida de
datos, no como característica.)

## Consequences

- El primer desbloqueo ya no debería desmontar el vuelo a los ~12 s: la señal de montaje exitosa
  se propaga correctamente al provider.
- Repro en instalado: desbloquear y esperar **>12 s** → `Z:` debe seguir montada.
- QA Release: 45/45 tests verdes (incluye `ZeroByteFileTests`, `FirstUnlockTests`,
  read-after-write).

## References

- `src\SecureFolder.Core\Filesystem\SecureFolderFileSystem.cs` (override `Mounted`)
- `src\SecureFolder.Core\Mounting\WinFspMountProvider.cs` (suscripción/desuscripción `MountedSuccessfully`)
- Scripts QA en `%TEMP%\opencode\qa\` (`qfrepro.ps1`, `verifyfix.ps1`, `dclk.ps1`)