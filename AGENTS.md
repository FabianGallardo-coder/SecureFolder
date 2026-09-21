# AGENTS.md

Guía operativa para agentes que trabajen en SecureFolder.

## Proyecto

Carpetas cifradas (`.sfv`) montadas como unidad virtual con WinFsp. WPF (.NET 10) +
`SecureFolder.Core` (cripto + filesystem). No hay `.sln` — compilar el proyecto de App.

## Comandos

```powershell
# Build (Release) — App incluye Core
dotnet build src/SecureFolder.App/SecureFolder.App.csproj -c Release

# Tests (xUnit + FluentAssertions)
dotnet test src/SecureFolder.Tests/SecureFolder.Tests.csproj -c Release

# Publicar self-contained + instalador Inno Setup (requiere ISCC en PATH estándar)
.\build.ps1            # → release\app\… y release\SecureFolderSetup.exe
```

## Convenciones

- **Caminos y formato**: path interno con separador único `\`; raíz = `\`. Nunca duplicar la
  lógica de slicing de caminos fuera de `Norm`/`DirectChildName`.
- **Enumeración WinFsp**: usar el patrón memfs (cursor en `Context`, no recalcular por llamada).
  Ver `docs/enumeracion.md`.
- **Contraseña mínima del vault**: 8 caracteres. Campo "Ubicación" = carpeta donde se guarda
  el `.sfv`, NO la letra de unidad montada.
- **Idioma**: comentarios y mensajes de UI en español; commits siguiendo Conventional Commits
  (`fix(scope): descripción`), repo usa español para descripciones.
- Sin `.sln`; el Core expone internals a Tests vía `InternalsVisibleTo`.

## Entorno Windows (gotchas QA)

- Shell **elevado** = administrador; hay que elevar (`-Verb RunAs`, UAC) para el instalador.
- La app puede abrirse en el **monitor secundario** (coords negativas en `BoundingRectangle`,
  p. ej. `x=-1313`). Para clics físicos con UIA, usar el Alt-trick antes de `SetForegroundWindow`
  y verificar `GetForegroundWindow` == la ventana.
- El botón "Crear carpeta segura" puede quedar oculto por la status bar: la fila 4 del Grid del
  botón debe ser `Auto` (regresión: clic físico sin efecto).
- WinFsp instalado (registro `HKLM\SOFTWARE\WOW6432Node\WinFsp`). El instalador lo incluye
  desde `installer\vendor\winfsp-*.msi`.

## Memoria del agente (LEER al empezar)

- **Base de conocimiento**: `.agent/knowledge/INDEX.md` — bugs abiertos, decisiones y pendientes.
- **Bugs (ver `.agent/knowledge/`)**: 0 bytes y primer unlock están **FIJADOS en código + tests**
  (queda solo repro sobre app instalada). "Eliminar" crasheaba (`RemoveVault` sin
  `CommandParameter`) → FIJADO usando `SelectedVault`.
- **Pendientes/cableados**: QA E2E sobre app **instalada** (requiere UAC); verificación visual de
  "Bloquear todas"; regenerar `release\app` + instalador con el último fix. "Renombrar", "Cambiar
  contraseña" y "Eliminar" ya están cableados en la UI (menú `⚙`, vault bloqueado).
- **Docs**: `docs/` contiene la wiki (arquitectura, formato `.sfv`, enumeración WinFsp).
- Actualizar `.agent/knowledge/` al cerrar o descubrir bugs (regla del skill `knowledge-base-update`).