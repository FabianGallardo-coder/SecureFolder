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
