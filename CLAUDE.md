# CLAUDE.md

Guía operativa para Claude Code trabajando en **SecureFolder**. Leela completa al empezar;
es la fuente de contexto que te permite revisar y corregir el proyecto sin re-investigar.

## Resumen del proyecto

Carpetas cifradas (`.sfv`) para Windows que se montan como **unidad virtual transparente** con
WinFsp. WPF (.NET 10) + `SecureFolder.Core` (cripto + filesystem). 100% offline.

```
SecureFolder.App          WPF UI + tray icon (net10.0-windows)
│   ├─ MainWindow.xaml        Grid con tarjetas de vaults y botones crear/desbloquear/bloquear
│   ├─ MainViewModel.cs       MVVM (CommunityToolkit.Mvvm)
│   └─ Converters/            CountToVisibleConverter y otros
│
SecureFolder.Core          Criptografía + filesystem virtual
│   ├─ Crypto/                AesGcmEngine, Argon2Kdf, KeyWrapper (RFC 3394), SecureRandom
│   ├─ Filesystem/            SecureFolderFileSystem (WinFsp FileSystemBase)
│   ├─ Vault/                 VaultFormat (.sfv), VaultManager, HmacHelper
│   └─ Models/                VaultInfo, FileEntry, Result
│
SecureFolder.Tests         xUnit + FluentAssertions (42 casos)
```

No hay `.sln` — se compila el proyecto de App directamente.

## Comandos

```powershell
# Build (Release) — App incluye Core
dotnet build src/SecureFolder.App/SecureFolder.App.csproj -c Release

# Tests
dotnet test src/SecureFolder.Tests/SecureFolder.Tests.csproj -c Release

# Publicar self-contained + instalador Inno Setup (iscc.exe en PATH estándar)
.\build.ps1                # → release\app\… y release\SecureFolderSetup.exe
```

Ejecutable de desarrollo: `src\SecureFolder.App\bin\Release\net10.0-windows\SecureFolder.App.exe`
Instalado: `C:\Program Files\SecureFolder\SecureFolder.App.exe` (puede estar desactualizado).

## Arquitectura clave

- **Cifrado**: contraseña → Argon2id → KEK; DEK aleatorio envuelto con AES-256-KW (RFC 3394)
  permite cambiar contraseña sin re-cifrar. Datos en chunks de 64 KB con AES-256-GCM.
  Detalles: `docs/arquitectura.md`, `docs/formato-sfv.md`.
- **Filesystem**: `_files` (ruta → MemFile), `_dirs`, `_allDirs`; paths con separador único `\`,
  raíz = `\`. **Enumeración**: patrón memfs con cursor en `Context` y filtro por marker
  (ver `docs/enumeracion.md` — bug histórico de la raíz).
- **Índice**: `FileIndex` es `Dictionary<relativePath, FileEntry>` serializado a JSON cifrado;
  solo guarda archivos (las carpetas se recrean al montar).
- **VaultManager** expone crear/abrir/bloquear/cambiar contraseña; `FindVaults` escanea carpetas.

## Convenciones

- Comentarios y mensajes de UI en **español**.
- Commits: Conventional Commits en español (`fix(scope): descripción`).
- No duplicar lógica de caminos: usar `Norm` y `DirectChildName` (Core/Filesystem).
- No volver a introducir slices de rutas "inline" fuera de esos helpers.
- El Core expone internals a Tests vía `InternalsVisibleTo` (propiedad en el csproj).
- Contraseña mínima del vault: **8 caracteres**; campo "Ubicación" = carpeta del `.sfv`,
  NO la letra de unidad.

## Entorno Windows (gotchas)

- Shell **elevado** = administrador. El instalador requiere elevación (`-Verb RunAs`, UAC).
- La app puede abrirse en el **monitor secundario** (coords negativas en `BoundingRectangle`,
  p. ej. `x=-1313`). Para clics físicos con UIA: Alt-trick antes de `SetForegroundWindow` y
  verificar con `GetForegroundWindow`.
- "Crear carpeta segura" puede quedar oculto por la status bar: su fila del Grid debe ser
  `Auto` (regresión conocida: clic físico sin efecto).
- WinFsp: registro `HKLM\SOFTWARE\WOW6432Node\WinFsp`; el instalador lo incluye desde
  `installer\vendor\winfsp-*.msi`.

## Bugs conocidos y pendientes

Estado completo con causa y referencias en `.agent/knowledge/INDEX.md`. Resumen:

1. **Archivos de 0 bytes se pierden al bloquear** (FIXED) - Corregido en `FlushDirtyFiles` para preservar archivos vacíos en el índice.
2. **Primer desbloqueo de la sesión a veces no monta Z:** (ABIERTO) - Falla silenciosa, sin causa. VER `.agent/knowledge/first-unlock-flaky.md`.
3. **"Cambiar contraseña" y "Eliminar vault" sin botón en la UI** (FIXED) - Implementados en ViewModel y diseñados para XAML. Soporta borrado físico.
4. Carpetas vacías no persisten (decisión tomada, documentado en README).
5. **Renombrar bóveda (.sfv)** (PENDIENTE) - No existe funcionalidad para cambiar el nombre del archivo físico y la ruta de configuración.

## Revisar y corregir (workflow)

Cuando te pidan revisar el proyecto o corregir un bug, usa la skill
**`review-and-fix`** (`.claude/skills/review-and-fix/SKILL.md`) y seguí estos pasos:

1. **Contexto**: lee `AGENTS.md`, `CLAUDE.md`, `.agent/knowledge/INDEX.md` y `docs/`.
2. **Revisión**: `dotnet build` + `dotnet test` primero; revisa el diff/repo con la skill
   `code-review` (enfocada en `FlushDirtyFiles`, montaje/unlock, cableado de la UI).
3. **Corrección**: cambio mínimo respetando convenciones; agrega tests de regresión.
4. **Verificación**: build Release 0 warnings/errores + tests OK. Para bugs de filesystem,
   reproducir con los scripts QA en `%TEMP%\opencode\qa\` (`repro_root.ps1`, `qfrepro.ps1`).
5. **Memoria**: cerrá o actualizá la entrada en `.agent/knowledge/` (regla de
   `knowledge-base-update`). Nunca dejes el conocimiento desactualizado.
6. **Commit**: Conventional Commits en español. Solo push si lo piden.

## Skills útiles disponibles

- `review-and-fix` (proyecto) — workflow completo per-file de revisar + corregir.
- `code-review`, `security-reviewer`, `test-master` — auditorías específicas.
- `knowledge-base-update` — registrar/actualizar bugs y decisiones en `.agent/knowledge/`.
- `debugging-wizard` / `investigate` — causa raíz de bugs.
- `contextual-commit` / `caveman-commit` — mensajes de commit.

## Documentación

- `docs/` — wiki del proyecto (arquitectura, formato `.sfv`, enumeración WinFsp).
- `AGENTS.md` — guía operativa para agentes de opencode (comandos y gotchas idénticos).
- `.agent/knowledge/` — base de conocimiento: bugs abiertos, decisiones y pendientes.
  Es la fuente de verdad única de estado; actualizala al descubrir o cerrar bugs.