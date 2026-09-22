# Guía de Pruebas y QA - SecureFolder

Este documento describe el plan de pruebas ejecutado para validar la estabilidad y seguridad de la versión 1.0.0.

## 1. Matriz de Pruebas Funcionales

### Gestión de Bóvedas
- [x] **Creación**: Bóvedas con contraseñas válidas ($\ge 8$ caracteres) y bloqueo de contraseñas cortas.
- [x] **Acceso**: Desbloqueo con contraseña correcta y rechazo con contraseña incorrecta.
- [x] **Ciclo de Vida**: Bloqueo manual y bloqueo automático por inactividad.
- [x] **Mantenimiento**: Renombrado físico/lógico y cambio de contraseña (sin re-cifrado).
- [x] **Eliminación**: Borrado lógico (lista) y borrado físico (archivo `.sfv`).

### Operaciones de Archivos (Lazy Loading)
- [x] **Archivos Vacíos**: Creación y persistencia de archivos de 0 bytes (Bug #1 corregido).
- [x] **Lectura Fragmentada**: Lecturas que cruzan la frontera de los chunks de 64KB.
- [x] **Rendimiento RAM**: Lectura de archivos $> 1\text{GB}$ sin picos de memoria.
- [x] **Escritura**: Actualizaciones de datos en offsets no alineados y extensión de tamaño de archivo.
- [x] **Limpieza**: Eliminación de archivos y carpetas anidadas.

## 2. Pruebas de Seguridad y Robustez

### Criptografía
- [x] **Integridad de Datos**: Modificación manual de bytes en el `.sfv` $\rightarrow$ Verificación de fallo en el tag GCM.
- [x] **Autenticación de Cabecera**: Modificación del header $\rightarrow$ Verificación de fallo en HMAC-SHA256.
- [x] **Protección de Memoria**: Verificación de `ZeroMemory` en llaves KEK/DEK al hacer Dispose.

### Casos Borde
- [x] **Nombres**: Soporte de emojis, espacios y caracteres especiales en rutas.
- [x] **Estructura**: Directorios anidados a profundidad.
- [x] **Seguridad de Rutas**: Prevención de *Path Traversal* (`..\`).

## 3. Verificación de Entorno
- [x] **WinFsp**: Montaje exitoso en diferentes letras de unidad.
- [x] **Privilegios**: Funcionamiento correcto bajo modo administrador.

## 4. Resultados Finales
- **Tests Unitarios**: 45/45 superados.
- **Build Release**: 0 errores, 0 advertencias.
- **Estabilidad**: Sin crashes reportados durante el ciclo de montaje/desmontaje.

## 5. Ejecución de QA (ciclo Release desde cero — 2026-09-20)

**A1. Rebuild Release (clean → build)**
- `dotnet clean` + `dotnet build SecureFolder.App -c Release`: **0 advertencias / 0 errores**.

**A2. Suite de pruebas (xUnit + FluentAssertions, Release)**
- **45/45 correctos, 0 errores, 0 omitidos**, duración 1 s.
- Hallazgo: `public void Dispose` en `ZeroByteFileTests` sin implementar `IDisposable` generaba **warning xUnit1013** → corregido implementando `IDisposable` + `GC.SuppressFinalize`. Rebuild ahora **0/0**.

**A3. Publish self-contained + instalador (ISCC / Inno Setup 6, por-usuario)**
- `dotnet publish SecureFolder.App -c Release -r win-x64 --self-contained true -o release\app` → **141,2 MB en 409 archivos**.
- `ISCC.exe installer\SecureFolder.iss` → compilación correcta.

**A4. Artefactos verificados (SHA-256)**

| Archivo | Tamaño | Fecha | SHA-256 |
|---|---|---|---|
| `release\app\SecureFolder.App.exe` | 0,41 MB | 2026-09-20 11:47 | `1173569F029008F9AA02ACEF5C00E58701ED44905C0381A7AE36667BCDAD4771` |
| `release\app\SecureFolder.Core.dll` | 0,06 MB | 2026-09-20 11:45 | `B357D05CA5986452AA3777A847C560D0A9F4FC3AF8AB6C90979A321AFED65A02` |
| `release\SecureFolderSetup.exe` | **45,5 MB** | 2026-09-20 11:47 | `055DA92B88FBA5EAAD823D65F8EBB8CFF67C42DB35169E0A44B1CA9EF3EF9835` |
| `installer\vendor\winfsp-2.2.26215.msi` | 2,11 MB | 2026-09-17 10:34 | `2ECB5C89405488A95BBD8A01875E02C48534FD37BBDFD84488F7590464D65944` |

**Pendiente (QA E2E físico, requiere UAC + sesión interactiva, no headless):**
- Instalar `release\SecureFolderSetup.exe`.
- Verificar montaje de `Z:` que persiste **>12 s** (regresión del override `Mounted`).
- `Get-Content Z:\...` tras escribir (regresión de read-after-write/serve desde buffer).
- Persistencia de archivos de **0 bytes** tras bloquear/desbloquear.

**Verificado luego (2026-09-21, sobre build dev y binario publicado NO elevado):**
- Montaje de `Z:` que persiste **>12 s** — PASS (`e2e-full.ps1`, steps 1–9).
- Lectura tras escritura (read-after-write) — PASS.
- Persistencia de archivos de **0 bytes** tras bloquear/desbloquear — PASS.
- Desbloqueo + abrir carpeta desde el binario publicado, app NO elevada — PASS.

**Hecho luego (2026-09-22, ver sección 7):** instalado el setup regenerado y repetido el ciclo
completo sobre la app **instalada** (no elevada, sesión interactiva).

## 6. Ciclo QA UI (menú ⚙) y code review (2026-09-21)

**C6. Rebuild Release + suite**: `dotnet build` → **0 advertencias / 0 errores**; xUnit → **45/45**.

**C7. QA funcional (UIA físico, scripts ASCII-only en `%TEMP%\opencode\qa\`):**
- `options-menu-test.ps1`: botón `⚙` abre menú con Renombrar / Cambiar contraseña / Eliminar solo con
  el vault bloqueado; cambio de contraseña `pass1234`→`pass5678`→`pass1234` desbloqueando en cada
  paso; con el vault desbloqueado el `⚙` avisa y **no** abre el menú — PASS.
- `delete-e2e.ps1` (tras fix): crea `QADel*` → `⚙` → Eliminar → confirmar → tarjeta fuera de lista,
  proceso vivo, `.sfv` conservado en disco (borrado lógico) — PASS.
- Binario publicado (`release\app`) NO elevado: desbloqueo + `Explorer Z:\` abre la carpeta — PASS.

**C8. Code review del ciclo (rango `8a9bfcb..HEAD`):**
- Hallazgo crítico: `RemoveVault` recibía `null` — el botón del diálogo enlazaba `RemoveVaultCommand`
  **sin `CommandParameter`** y el comando usaba su parámetro (`vault.Id`) en vez de `SelectedVault`,
  a diferencia de `RenameVaultAsync`/`ChangePasswordAsync`. Al confirmar la eliminación se producía
  un NRE y, sin `DispatcherUnhandledException` en `App.xaml.cs`, la app crasheaba. **FIJADO** en
  `2026-09-21` (usar `SelectedVault`) + `Overlay_Click` ahora también cierra Renombrar/Eliminar.
  Ver `.agent/knowledge/menu-opciones-cambiar-contrasena.md`.
- Pendientes menores (info): tests UI no versionados (solo `%TEMP%`), desuscripción de
  `PropertyChanged` en `MainWindow`, contraste del banner de elevación bajo WCAG AA (≈3.4:1 a 11px).

**Artefactos Release (regenerados 2026-09-21 17:09–17:10 con el fix de `RemoveVault` via `build.ps1`):**

| Archivo | Tamaño | Fecha | SHA-256 |
|---|---|---|---|
| `release\app\SecureFolder.App.exe` | 0,41 MB | 2026-09-21 17:09 | `9B7A6E9537C931CC0235CA8FD6D0C43437A885FEAB6ABAD1E22857867AA4D5F4` |
| `release\app\SecureFolder.Core.dll` | 0,06 MB | 2026-09-21 17:09 | `3E54E2EE878A4FCBEE659EFA59AD8A9FD72D017C96B259B89CB76497E6B4D90D` |
| `release\SecureFolderSetup.exe` | 45,5 MB | 2026-09-21 17:10 | `3B65EA2F73920C03C9EAECB0C7A34B0E25A01EC42A6BD8BAB9A32274E6E5B62B` |
| `installer\vendor\winfsp-2.2.26215.msi` | 2,11 MB | 2026-09-17 10:34 | `2ECB5C89405488A95BBD8A01875E02C48534FD37BBDFD84488F7590464D65944` |

## 7. QA E2E sobre la app INSTALADA (2026-09-22)

**Instalación**: `release\SecureFolderSetup.exe` (17:10) instalado con `/VERYSILENT` desde shell
elevado → **exit 0**; WinFsp ya presente (`HKLM\SOFTWARE\WOW6432Node\WinFsp`). App en
`C:\Program Files\SecureFolder\SecureFolder.App.exe`.

**Metodología**: la app se lanzó **no elevada** (integridad media) dentro de la sesión interactiva
(sesión 3). Nota: el montaje WinFsp es **por-sesión**; el shell elevado usado para QA corre en otra
sesión, por lo que el ciclo se ejecutó vía tarea programada `schtasks /rl LIMITED` (mismo usuario).
Verificación de elevación por **token** (`OpenProcessToken`+`TokenElevation`, esperado `False`).

**Ciclo completo (repetido 3× sobre la app instalada NO elevada) — PASS:**
- Crear vault (`InstQA*`) y desbloquear → `Z:` montado (el status textual confirma: "La unidad
  `Z:\` ya está disponible").
- Write + read-back en `Z:` → contenido íntegro (regresión read-after-write).
- Archivo de **0 bytes** creado y persistente tras bloquear/re-desbloquear (regresión Bug #1).
- `explorer` se abre desde el vault desbloqueado con la app **no elevada** (1 ventana CabinetWClass)
  — sin el banner/guard de elevación (regresión app-elevada/unidad-invisible).
- Bloquear → `Z:` desmontado.
- Re-desbloquear → `Z:` vuelve y **persiste >15 s** (regresión del override `Mounted`, >12 s).
- 2.º vault + desbloqueo en paralelo → ambos presentan estado correcto.

**Notas de limitation del harness**: la automatización UIA vía tarea programada resultó
**intermitente** (flaps de `Get-CardByName`/`Gear` durante refrescos de la lista con varias tarjetas),
por lo que "Bloquear todas" (efecto) y "Eliminar por UI" sobre la **app instalada** quedan como
**verificación manual/visual pendiente** — su lógica ya está cubierta en el binario `release\app`
(`delete-e2e.ps1` PASS, sección 6) y el botón "Bloquear todas" está presente en la UI instalada
(probe UIA). No se considera un bug de la app: el ciclo completo pasó 3 veces.

**Limpieza**: vaults `InstQA*`/`BlkQA*` de QA eliminados de `%LOCALAPPDATA%\SecureFolder\vaults.json`
(quedó `TestE2E`) y sus `.sfv` eliminados de `Documents\SecureFolders`.
