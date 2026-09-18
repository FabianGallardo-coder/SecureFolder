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
1. **Archivos de 0 bytes se pierden al bloquear** — pérdida de datos silenciosa. Ver
   [zero-byte-files](./zero-byte-files.md). Prioridad alta.
2. **Primer desbloqueo de la sesión a veces no monta Z:** — falla silenciosa, sin causa.
   Ver [first-unlock-flaky](./first-unlock-flaky.md). Prioridad media.

### Funcionalidad sin cablear
3. **"Cambiar contraseña" y "Eliminar vault" sin botón en la UI** — la lógica existe en
   `VaultManager`/`VaultFormat` (`ChangePasswordAsync`), falta el flujo en la UI.
   Prioridad media.

### Verificación pendiente
4. **QA E2E sobre la app instalada** — el instalado (`C:\Program Files\SecureFolder\`) se probó
   solo en smoke; el repro completo corrió sobre `src\SecureFolder.App\bin\Release\...`.
   Prioridad baja.
5. **"Bloquear todas"** — `CountToVisibleConverter` arreglado; verificar visualmente con vars
   vaults al mismo tiempo en el build final.

## Completado (último ciclo, commit `2713f28`)
- Enumeración de raíz colgada → fijo (ver `enumeration-root-hang.md`).
- Clic físico de "Crear carpeta segura" → fijo (fila del Grid a `Auto`).
- `CountToVisibleConverter` para `int` (Bloquear todas).
- Documentación: README (limitaciones), `docs/`, `AGENTS.md`, esta base de conocimiento.
- Instalador regenerado e instalado (Inno Setup, `release\SecureFolderSetup.exe`).

## References

- `docs/` (wiki del proyecto), `README.md`
- `.agent\knowledge\INDEX.md`