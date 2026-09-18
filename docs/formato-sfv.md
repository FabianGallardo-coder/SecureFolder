# Formato del vault (.sfv)

Versión 1 (`SFVL`). Definición de referencia: `src\SecureFolder.Core\Vault\VaultFormat.cs`.

## Layout del archivo

```
┌──────────────────────────────────────┐
│              VAULT HEADER            │  ~138 bytes
│  Magic "SFVL" (4B)                   │
│  Version uint16 LE (2B)              │
│  Salt 16B (Argon2id)                 │
│  KDF params 12B                      │
│  DEK envuelto 40B (AES-256-KW)       │
│  Blob de verificación 32B            │
│  HMAC-SHA256 del header 32B          │
├──────────────────────────────────────┤
│  Longitud del índice cifrado (4B)    │
│  Índice cifrado (AES-256-GCM, JSON)  │
├──────────────────────────────────────┤
│  Bloques de datos cifrados           │  chunks de 64 KB por archivo
└──────────────────────────────────────┘
```

Nota: `HeaderSize = 138` bytes (constante en `VaultFormat`).

| Campo | Offset | Tamaño | Detalle |
|-------|--------|--------|---------|
| Magic | 0 | 4 | `SFVL` (ASCII) |
| Version | 4 | 2 | uint16 LE; cargas > versión actual se rechazan |
| Salt | 6 | 16 | salt de Argon2id |
| KDF params | 22 | 12 | memorySize (4) + iterations (4) + parallelism (4), LE |
| DEK envuelto | 34 | 40 | AES-256-KW (RFC 3394): 32B DEK + 8B check |
| Blob verificación | 74 | 32 | nonce (12) + ciphertext "SFLV" (4) + tag (16), GCM con DEK |
| HMAC header | 106 | 32 | HMAC-SHA256(KEK) sobre los primeros 106 bytes |
| Longitud índice | 138 | 4 | int32 LE, N |
| Índice cifrado | 142 | N | `AesGcmEngine.Encrypt(JSON)` |
| Bloques | 142+N | … | chunks de 64 KB, cada uno nonce(12)+ciphertext+tag(16) |

## Parámetros de seguridad

- **KDF**: Argon2id — 64 MB de memoria, 3 iteraciones, paralelismo 4 (`KdfParameters.Default`).
- **Cifrado**: AES-256-GCM, nonce 12B + tag 16B ⇒ overhead de 28B por bloque cifrado.
- **Envolver DEK**: AES-256-KW (RFC 3394) — permite cambiar contraseña sin re-cifrar datos.
- **Inicio de sesión**: la verificación tiene dos capas — HMAC del header (descarta contraseña
  errónea) y blob de verificación GCM con el DEK "SFLV".

## Índice (JSON cifrado)

El índice es un diccionario `relativePath → FileEntry` serializado con `System.Text.Json`:

| Campo | Tipo | Detalle |
|-------|------|---------|
| `OriginalName` | string | nombre visible del archivo |
| `RelativePath` | string | ruta relativa con `/` |
| `DataOffset` | long | offset del bloque dentro de la sección de datos |
| `EncryptedSize` | long | tamaño del bloque cifrado |
| `OriginalSize` | long | tamaño en claro |
| `Hash` | byte[32] | SHA-256 del contenido en claro |
| `CreationTime` / `LastWriteTime` | DateTimeOffset | metadatos |
| `Nonce` | byte[] | nonce del bloque |

El `DataOffset` es relativo al inicio de la sección de datos (se re-mapea al reescribir el vault
en `FlushDirtyFiles`). Solo se indexan **archivos**; las carpetas se recrean a partir de las rutas
de los archivos al montar (ver [Decisión: carpetas vacías](../.agent/knowledge/empty-folders-option-b.md)).