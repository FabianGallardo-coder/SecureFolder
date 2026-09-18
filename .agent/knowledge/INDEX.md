# Project Knowledge Base

| Date | Type | Topic | Summary |
|------|------|-------|---------|
| 2026-09-18 | bug | [Enumeration root hang](./enumeration-root-hang.md) | Listar la raíz de Z: colgaba; off-by-one + violación del protocolo de enumeración WinFsp. Fijo con cursor de estado por IRP. |
| 2026-09-18 | bug | [Zero-byte files lost](./zero-byte-files.md) | Archivos de 0 bytes se eliminan del índice al bloquear (pérdida de datos). ABIERTO. |
| 2026-09-18 | bug | [First unlock flaky](./first-unlock-flaky.md) | El primer desbloqueo de la sesión a veces no monta Z: (falla silenciosa). ABIERTO, sin causa. |
| 2026-09-18 | decision | [Empty folders (Option B)](./empty-folders-option-b.md) | Las carpetas vacías no sobreviven al bloqueo; se documenta en vez de cambiar el formato. |
| 2026-09-18 | todo | [Pending tasks](./tareas-pendientes.md) | Cambiar contraseña/eliminar sin UI, QA E2E sobre instalado, "Bloquear todas", bugs abiertos. |