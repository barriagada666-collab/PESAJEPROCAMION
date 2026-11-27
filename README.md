# PesajeProCamion

Sistema integral de pesaje de camiones en báscula comercial, control de peso por ejes (normativa M.O.P. Chile) y gestión administrativa de planta, migrado completamente a **.NET 10**, **Firebird SQL** e instalador **Inno Setup 7**.

---

## Descripción del Proyecto

**PesajeProCamion v2.0** moderniza y unifica los módulos operativos (*Pesaje en Báscula* y *Gestión e Informes*) en una plataforma robusta, tipada, limpia de deuda técnica BDE/ODS 9.1 y preparada para operación industrial continua.

### Características Principales
- **Pesaje Comercial en Tiempo Real**: Visor digital 7 segmentos High-DPI con indicador de estabilidad, zona cero, captura por puerto RS-232 configurable (`STX`, posición, largo, paridad), ingreso manual de contingencia y modo simulación (`/TEST`).
- **Control de Flujo en Planta**: Gestión de dos pasadas (`DBTRANSITO` -> `DBPESAJES`), tara registrada con control de vigencia (`NDIAS` / `TARAREG`), normalización de patentes y validación de RUT chileno Módulo 11.
- **Pesaje por Ejes M.O.P. (Decreto 158/80 y Ley 19.171/92)**: Modo estático y dinámico con filtrado porcentual, compensación/offset por eje, verificación automática de ejes simples/conjuntos/tándems y peso bruto máximo por largo, además de reproducción de curva `DEMO1`.
- **Gestión, Exportación e Informes**: Edición y anulación segura con auditoría, impresión gráfica y RAW de tickets comerciales y boletos MOP, exportación nativa a **Excel (`.xlsx`)**, **dBASE III (`.dbf`)** y **CSV**, más 10 informes operativos y de auditoría.
- **Migrador Automático ODS 9.1 -> Firebird UTF8**: Lee archivos legacy InterBase `.GDB` directamente desde binario, sanea anomalías (patentes duplicadas por espacios, registros nulos, huérfanos MOP, fechas `1899-12-30`) e importa todo hacia Firebird Dialecto 3.
- **Seguridad Moderna**: Contraseñas protegidas con **PBKDF2-SHA256**, políticas RBAC estrictas por perfil/privilegio y bitácora histórica (`DBLOGSINGRESO` + `DBAUDITLOG`).

---

## Requisitos

### Desarrollo y Compilación
- **Windows 10 / 11** (x64)
- **.NET 10 SDK** (`10.0.100+` con Windows Desktop)
- **Firebird 2.5+** (`localhost:3050` para pruebas de integración)
- **Inno Setup 7** (para compilar el instalador)

### Ejecución (Instalador)
- **Windows 10 / 11 (64 bits)** + acceso a servidor **Firebird 2.5+** (el paquete instalado es autocontenido y no requiere instalar .NET previamente).

---

## Instalación

1. Descargue o compile `PesajeProCamion_Setup_v2.0.0.exe` ubicado en `installer/Output/`.
2. Ejecute el instalador como usuario estándar o administrador (soporta actualización directa conservando datos y configuración en `%ProgramData%\PesajeProCamion`).
3. Para despliegue desatendido:
   ```powershell
   PesajeProCamion_Setup_v2.0.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
   ```

---

## Configuración y Migración Inicial

La configuración de conexión Firebird se almacena localmente en `%ProgramData%\PesajeProCamion\appsettings.json` (editable desde el botón **Base de Datos** en el Login).

Para migrar una base legacy InterBase `.GDB` desde consola:
```powershell
PesajeProCamion.App.exe --migrate "D:\ruta\WMDBSPC.GDB" "C:\ProgramData\PesajeProCamion\Data\WMDBSPC.FDB"
```

---

## Uso y Compilación Rápida

```powershell
# Compilar en Debug y Release (0 errores, 0 advertencias)
dotnet build PesajeProCamion.sln -c Debug
dotnet build PesajeProCamion.sln -c Release

# Ejecutar todas las pruebas (Unit, Integration, Functional, UI, Regression, Stress)
dotnet test PesajeProCamion.sln -c Release

# Generar publicación autocontenida e instalador Inno Setup 7
dotnet publish src\PesajeProCamion.App\PesajeProCamion.App.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64
& "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe" installer\PesajeProCamion.iss
```

Consulte la documentación detallada en `docs/`:
- [Auditoría Legacy](docs/AUDITORIA_LEGACY.md)
- [Arquitectura Técnica](docs/ARQUITECTURA.md)
- [Manual Funcional](docs/MANUAL_FUNCIONAL.md)
- [Guía de Compilación](docs/COMPILACION.md)
- [Guía de Despliegue y Migración](docs/DESPLIEGUE.md)

---

## Versionado e Historial de Cambios

Versionado semántico (`MAJOR.MINOR.PATCH`). Versión actual: **`2.0.0`**.
Ver detalle completo en [CHANGELOG.md](CHANGELOG.md).

## Créditos

Desarrollado para operaciones industriales de pesaje vehicular y control caminero sobre plataforma **.NET 10** y **Firebird SQL**.
