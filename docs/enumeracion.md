# Enumeración de directorios en WinFsp

Cómo funciona la enumeración con WinFsp y por qué colgaba la raíz. Resumen del bug/root-cause:
`.agent/knowledge/enumeration-root-hang.md`.

## El contrato IRP / marker

Cuando Windows enumera un directorio de la unidad virtual, WinFsp emite un IRP
`ReadDirectoryEntry` por "página". Cada llamada siguiente incluye un **marker** con el último
nombre entregado; es la única forma que tiene el FSD de continuar la lectura.

Reglas del driver (`ReadDirectoryEntry`):

| Marker | Significado | Qué debe devolver |
|--------|-------------|-------------------|
| `""` (o `"\0"`) | IRP **fresco** (primera página) | `.`, `..` y el primer elemento |
| nombre `N` | enumeración en curso | el siguiente elemento **después** de `N`. Nunca `.`/`..` |

- El `Context` del IRP es el cursor por operación; el framework lo resetea entre IRPs.
- Si el driver **re-sirve** un nombre `<= marker` (incluidos `.`/`..`), WinFsp reintenta para
  siempre → hang.

## El bug de la raíz

Dos fallas combinadas:

1. **Nombre mal recortado**: la raíz es un separador único (`dirPath == "\\"`, `Length == 1`).
   Al recortar con `dirPath.Length + 1` (=2) se comía el primer carácter: `\archivo_root.txt`
   → `rchivo_root.txt`. En directorios anidados funcionaba porque su ruta incluye el `\` final
   propio del padre.
   Offset correcto: `dirPath.Length + (dirPath == "\\" ? 0 : 1)`.

2. **Re-servir nombres ya entregados**: con código stateless, cada llamada recalculaba `.`+`..`+
   todos los nombres; al no respetar el marker, el ciclo `marker=X → devolver "." → marker="." →
   devolver X...` se repetía indefinidamente (7.311 llamadas observadas).

## La solución (patrón memfs)

Implementación tomada de `winfsp/tst/memfs-dotnet/Program.cs` (las drivers de referencia):

- Lista plana por IRP: `_enumList` de tuplas `(Name, DirPath, File)`, reconstruida al montar.
- `Context` = índice en `_enumList`; se incrementa por elemento servido.
- `isFresh = string.IsNullOrEmpty(Marker)` → dot entries solo en IRP fresco.
- Filtro global `string.Compare(name, Marker, OrdinalIgnoreCase) <= 0 ⇒ skip` (no re-servir).

El slicing correcto quedó aislado en `DirectChildName(dirPath, fullPath)`, que devuelve el hijo
directo (o `null`), con tests de regresión en
`src/SecureFolder.Tests/Filesystem/SecureFolderFileSystemTests.cs`.

## Cómo no volver a caer

- Nunca recalcular la lista completa por llamada *y* devolverla desde el inicio.
- Nunca devolver `.`/`..` cuando el marker no es vacío.
- Mantener la aritmética de rutas en helpers centralizados (`Norm`, `DirectChildName`)
  y cubrir raíz (`\`) y caminos anidados con tests de tabla.