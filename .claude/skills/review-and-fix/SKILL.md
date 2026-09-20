---
name: review-and-fix
description: >
  Workflow completo para revisar y corregir SecureFolder. Usar cuando se pida revisar el proyecto
  o corregir un bug: revisar el estado actual, identificar la causa raíz, aplicar un cambio mínimo
  con tests de regresión, verificar (build + tests + reproducción QA) y registrar el resultado en
  la base de conocimiento (.agent/knowledge/). Triggers: "revisa el proyecto", "corrige este bug",
  "revisa el diff/PR", "arregla X en SecureFolder".
---

# Revisar y corregir SecureFolder

## 0. Regla de oro

Nunca "corregir por corregir": reproducí el bug, encontrá la causa raíz, aplicá el cambio mínimo
y verificá. Cerrá el conocimiento en `.agent/knowledge/` al terminar (incluso si no se resuelve).

## 1. Cargar contexto (siempre primero)

1. Leer `CLAUDE.md` y `AGENTS.md` (comandos, convenciones, gotchas).
2. Leer `.agent/knowledge/INDEX.md` y las entradas referenciadas (bugs abiertos, decisiones).
3. Leer `docs/` según el área: `enumeracion.md` (filesystem), `formato-sfv.md` (vault).
4. `git status` + `git log --oneline -10` para saber qué cambió y qué está sin commitear.

## 2. Revisión (línea base)

- `dotnet build src/SecureFolder.App/SecureFolder.App.csproj -c Release` → **0 warnings/0 errors**.
- `dotnet test src/SecureFolder.Tests/SecureFolder.Tests.csproj -c Release` → **42 deben pasar**.
- Revisar el área reportada con `code-review`/`security-reviewer` y los bugs abiertos conocidos:
  - `FlushDirtyFiles` (0 bytes, pérdida de datos).
  - Montaje/primer unlock (falla silenciosa).
  - Cableado de la UI (crear/desbloquear/bloquear/cambiar/eliminar).
- Anotar hallazgos: severidad + archivo:línea + fix propuesto.

## 3. Corrección

- Cambio mínimo, respetando convenciones (comentarios/mensajes en español, separador único `\`,
  helpers `Norm`/`DirectChildName`, sin duplicar slicing de rutas).
- Todo fix a lógica de rutas/filesystem necesita tests de regresión en
  `src/SecureFolder.Tests/Filesystem/` (tabla con raíz `\` y caminos anidados).
- No agregar comentarios innecesarios. No tocar infraestructura (instalador/docs) salvo que el
  fix lo requiera.

## 4. Verificación

1. Build Release: 0 warnings, 0 errores.
2. Tests: todos verdes (no solo los nuevos).
3. Si el bug es de filesystem/montaje, reproducirlo contra el binario de desarrollo
   `src\SecureFolder.App\bin\Release\net10.0-windows\SecureFolder.App.exe` con los scripts de
   `$env:TEMP\opencode\qa\` (p. ej. `repro_root.ps1` imprime `LIST OK: <archivo>`; `qfrepro.ps1`
   corre el flujo QF completo). Windows PowerShell, lanzar con
   `powershell -NoProfile -ExecutionPolicy Bypass -File <script>`.
4. Si el bug es de clic/UIA: recordar Alt-trick + `GetForegroundWindow` (ventana puede caer en
   el monitor secundario con coords negativas).

## 5. Registrar en memoria

- Crear/actualizar la entrada en `.agent/knowledge/` usando el template del skill
  `knowledge-base-update` (frontmatter type/topic/date/tags + Summary/Context/Finding/Rationale/
  Consequences/References).
- Actualizar `INDEX.md` (agregar fila o marcar resuelto con el commit).
- Bugs resueltos: mover a "Completado" dejando constancia del commit.
- Bugs abiertos sin resolver: dejar hipótesis y siguiente paso.

## 6. Entrega

- Commit: Conventional Commits en español, tipo según el cambio
  (`fix(filesystem): ...`, `feat(ui): ...`, `docs: ...`). Cuerpo con el porqué.
- Push SOLO si el usuario lo pide.
- Reporte final: qué se corrigió (causa raíz), cómo se verificó, qué queda pendiente.