---
type: bug
topic: Los archivos de 0 bytes se pierden al bloquear (pérdida de datos)
date: 2026-09-18
tags: [data-loss, flush, index, empty-file]
---

## Summary

**ABIERTO.** Los archivos vacíos (0 bytes) se eliminan del índice al bloquear el vault:
`FlushDirtyFiles` hace `FileIndex.Remove(rel)` cuando `plaintext.Length == 0`, así que el
archivo (y el directorio que solo lo contiene) desaparece tras reabrir el vault.

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
sobrecarga extra, pero hoy se descarta antes de cifrar.

## Rationale

(Indeterminado — sin decisión tomada. El comportamiento actual se trata como bug de pérdida de
datos, no como característica.)

## Consequences

- Crear un archivo vacío y bloquearlo = perderlo silenciosamente al reabrir.
- Al corregirlo, el flujo GCM de chunk vacío ya está soportado por el formato (overhead 28B);
  el formato `.sfv` no necesita cambios.

## References

- `src\SecureFolder.Core\Filesystem\SecureFolderFileSystem.cs:287-292`