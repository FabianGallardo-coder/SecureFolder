---
type: todo
topic: Tareas pendientes y bugs abiertos
date: 2026-09-18
tags: [todo, backlog, bugs]
---

## Summary

Estado de las tareas pendientes y bugs abiertos del proyecto. Es la fuente de verdad para los
siguientes ciclos de trabajo.

## Pending tasks (por prioridad)

### Bugs abiertos
1. **Archivos de 0 bytes se pierden al bloquear** — ~~pérdida de datos silenciosa~~ **FIJADO en
   código + test de regresión `ZeroByteFileTests` (2026-09-20)**. Repro E2E físico sobre build dev
   **PASS** (`e2e-full.ps1`, 2026-09-21). Ver [zero-byte-files](./zero-byte-files.md).
2. **Primer desbloqueo de la sesión a veces no monta Z:** — **causa hallada y FIJADA**: faltaba
   el override `Mounted` → el mount se desmontaba solo a los ~10-12 s. Repro E2E físico sobre build
   dev **PASS** (`e2e-full.ps1`, 2026-09-21). Ver [first-unlock-flaky](./first-unlock-flaky.md).

### Funcionalidad sin cablear
3. **"Cambiar contraseña" y "Eliminar vault" sin botón en la UI** — la lógica existe en
   `VaultManager`/`VaultFormat` (`ChangePasswordAsync`), falta el flujo en la UI.
   Prioridad media.

### Verificación pendiente
4. **QA E2E sobre la app instalada** — el instalador `release\SecureFolderSetup.exe` (45,5 MB,
   2026-09-21) se regeneró con los binaries corregidos + WinFsp MSI; falta el repro físico en
   máquina con escritorio (requiere UAC + UI interactiva). El flujo completo ya pasa sobre el build
   dev vía `e2e-full.ps1` (steps 1–9 PASS).
5. **"Bloquear todas"** — `CountToVisibleConverter` arreglado; verificar visualmente con vars
   vaults al mismo tiempo en el build final.

## Completado (2026-09-21)
- **Clic en tarjeta de vault desbloqueado no abría la carpeta** → FIJADO: `ShellExecute` sobre la
  raíz de la unidad WinFsp no abre ventana; ahora se lanza `explorer.exe "{letra}:\"`. QA físico
  PASS. Ver [clic-tarjeta-no-abre-carpeta](./clic-tarjeta-no-abre-carpeta.md).
- **El 2.º desbloqueo fallaba (PasswordBox conservaba la contraseña)** → FIJADO vaciando los
  `PasswordBox` al abrir el diálogo (`ViewModel_PropertyChanged`). QA E2E steps 1–9 PASS, 45/45.
  Ver [passwordbox-no-se-limpia](./passwordbox-no-se-limpia.md).
- **E2E completo automatizado** (`%TEMP%\opencode\qa\e2e-full.ps1`): crear vault → desbloquear →
  abrir Explorador → crear/editar/borrar archivos → bloquear → re-desbloquear → persistencia.
  Resultado **PASS** (2026-09-21). Correcciones de QA: `Get-StatusMessage` ya excluye el header
  `'Mis carpetas seguras'`; el workaround `ESC` tras "Crear" se eliminó (el diálogo cierra solo).
- **App elevada → el Explorador no ve `Z:\`** ("Ubicación no disponible") → FIJADO con banner de
  aviso al inicio (`IsElevated`) + guarda en `OpenVaultFolder`. Causa: sesión de logon distinta por
  UAC + `DefineDosDevice`. **Ojo QA:** probar "abrir carpeta" con la app **no elevada**.
  Ver [app-elevada-unidad-invisible](./app-elevada-unidad-invisible.md).

## Completado (último ciclo, QA Release headless 2026-09-20)
- Bugs #1 (zero-byte) y #2 (first-unlock flaky → timeout de montaje) fijados en código + tests de
  regresión. Suite completa **45/45**, build **0 advertencias / 0 errores**.
- Hallazgo menor: warning `xUnit1013` (Dispose público sin `IDisposable`) en `ZeroByteFileTests`
  → corregido implementando `IDisposable` + `GC.SuppressFinalize`.
- Enumeración de raíz colgada → fijo (ver `enumeration-root-hang.md`).
- Clic físico de "Crear carpeta segura" → fijo (fila del Grid a `Auto`).
- `CountToVisibleConverter` para `int` (Bloquear todas).
- Documentación: README (limitaciones), `docs/`, `AGENTS.md`, esta base de conocimiento.
- Instalador regenerado e instalado (Inno Setup, `release\SecureFolderSetup.exe` 45,5 MB).

## References

- `docs/` (wiki del proyecto), `README.md`
- `.agent\knowledge\INDEX.md`