# SecureFolder — Wiki

Documentación técnica del proyecto. La fuente de verdad operativa del agente
(errores, pendientes, decisiones) está en [`.agent/knowledge/`](../.agent/knowledge/INDEX.md).

## Contenido

| Página | Qué cubre |
|--------|-----------|
| [Arquitectura](./arquitectura.md) | Proyectos, capas, flujo de cifrado y montaje |
| [Formato del vault (.sfv)](./formato-sfv.md) | Estructura binaria del contenedor cifrado |
| [Enumeración WinFsp](./enumeracion.md) | Protocolo IRP/marker y por qué casó el bug de la raíz |

## Guía rápida

- Requisitos: Windows 10/11 x64 y [WinFsp](https://winfsp.dev/rel/) (el instalador lo incluye).
- Instalación y uso: ver `README.md` en la raíz.
- Build/tests: ver `AGENTS.md` en la raíz.