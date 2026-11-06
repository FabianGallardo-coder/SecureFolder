# E2E real: vault creado en D:, movido a C: y recuperado con «Abrir existente…»

- **Fecha**: 2026-11-06
- **Tipo**: QA E2E (UIA, sin clics físicos) + learnings de automatización
- **Estado**: PASS completo (Fase A + B + B6); limpieza verificada (0 residuos)

## Escenario probado (pedido por el usuario)

1. **Fase A — crear en D:** (disco NTFS «backup» que Windows reporta como *Fixed*):
   crear `D:\PruebaUSB.sfv` por la UI → desbloquear → **Z: montada** → escribir
   `viaje.txt` → bloquear → Z: desmontada → el `.sfv` creció 172→560 bytes y **no
   contiene el texto en claro** (cifrado verificado).
2. **Fase B — mover a C:** `D:\PruebaUSB.sfv` →
   `C:\Users\Fabian\Documents\mis-sfv-movidos\PruebaUSB.sfv`.
   - La app muestra la **tarjeta con la ruta vieja** (`D:\…`).
   - Al desbloquear: **«No se encontró el archivo de la carpeta segura en el disco.»**
     → error limpio, sin crash, la tarjeta queda «Bloqueada». ✔ comportamiento deseado.
3. **Recuperación — «Abrir existente…»**: importa el `.sfv` desde la nueva ubicación
   (se agregó una 2.ª entrada; hubo un 2.º intento rechazado por el guardián de
   duplicados «Esta carpeta segura ya está en la lista» — dedup por path exacto,
   case-insensitive, en `VaultManager.ImportVaultAsync`).
4. **Fase B6 — probar el importado**: con **2 tarjetas** (D: rota + C: válida),
   desbloquear específicamente la de **C:** → **Z: montada** → `viaje.txt` con el
   contenido intacto (el escrito en Fase A) → bloquear → Z: desmontada.

## Learnings UIA para futuros scripts E2E (muy útiles)

1. **OpenFileDialog de Win11** (Common Item Dialog) es **hijo** de la ventana
   principal (`#32770` como *descendiente*, no top-level). Buscarlo con
   `FindAll(Descendants, ControlType.Window)` + `ClassName -eq '#32770'`.
2. **No hay edit `FileName`**; los botones «Abrir/Cancelar» son `Pane` (id 1/2)
   **sin patrones**. Ruta: Alt+D → si aparece un edit de dirección → `SetValue` +
   Enter; si no, `Ctrl+L` + `SendKeys` de la ruta + Enter.
3. **`PropertyCondition` con wildcard sobre `Name` NO casa** en el proveedor UIA de
   WPF (aunque el Name coincida). Filtrar con `-like` tras `FindAll`.
4. **Caminar `TreeScope.Ancestors`** desde un `Text` de la tarjeta no devolvió la
   tarjeta usable → usar **proximidad de `BoundingRectangle`** (botón en la misma
   fila Y, tolerancia ≤ 80 px) para elegir el botón de una tarjeta concreta
   (clave con 2 tarjetas homónimas).
5. **TextBox WPF = `ControlType.Edit`** (el enum no tiene miembro `TextBox`).
6. **PasswordBox sin `ValuePattern`** → Alt-trick (`keybd_event` VK_MENU down/up) +
   `SetForegroundWindow` + `SendKeys`; verificar `GetForegroundWindow` antes de tipear.
7. **`SetFocus` sobre `#32770`** lanza «El elemento de destino no puede recibir el
   foco» → usar solo foreground + SendKeys.
8. **Botón «Crear carpeta segura»** del bottom bar tiene **Name UIA vacío** (el
   contenido es un `StackPanel`) → helper que arma Name + hijos `Text`.
9. **Arranque en frío > 8 s**: reusar instancia existente y reintentar la ventana
   hasta 25 s.
10. Detección de resultados por **status `Text`** (regex sobre `Get-Texts`); ojo:
    «Agregando…» no contiene «agregada» — esperar el mensaje final con reintentos.

## Registros de la prueba

- `vaults.json` quedó con 2 entradas durante el test (D: rota + C: importada) y se
  borró en la limpieza final junto con el `.sfv` de prueba y la carpeta de C:.
- `D:` sin restos, `Z:` libre, 0 procesos al cierre.
