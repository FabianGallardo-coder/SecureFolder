---
type: decision
topic: Las carpetas vacías no sobreviven al bloqueo (Opción B)
date: 2026-09-18
tags: [decision, empty-folders, index, format]
---

## Summary

Decisión tomada: **no** cambiar el formato del índice para persistir carpetas vacías. Se
documenta la limitación en el README (sección "Limitaciones conocidas") y se mantiene el
formato `.sfv` actual.

## Context

Al probar el vault se observó que una carpeta creada vacía en Z: desaparece tras
bloquear/desbloquear. El índice solo almacena archivos (`FileIndex` es un diccionario
`rel → FileEntry`); las carpetas se recrean al volcar los archivos y por eso solo persisten
aquellas con contenido.

## Decision / Finding

**Opción B (adoptada)**: documentar la limitación y mantener el formato. Alternativas
rechazadas:
- **Opción A**: incluir carpetas vacías en el índice (cambio de formato + riesgo de compatibilidad).
- No hay opción "intermedia sin dolor" que valga el costo actual.

## Rationale

El índice `rel → FileEntry` no distingue archivos/carpetas; persistir carpetas vacías exige un
cambio de esquema (p. ej. una lista separada de dirs o una entrada de directorio), sin beneficio
crítico para el caso de uso actual.

## Consequences

- README documenta el comportamiento.
- Si en el futuro se necesita persistir carpetas vacías, habrá que versionar el índice
  (el formato ya guarda `version` en el header y rechaza versiones superiores).

## References

- `README.md` → "Limitaciones conocidas"
- `src\SecureFolder.Core\Vault\VaultManager.cs` (`FileIndex`)