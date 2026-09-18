---
type: bug
topic: El primer desbloqueo de la sesión a veces falla en silencio
date: 2026-09-18
tags: [unlock, mount, flaky, qa]
---

## Summary

**ABIERTO.** El primer desbloqueo después de arrancar la app a veces no monta Z: y no muestra
error (falla silenciosa). Los desbloqueos posteriores suelen funcionar. Observado en QA
repetido (sesión A fallaba una vez; la sesión B siempre OK).

## Context

QA automatizado con UIA: se crea un vault, se desbloquea por primera vez y `Test-Path Z:\`
daba `False` sin excepción visible. Correr el flujo de nuevo dentro de la misma sesión
funcionaba.

## Decision / Finding

Sin causa raíz identificada todavía. Hipótesis plausibles (no verificadas):
- Carrera en el montaje WinFsp/`MountEx` al iniciar (permisos/raíz/registro de opciones).
- El diálogo UIA se cierra/valida antes de que el manejador de montaje termine.
- Ventana/foreground: la app abre en el monitor secundario (coords negativas) y el clic del
  botón "Desbloquear" no aterriza (ver variante UIA).

## Rationale

No se profundizó por prioridad. No es pérdida de datos; es disponibilidad momentánea del montaje.

## Consequences

- En QA interactivo, reintentar el desbloqueo una vez dentro de la misma sesión suele funcionar.
- Si reaparece en uso normal, priorizar el diagnóstico (log de montaje + excepción WinFsp).

## References

- Scripts QA en `%TEMP%\opencode\qa\` (`qfrepro.ps1`, `verifyfix.ps1`, `dclk.ps1`)