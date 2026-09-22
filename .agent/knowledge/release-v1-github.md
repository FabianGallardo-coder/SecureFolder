# Release v1.0, repo público y saneamiento de ramas (GitHub)

**Fecha**: 2026-09-22 · **Tipo**: decisión / infra

## Estado actual

- **Repo `FabianGallardo-coder/SecureFolder` → PÚBLICO** (antes privado; hecho público para que
  el botón de descarga anónimo del pitch/guía funcione).
- **Default branch = `master`** (antes `Master`, que apuntaba al Initial commit `21e9c88` y hacía
  que el repo se viera "vacío/viejo" al abrirlo). La rama `Master` fue **borrada** del remoto.
- **PR #1** (`master` → `Master`): se autocerró al borrar la base.
- **Release `v1.0`** publicado con asset `SecureFolderSetup.exe` (47 662 945 bytes ≈ 45,5 MB),
  `--latest`. URL de descarga que usan los CTA:

  `https://github.com/FabianGallardo-coder/SecureFolder/releases/download/v1.0/SecureFolderSetup.exe`

  Verificada con GET anónimo → **HTTP 206**.
- CTA insertados en `pitch/index.html` (S12, `a.pill`) y `pitch/guia-creacion.html` (S15, `.cta-row`).
- Commit: `2b2598f docs(pitch): refina pitch, añade guía paso a paso y CTA de descarga`.

## Gotchas aprendidas

1. **Refs case-insensitive en Windows**: `refs/remotes/origin/Master` y `origin/master` son el
   MISMO archivo en NTFS → `git status` mostraba `ahead 21` falso (vs Initial commit).
   `git fetch` lo repara; pero `git push origin --delete Master` **borra también el tracking
   local** por la colisión → volver a `git fetch` después de deletes de ramas con case distinto.
2. **Assets de release en repo privado**: la `browser_download_url` da 404 anónimo Y con
   `Authorization:` header (github.com solo acepta sesión de navegador para assets privados).
   Con `gh`/API (`Accept: application/octet-stream` + **id numérico**, no el node id `RA_…`)
   sí descarga. Solución elegida: repo público.
3. **`gh auth login` device flow en shell no interactiva**: `Start-Process` con stdin = archivo
   con `\n` (satisface el "Press Enter"); el **one-time code va a stderr** (`gh-auth.err`), no a
   stdout. El código también se copia al portapapeles.
4. `gh api repos/…/releases/assets/{id}` espera el **id numérico**; `--jq '.assets[0].id'` de
   `gh release view` devuelve el **node id** (`RA_…`) → 404 engañoso. Usar `gh release download`
   para probar assets.
5. Subir el `.exe` al repo está descartado (45 MB + binario); assets de Release es el mecanismo.

## Relanzar un release

```powershell
gh release create v1.1 "release\SecureFolderSetup.exe" --title "SecureFolder v1.1" --latest --notes "…"
```

Si cambia el tag, actualizar los dos href de los CTA (guía S15 + pitch S12).
