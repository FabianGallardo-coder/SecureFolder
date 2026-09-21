# Arquitectura

SecureFolder es una aplicación WPF de Windows que crea contenedores cifrados (`.sfv`) y los
monta como **unidades virtuales transparentes** mediante [WinFsp](https://winfsp.dev/).

## Proyectos

```
SecureFolder.App          UI WPF + tray icon (net10.0-windows)
│   ├─ MainWindow.xaml        Tarjetas de vaults; botón ⚙ (ContextMenu: renombrar / cambiar
│   │                         contraseña / eliminar) y overlays de diálogo; banner si corre elevada
│   ├─ MainWindow.xaml.cs     Handlers de tarjetas y del ContextMenu; limpieza de PasswordBox
│   │                         (WPF no permite enlazarlos por MVVM)
│   ├─ MainViewModel.cs       MVVM (CommunityToolkit.Mvvm) — comandos del ciclo de vida del vault
│   └─ Converters/            CountToVisibleConverter y otros
│
SecureFolder.Core          Criptografía + filesystem virtual (net10.0-windows)
│   ├─ Crypto/                AesGcmEngine, Argon2Kdf, KeyWrapper (RFC 3394), SecureRandom
│   │   └─ Chunking:          Lógica de cifrado fragmentado (64KB) para soporte de archivos grandes.
│   ├─ Filesystem/            SecureFolderFileSystem (WinFsp FileSystemBase)
│   │   └─ Lazy Loading:       Lectura offset-based para evitar consumo masivo de RAM.
│   ├─ Vault/                 VaultFormat (.sfv), VaultManager, HmacHelper
│   └─ Models/                VaultInfo, FileEntry, Result
│
SecureFolder.Tests         xUnit + FluentAssertions (45+ casos, incl. regresión de enumeración y 0-byte files)
```

No hay `.sln`; se compila el proyecto de App directamente.

## Flujo de cifrado

1. **Crear vault**: la contraseña pasa por **Argon2id** → KEK (key-encryption key). Se genera un
   **DEK** aleatorio, se envuelve con AES-256-KW (RFC 3394) y se guarda en el header junto con
   salt, parámetros KDF y un blob de verificación. El header se autentica con HMAC-SHA256(KEK).
2. **Escribir archivos**: mientras el vault está montado, el filesystem corta cada archivo en
   chunks de 64 KB cifrados con **AES-256-GCM** (autenticado); el índice de archivos se mantiene
   cifrado con el DEK.
3. **Bloquear**: se vuelcan los bloques y el índice cifrado al `.sfv` y se desmonta la unidad.
4. **Cambiar contraseña**: se re-deriva la nueva KEK y se re-envuelve el mismo DEK — sin
   re-cifrar los datos.

```
password ─Argon2id──► KEK ─AES-KW(RFC3394)──► DEK ─AES-256-GCM──► datos (chunks 64KB)
                     └──────────► HMAC (integridad del header + verificación de contraseña)
```

## Ciclo de vida del vault (UI)

La tarjeta de cada vault muestra su estado (Bloqueada/Desbloqueada) y sus acciones:

- **Crear / Desbloquear / Bloquear**: botones de la tarjeta y overlays de diálogo.
- **Renombrar / Cambiar contraseña / Eliminar**: botón `⚙` → `ContextMenu`, habilitado solo con el
  vault **bloqueado** (el Core rechaza esas operaciones con la carpeta montada — ver `VaultManager`).
- **Cambiar contraseña**: re-deriva la nueva KEK y re-envuelve el mismo DEK, **sin re-cifrar datos**.
- **Eliminar**: confirmación previa; borrado *lógico* (sacar de la lista) o *físico* (borrar el `.sfv`).
- **Auto-bloqueo**: timer de 30 s que desmonta la unidad tras la inactividad configurada.

Notas de implementación:

- Los `PasswordBox` se limpian en `MainWindow.xaml.cs` al abrir cada diálogo y al resetear la
  contraseña en el ViewModel (`UnlockPassword`, `ChangePasswordCurrent`), evitando que el reintento
  concatene la contraseña anterior.
- Si la app corre **elevada**, la unidad montada no es visible para un Explorador no elevado
  (sesión de logon distinta por `DefineDosDevice`). La UI lo avisa con un banner de advertencia y
  `OpenVaultFolder` se abstiene de abrir el Explorador en ese caso.

## Mount (WinFsp)

`SecureFolderFileSystem` es un `FileSystemBase` de `winfsp.net`. Mantiene en memoria:
`_files` (ruta → `MemFile` con buffer), `_dirs` (dirs existentes), `_allDirs` (todos los dirs,
incluyendo los vacíos en sesión actual). Paths normalizados con separador único `\`; la raíz es
`\`. El protocolo de enumeración (IRP/marker) se explica en [Enumeración WinFsp](./enumeracion.md).

## Distribución

`build.ps1` publica self-contained (`release\app`) y compila el instalador **Inno Setup**
(`installer\SecureFolder.iss` → `release\SecureFolderSetup.exe`) que instala WinFsp desde
`installer\vendor\winfsp-*.msi`. La instalación requiere privilegios de administrador.