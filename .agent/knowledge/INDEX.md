# Project Knowledge Base

| Date | Type | Topic | Summary |
|------|------|-------|---------|
| 2026-09-18 | bug | [Enumeration root hang](./enumeration-root-hang.md) | Listar la raíz de Z: colgaba; off-by-one + violación del protocolo de enumeración WinFsp. Fijo con cursor de estado por IRP. |
| 2026-09-18 | bug | [Zero-byte files lost](./zero-byte-files.md) | Archivos de 0 bytes se eliminaban del índice al bloquear (pérdida de datos). FIJADO en código + test de regresión; QA Release 45/45. Pendiente solo repro E2E físico. |
| 2026-09-18 | bug | [First unlock flaky (causa hallada)](./first-unlock-flaky.md) | Montaje solo señalaba éxito en el camino de fallo; faltaba override `Mounted` → el vault se desmontaba solo a los ~10-12 s. FIJADO en código + test de regresión; QA Release 45/45. Pendiente solo repro E2E físico. |
| 2026-09-18 | bug | [Read-after-write vacío (Z: contenido '')](./read-after-write-empty.md) | `Read` comparaba offset contra `VaultFilePath.Length` (largo del string de la ruta) y no servía desde `file.Buffer`. SOLUCIONADO en código; QA Release 45/45. Pendiente solo repro E2E físico. |
| 2026-09-18 | decision | [Empty folders (Option B)](./empty-folders-option-b.md) | Las carpetas vacías no sobreviven al bloqueo; se documenta en vez de cambiar el formato. |
| 2026-09-20 | bug | [Card UIA vacía tras desbloquear](./card-uia-vacio.md) | DataItem sin hijos en árbol UIA tras refresh de colección. FIJADO quitando `RefreshVaultList()` redundante de unlock/lock/lockall (INPC basta); tabla visible OK. Incluye fix de StackPanel duplicado en la tarjeta. |
| 2026-09-21 | bug | [Clic en tarjeta no abre carpeta](./clic-tarjeta-no-abre-carpeta.md) | Con el vault desbloqueado, clic en la tarjeta no hacía nada: `ShellExecute` sobre la raíz WinFsp (`Z:\`) no abre ventana. FIJADO lanzando `explorer.exe "Z:\"`. QA físico PASS, 45/45. |
| 2026-09-18 | todo | [Pending tasks](./tareas-pendientes.md) | Cambiar contraseña/eliminar sin UI, QA E2E sobre instalado, "Bloquear todas", bugs abiertos. |