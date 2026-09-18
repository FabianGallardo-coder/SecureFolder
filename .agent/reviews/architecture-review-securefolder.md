# Architecture Review — SecureFolder v1.0.0

- **Fecha:** 2026-09-17
- **Alcance:** revisión de arquitectura **post-implementación** (no existía `implementation_plan.md` al momento de la revisión).
- **Commit revisado:** `41cab6c` (rama `master`, PR #1).
- **Método:** lectura del código implementado (App WPF, Core, filesystem WinFsp, formato de vault, CI, instalador) + verificación empírica (tests unitarios, harness de integración, Event Log de Windows).
- **Veredicto:** ⚠️ **Revisions required** — 4 blockers (1 ya resuelto en esta iteración), 8 warnings, 6 notes.

> Nota de seguimiento: durante la revisión se reprodujo un **crash real** de la GUI (ver B4). Estaba clasificado inicialmente como *note*; se promovió a **blocker** y **ya fue corregido y verificado** en `src/SecureFolder.App/App.xaml.cs`.

---

## 🔴 Blockers

### B4 — Crash de instancia única al relanzar la app (RESUELTO)
- **Evidencia:** Event Log `Application`, `.NET Runtime` ID 1026:
  ```
  System.ApplicationException: Object synchronization method was called
  from an unsynchronized block of code.
     at System.Threading.Mutex.ReleaseMutex()
     at SecureFolder.App.App.OnExit(...) App.xaml.cs:line 86
  ```
- **Causa raíz:** al abrir una segunda instancia, `OnStartup` crea el mutex con `new Mutex(true, name, out createdNew)`. Como ya existe, `createdNew == false` y **esa instancia no adquiere la propiedad del mutex**, pero `Shutdown()` disparaba `OnExit`, que llamaba `_mutex.ReleaseMutex()` sin condición → `ApplicationException` no controlada → el proceso crashea. Efecto percibido por el usuario: "la ventana se cerró y no volvió a abrir" (la primera instancia quedaba viva pero oculta en la bandeja; los relanzamientos solo crasheaban).
- **Fix aplicado:** `App.xaml.cs`
  - Bandera `_isFirstInstance`; `ReleaseMutex()` solo si esta instancia es dueña, envuelto en `try/catch (ApplicationException)`.
  - Eliminada la doble liberación en `TrayMenu_Exit_Click` (ahora solo llama `Shutdown()`; la limpieza ocurre una vez en `OnExit`).
  - Segunda instancia ahora **señaliza** a la primera vía `EventWaitHandle` (`SecureFolder_ShowWindow_v1`) y sale con código 0; la primera instancia **restaura y activa su ventana**. Esto resuelve el "no se vuelve a abrir".
- **Verificación:** instancia 1 viva; instancias 2 y 3 salen con código 0; **sin nuevos eventos de crash** en Event Log; harness de integración ALL PASS.

### B1 — CI nunca ejecuta
- `.github/workflows/ci.yml:4` se dispara en `push` a `[main, develop]`; `:6` en PR a `[main]`.
- Las ramas reales del repo son `Master` (default) y `master`. Resultado: **PR #1 no tiene checks**.

### B2 — Persistencia de vault no atómica (riesgo de pérdida total)
- `src/SecureFolder.Core/Filesystem/SecureFolderFileSystem.cs:331` — `FlushDirtyFiles` escribe con `File.WriteAllBytes` sobre el `.sfv` existente.
- `src/SecureFolder.Core/Vault/VaultFormat.cs:286-299` — `ChangePasswordAsync` reescribe el vault completo.
- `src/SecureFolder.Core/Vault/VaultFormat.cs` — `CreateVaultAsync` escribe directamente el archivo final.
- No hay escritura a temporal + `File.Replace`/`rename` atómico ni copia `.bak`. Un corte a mitad de escritura deja el vault corrupto e irrecuperable.

### B3 — El flush carga el vault completo en memoria
- `SecureFolderFileSystem.cs:236` — `ReadAllBytes` del `.sfv` completo en `LoadFileData`; `:331` — `ms.ToArray()` construye una segunda copia completa.
- Pico de memoria ~3-4× el tamaño del vault y bloqueo del hilo durante todo el volcado. Vaults ≥ 1-2 GB quedan imprácticos/no viables.

---

## 🟡 Warnings

### W1 — Sin tests commiteados de la capa de filesystem
- `SecureFolderFileSystem.cs` tiene ~997 LOC sin un solo test en el repo. El harness de integración (WinFsp real: mount, escritura, roundtrip, handles, lock/unlock, overwrite/delete persistente) se ejecutó **desde un directorio temporal** y no está versionado.

### W2 — Estado compartido sin sincronización
- `SecureFolderFileSystem.cs:28-31` — `_files`, `_dirs`, `_allDirs`, `_deletedPaths` se acceden desde callbacks del FSD sin locks.
- `SecureFolderFileSystem.cs:121-131` — `Unmount` flushea antes de `_host.Unmount()`, pero otras operaciones siguen concurrentes.

### W3 — Hash de integridad calculado y persistido pero nunca verificado
- `src/SecureFolder.Core/Models/VaultInfo.cs:63` (`FileEntry.Hash`) se escribe en el índice, pero `LoadFileData` (`SecureFolderFileSystem.cs:208-229`) nunca lo valida al leer. No hay detección de corrupción por archivo.

### W4 — Auto-lock mide tiempo desde el desbloqueo, no inactividad
- `src/SecureFolder.App/ViewModels/MainViewModel.cs:309-324` — el temporizador cierra el vault tras N minutos desde el unlock, aunque el usuario esté trabajando activamente.

### W5 — Disco lleno invisible
- `SecureFolderFileSystem.cs:336-345` — `GetVolumeInfo` reporta `FreeSize = TotalSize` (constante 1 TiB). Sin cuota real, cualquier escritura con disco lleno falla de forma silenciosa/confusa.

### W6 — Código muerto que invoca `handle.exe`
- `src/SecureFolder.Core/Vault/VaultManager.cs:251-309` — `DetectOpenFiles` no se usa y depende de una herramienta externa.

### W7 — `AutoLockOnShutdown` se persiste pero nunca se lee
- `VaultInfo.cs:33` + `VaultManager.cs:344` — la propiedad existe y se guarda, pero ninguna ruta de código la consume. El README promete este comportamiento.

### W8 — Sin zeroización de claves en fallo de descifrado
- `VaultFormat.cs:221` — si falla `DecryptIndex`, la clave derivada permanece en memoria sin limpiarse.

---

## 🔵 Notes

- **N1** — `Norm()` no rechaza rutas con `..` (`SecureFolderFileSystem.cs:863-869`); conviene validar/denegar traversal.
- **N2** — Las contraseñas se manejan como `string` gestionado (`MainViewModel.cs:45-61`); no se pueden limpiar de memoria — usar `SecureString`/`char[]` si el modelo de amenaza lo exige.
- **N3** — No hay framework de migración de versión de formato de vault.
- **N4** — No hay logging estructurado; el diagnóstico depende del Event Log.
- **N5** — `VaultOpenResult.VaultName` se serializa siempre como `""` (`VaultFormat.cs:229`).
- **N6** — `_mutex.ReleaseMutex()` puede lanzar (origen del B4); ya cubierto por el fix.

---

## ✅ Observaciones positivas
- Cifrado AEAD correcto (AES-GCM) con derivación de clave por PBKDF2 y salt/IV por archivo.
- Integración WinFsp sólida: `OverwriteEx` implementado (el FSD usa `FILE_OVERWRITE_IF`), `FlushAndPurgeOnCleanup` + `PostCleanupWhenModifiedOnly` para drenar handles y permitir lock por `GetOpenFiles()`.
- Instalador self-contained (Inno Setup) reproducible vía `build.ps1`.
- Los 29 tests unitarios pasan; el harness de integración completo pasa.

---

## Checklist de remediación sugerido

**Antes del release público**
- [x] B4 — Crash de instancia única
- [ ] B1 — Corregir ramas en `.github/workflows/ci.yml` (`Master`/`master`)
- [ ] B2 — Escritura atómica (temp + `File.Replace`) + `.bak` en flush y cambio de contraseña
- [ ] B3 — Flush en streaming (sin cargar el vault completo en memoria)

**Siguiente iteración**
- [ ] W1 — Commitear el harness de integración como proyecto de tests
- [ ] W2 — Locks/estructuras concurrentes en la capa FS
- [ ] W3 — Verificar `FileEntry.Hash` al leer
- [ ] W4 — Auto-lock por inactividad real
- [ ] W5 — Cuota real o `FreeSize` honesto
- [ ] W6 — Eliminar `DetectOpenFiles`
- [ ] W7 — Implementar `AutoLockOnShutdown` o quitarlo del README
- [ ] W8 — Zeroizar claves en error
