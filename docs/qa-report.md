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

**Todavía pendiente (requiere UAC + sesión interactiva):**
- Instalar `release\SecureFolderSetup.exe` y repetir el ciclo completo sobre la app **instalada**.
- Regenerar `release\app` + instalador con el fix de `RemoveVault` (ver sección 6) y actualizar los hashes.

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

**Artefactos Release del ciclo (2026-09-21 13:01–13:02):**

> Nota: **no incluyen** el fix de `RemoveVault` (2026-09-21); se regeneran en el próximo ciclo y se
> actualizan los hashes de esta tabla.

| Archivo | Tamaño | Fecha | SHA-256 |
|---|---|---|---|
| `release\app\SecureFolder.App.exe` | 0,41 MB | 2026-09-21 13:01 | `93743465E2D17D36A7FF2CD3970809B808D5B43036F92CB1B2E69530B1089044` |
| `release\app\SecureFolder.Core.dll` | 0,06 MB | 2026-09-21 13:01 | `444BEB1C897DE481306E0767201F62197B65E7DBCD055FDDB6AD47360B024E0B` |
| `release\SecureFolderSetup.exe` | 47,7 MB | 2026-09-21 13:02 | `7E8B9AFF9B247E31B99F186CBEEC33B37979F4C19CC87E6EEAFDB7D816A5D495` |
| `installer\vendor\winfsp-2.2.26215.msi` | 2,11 MB | 2026-09-17 10:34 | `2ECB5C89405488A95BBD8A01875E02C48534FD37BBDFD84488F7590464D65944` |
