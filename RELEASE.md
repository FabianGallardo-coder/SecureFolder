# SecureFolder v1.0.0 - Release Notes

## 🚀 Versión 1.0.0 (Stable)

Esta es la primera versión estable de SecureFolder, un sistema de carpetas cifradas montadas como unidades virtuales transparentes.

### ✨ Características Principales
- **Cifrado de Grado Militar**: Implementación de AES-256-GCM para datos y AES-256-KW (RFC 3394) para la gestión de llaves.
- **Arquitectura Lazy Loading**: Soporte nativo para archivos grandes mediante lectura y escritura fragmentada (chunks de 64KB), eliminando el consumo masivo de memoria RAM.
- **Montaje Transparente**: Integración con WinFsp para montar bóvedas como letras de unidad (ej. `Z:\`) en Windows.
- **Gestión Completa de Bóvedas**: 
  - Creación, Bloqueo y Desbloqueo rápido.
  - Renombrado de bóvedas (archivo físico y lógico).
  - Eliminación segura (configurable: solo configuración o borrado físico del archivo).
  - Cambio de contraseña sin necesidad de re-cifrar los datos.
- **Integridad Garantizada**: Verificación de cabeceras mediante HMAC-SHA256 y autenticación de datos mediante tags GCM.

### 🛠️ Especificaciones Técnicas
- **Framework**: .NET 10.0 (Windows)
- **KDF**: Argon2id para derivación de llaves desde contraseñas.
- **Soporte de Archivos**: Soporte total para archivos de 0 bytes y estructuras de directorios anidadas.
- **UI**: Interfaz WPF moderna basada en MVVM.

### 🧪 Validación de Calidad (QA)
- **Pruebas Unitarias**: 45+ casos de prueba superados, incluyendo regresiones de enumeración y persistencia de archivos vacíos.
- **Estabilidad**: Verificado el cierre limpio del sistema de archivos y la liberación de recursos criptográficos.
- **Performance**: Validada la eficiencia de memoria en operaciones de lectura/escritura de archivos grandes.

### 📦 Instalación
1. Ejecutar el instalador `SecureFolderSetup.exe`.
2. El instalador configurará automáticamente WinFsp si no está presente.
3. Requiere privilegios de administrador para el montaje de la unidad virtual.
