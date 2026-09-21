# Tarjeta (DataItem) vacía en UIA tras desbloquear/bloquear

**Estado**: FIJADO (2026-09-20).

## Síntoma
- Tras desbloquear, bloquear o "Bloquear todas", la tarjeta del vault en la ventana quedaba
  **sin hijos en el árbol UIA** (DataItem sin descendientes). QA5c/QA-R3 fallaban.
- El rendido visual estaba bien: comparación de capturas LOCKED vs UNLOCKED daba DIFF ~1.3%
  (solo cambia el texto) y conteo de píxeles casi idéntico (8093 vs 8040 azules).
- OJO: la tarjeta SÍ se veía bien a ojo humano; el bug era de **accesibilidad/automatización**,
  no de pintado.

## Causa raíz
`MainViewModel.RefreshVaultList()` hacía `Vaults.Clear()` + `Add()` (ObservableCollection).
Se llamaba tras Desbloquear/Bloquear/Bloquear todas/auto-lock. Al regenerar todos los
contenedores del `ItemsControl`, los **automation peers UIA se descartan** y no se vuelven a
exponer los hijos (solo el DataItem raíz).

`VaultManager.UnlockVaultAsync` / `LockVaultAsync` mutan la **misma** instancia `VaultInfo`
(`mounted.Info.IsUnlocked`/`DriveLetter`) que el VM ya tiene en `Vaults`. Como `VaultInfo`
implementa `INotifyPropertyChanged` (StatusText, IconEmoji, IsUnlocked triggers), la UI se
actualiza en sitio **sin necesidad de refrescar la colección**.

## Fix
En `MainViewModel.cs`:
- Eliminar `RefreshVaultList()` de: `UnlockVaultAsync`, `LockVaultAsync`, `LockAllVaultsAsync`,
  `AutoLockTimer_Tick` (solo mutan estado → INPC hace el trabajo).
- Mantener `RefreshVaultList()` donde cambia la **lista**: constructor/load, `CreateVaultAsync`,
  rename, delete.

Verificado en vivo:
- Inicio: DataItem 8 descendientes (QATrace, Bloqueada, path, Desbloquear, ⚙…).
- Desbloquear: Z: montada, DataItem **sigue con 8 descendientes** (Desbloqueada (Z:\), Bloquear).
- Bloquear (tarjeta): Z: desmontada, vuelve a Bloqueada/Desbloquear.
- Bloquear todas: igual. Build 0/0, tests 45/45.

## Lecciones
- No reconstruyas la colección del ItemsControl solo para cambiar propiedades de un item:
  usa bindings + INotifyPropertyChanged.
- Un DataItem vacío en UIA **no significa** que no se pinte; verifica con captura de píxeles
  antes de tocar el XAML.