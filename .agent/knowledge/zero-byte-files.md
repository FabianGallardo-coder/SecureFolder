---
type: bug
topic: Los archivos de 0 bytes se pierden al bloquear (pérdida de datos)
date: 2026-09-18
tags: [data-loss, flush, index, empty-file]
---

## Summary

**FIJADO en código; QA Release 45/45 OK. Pendiente solo repro E2E físico.** Los archivos vacíos
(0 bytes) se eliminan del índice al bloquear el vault: `FlushDirtyFiles` hacía
`FileIndex.Remove(rel)` cuando `plaintext.Length == 0`, así que el archivo (y el directorio que
solo lo contiene) desaparecía tras reabrir el vault.

## Context

Detectado en QA funcional (repro de raíz). Un archivo creado vacío en la unidad montada no
sobrevive a bloqueo → desbloqueo. Sospecha original: la tarjeta/vault desaparecía de la UI;
la causa real está en el índice del vault.

## Decision / Finding

En `SecureFolderFileSystem.cs` → `FlushDirtyFiles()`:

```csharp
byte[] plaintext = file.Buffer?.ToArray() ?? Array.Empty<byte>();
if (plaintext.Length == 0)
{
    _vault.FileIndex.Remove(rel);   // ← el 0-byte se pierde
    continue;
}
```

La codificación GCM (nonce 12B + tag 16B + ciphertext vacío = 28B) permitiría persistirlo sin
sobrecarga extra.

## Fix aplicado (2026-09-20)

El índice ahora retiene las entradas de archivos vacíos: `FileEntry` con `DataOffset=-1`
(`Length=0`), cifrando el chunk vacío por el flujo GCM estándar del formato (overhead 28B).
El camino de vacío ya no ejecuta `FileIndex.Remove`. Se agregó test de regresión
`ZeroByteFileTests.ZeroByteFile_ShouldPersistAfterUnmount` (45 tests totales, todos verdes).

## Rationale

(Indeterminado — sin decisión tomada. El comportamiento actual se trata como bug de pérdida de
datos, no como característica.)

## Consequences

- ~~Crear un archivo vacío y bloquearlo = perderlo silenciosamente al reabrir.~~ Corregido.
- El flujo GCM de chunk vacío ya está soportado por el formato (overhead 28B); el formato `.sfv`
  no requirió cambios.
- Pendiente QA E2E físico: crear archivo 0-bytes en `Z:`, bloquear, desbloquear y verificar que persiste.

## References

- `src\SecureFolder.Core\Filesystem\SecureFolderFileSystem.cs:287-292`
- `src\SecureFolder.Tests\Filesystem\ZeroByteFileTests.cs`