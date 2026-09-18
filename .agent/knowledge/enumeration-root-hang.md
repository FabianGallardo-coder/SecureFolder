---
type: bug
topic: Enumeración de la raíz de la unidad virtual colgaba (WinFsp)
date: 2026-09-18
tags: [winfsp, filesystem, enumeration, hang, ReadDirectoryEntry]
---

## Summary

Listar la raíz de Z: colgaba indefinidamente. Había **dos bugs** en `ReadDirectoryEntry`:
un off-by-one al recortar el nombre del hijo directo de la raíz, y una violación del
protocolo de enumeración de WinFsp que hacía que el FSD reintentara para siempre
(7.311+ llamadas por IRP).

## Context

Windows `Get-ChildItem Z:\` (y el diálogo "Crear archivo") se quedaba colgado cuando la
raíz tenía contenido. `repro_root.ps1` reproducía el bug creando un archivo en la raíz e
intentando enumerarla.

## Decision / Finding

1. **Off-by-one en el nombre**: la raíz es un separador único (`dirPath == "\\"`, `Length == 1`).
   El código recortaba `childPath[(dirPath.Length + 1)..]` (desde el índice 2), con lo que
   `\archivo_root.txt` devolvía `rchivo_root.txt`. Los directorios no-raíz sí llevan su propio
   `\` final, por eso en "profundidad" funcionaba: el offset correcto es
   `dirPath.Length + (dirPath == "\\" ? 0 : 1)`.

2. **Protocolo de enumeración WinFsp**: el FSD re-servía `.`/`..` y los nombres ya consumidos
   tras un marker no vacío. WinFsp pasa un marker (último nombre entregado) en cada llamada
   siguiente; si el driver vuelve a entregar `.` o nombre <= marker, el FSD reintenta para siempre.
   Reglas del contrato (ver `winfsp/tst/memfs-dotnet/Program.cs`):
   - `marker == ""` → IRP de lectura **fresco**: devolver `.`, `..` y el primer elemento.
   - `marker != ""` → enumeración en curso: **no** re-servir dot entries ni nombres `<= marker`.
   - El `Context` es el cursor por IRP; el framework lo resetea entre IRPs.
   - El FSD reinicia la posición con el marker.

Fixes aplicados en `SecureFolderFileSystem.cs`:
- Helper interno `DirectChildName(dirPath, fullPath)`: hijo directo (name) o `null` (no es hijo
  directo: raíz misma, hermano, o descendiente más profundo). Contiene la lógica de offset.
- `ReadDirectoryEntry` reescrito con el patrón memfs: lista `_enumList` de `(Name, DirPath, File)`,
  `Context` como índice por IRP, `isFresh = string.IsNullOrEmpty(Marker)` (y `"\0"` tratado como
  fresco), dot entries solo en IRP fresco, filtro global `string.Compare(nm, Marker) <= 0` para no
  re-servir nombres ya consumidos.

## Rationale

El patrón stateless (recalcular todo cada llamada) es inválido en WinFsp; el `Context` debe
actuar como cursor, tal como hace memfs de referencia. `DirectChildName` centraliza la aritmética
de caminos en un único helper testeable.

## Consequences

- La enumeración funciona con nombres correctos y sin loop (verificado: `LIST OK: archivo_root.txt`).
- El fix completo requiere **ambas** correcciones: arreglar solo el offset no basta (el loop persiste).
- 10 tests de regresión en `src/SecureFolder.Tests/Filesystem/SecureFolderFileSystemTests.cs`
  (42 en total). Ver `docs/enumeracion.md` para la explicación del protocolo.

## References

- `src\SecureFolder.Core\Filesystem\SecureFolderFileSystem.cs` (`ReadDirectoryEntry`, `DirectChildName`)
- `winfsp/tst/memfs-dotnet/Program.cs` (patrón de referencia)
- Commit `2713f28`