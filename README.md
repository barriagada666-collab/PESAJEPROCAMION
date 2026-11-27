# PesajeProCamion

Sistema integral de pesaje de camiones en báscula comercial, control de peso por ejes (normativa M.O.P. Chile), gestión administrativa CRUD de tickets/usuarios y analítica BI en planta, desarrollado sobre **.NET 10 Zero-Dependencias**, **Firebird SQL** e instalador **Inno Setup 7** (con versión **Portable Single-File** y conexión transparente a servidores locales o remotos en red).

---

## Descripción del Proyecto

**PesajeProCamion v2.2.1 LTS** unifica y moderniza la operación completa de romana y administración bajo una interfaz corporativa **Petrol Teal + Dorado** 100% responsiva (validada desde `1024x700` hasta `4K`), eliminando dependencias legacy BDE/ODS 9.1, conectándose a bases locales o compartidas en red (`\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB`), protegiendo folios contra colisiones e incorporando personalización/edición avanzada de tickets, división de tickets en 2 folios e inteligencia de negocio BI (PDF + Excel `.xlsx`).

### Características Principales
- **Visor Personalizable y Editor en Vivo de Tickets (`TicketViewerDialog`)**:
  - **Checkboxes por campo** para mostrar u ocultar libremente *Patente, Cliente, Producto, Origen/Proveedor, Destino, Transportista, Chofer, Guía de Despacho, Fechas/Operador y Observaciones* (con persistencia en `appsettings.json` aplicada a Vista, Impresión, PDF y Copia de Texto).
  - **Encabezado destacado de Tipo de Pesaje** (**RECEPCIÓN DE CARGA / DESPACHO DE CARGA / MOVIMIENTO INTERNO**) en la parte superior derecha del ticket y edición en tiempo real del contenido/pesos con guardado directo a Firebird.
- **CRUD de Tickets sin Conflictos de Folio + Pesos Manuales + División en 2 (`TicketEditorDialog` + `SplitTicketDialog`)**:
  - Ingreso manual independiente de **Bruto**, **Tara** y **Neto** (con cálculo automático opcional).
  - **División automática de Tickets en 2 folios** (ej.: dividir Folio `#1` de Neto `10` en Folio `#1` Neto `6` + nuevo Folio `#2` Neto `4`), asignando automáticamente el próximo folio libre sin colisionar con históricos ni vehículos **En Proceso**.
- **Gestión Completa de Usuarios y Seguridad RBAC (`UsuariosForm`)**:
  - Cuenta administradora principal **`barriagada`** (clave inicial **`Cambias0`**) garantizada en inicialización y migración.
  - CRUD completo, restablecimiento de clave con visor mostrar/ocultar, plantillas (**Admin Total / Operador Pesaje / Solo Consulta**), permisos granulares y protección del último administrador activo.
- **Menús Contextuales por Clic Derecho (`ContextMenuStrip`)**:
  - Clic derecho en **Camiones en Tránsito**, **Administración de Pesajes**, **Gestión de Usuarios**, **Detalle BI** y **Diálogos de Búsqueda** para visualizar, modificar, dividir, duplicar, completar salida, anular/reactivar, exportar a PDF o copiar datos al instante.
- **Centro Analítico BI, KPIs y Exportación Nativa PDF + Excel (`.xlsx`)**:
  - Panel de **Informes y Auditoría** con filtro `DatePicker` (**Hoy / 7D / Mes / Todo**), segmentadores reactivos (**Patente, Proveedor/Origen, Cliente, Producto, Tipo, Estado, Búsqueda**), 5 tarjetas **KPI**, pestañas segmentadas, **Gráficos KPI** nativos y 11 informes operativos.
  - Exportación **cero-dependencias** a **PDF** corporativo (`PdfDocumentWriter`) y libro **Excel `.xlsx` de 6 hojas** (`Resumen_KPI`, `Detalle_Pesajes` y resúmenes), además de `.dbf` y `.csv`.
- **Conexión Remota en Red, Visor RS-232 y Cero Dependencias**:
  - Modal de BD Remota (`RemoteDatabaseResolver` + `DatabaseCapabilities`) para rutas UNC/IP (`\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB`), migrador ODS 9.1, visor COM1..COM16 (`STX=0`), simulación `/TEST` y logs (`ErrorLogViewerDialog`).

---

## Capturas

| Pantalla de Arranque Corporativa | Inicio de Sesión y Acceso BD/Logs |
| :---: | :---: |
| ![Splash Corporativo](docs/screenshots/splash.png) | ![Inicio de Sesión](docs/screenshots/login.png) |

| Panel de Pesaje Comercial y Visor RS-232 | Administración de Pesajes (CRUD + Dividir + Clic Derecho) |
| :---: | :---: |
| ![Panel de Pesaje](docs/screenshots/pesaje_main.png) | ![Administración de Pesajes](docs/screenshots/gestion_crud.png) |

| Visor Personalizable de Ticket (Checkboxes + Edición en Vivo) | División de Ticket en 2 Folios sin Colisión |
| :---: | :---: |
| ![Visor Personalizable](docs/screenshots/ticket_viewer.png) | ![Dividir Ticket](docs/screenshots/split_ticket.png) |

| Centro Analítico BI KPI y Segmentación | Gestión Integral de Usuarios y RBAC |
| :---: | :---: |
| ![Informes BI KPI](docs/screenshots/informes_bi_kpi.png) | ![Usuarios y Permisos](docs/screenshots/usuarios_crud.png) |

| Editor / Creador Manual de Ticket (Bruto/Tara/Neto Manual) | Conexión Remota BD y Migrador |
| :---: | :---: |
| ![Editor de Ticket](docs/screenshots/ticket_editor.png) | ![Conexión BD Remota](docs/screenshots/remote_db.png) |

---

## Requisitos

### Equipos Finales (Cero Dependencias)
- **Windows 10 / Windows 11 (64 bits)**, resolución mínima recomendada `1024x700`.
- **No requiere instalar .NET Runtime ni Excel** (compilación autocontenida con motores nativos OpenXML `.xlsx` y PDF 1.4 integrados).
- Conectividad hacia servidor **Firebird / InterBase** (local `localhost:3050` o equipo remoto en red TCP `3050`).

### Desarrollo y Empaquetado
- **Windows 10 / 11 (x64)**
- **.NET 10 SDK** (`10.0.100+` Windows Desktop)
- **Firebird 2.5+** para pruebas de integración
- **Inno Setup 7** (`ISCC.exe`)

---

## Instalación

### Opción 1 — Instalador Windows (Inno Setup 7)
Descargue `PesajeProCamion_Setup_v2.2.1.exe` desde la sección **Releases** de GitHub y ejecute el asistente (actualiza versiones anteriores conservando configuración y datos):
```powershell
PesajeProCamion_Setup_v2.2.1.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

### Opción 2 — Edición Portable Zero-Dependencias
Descomprima `PesajeProCamion_Portable_v2.2.1_win-x64.zip` y ejecute `PesajeProCamion.App.exe` directamente sin instalar ni requerir privilegios administrativos. Con `portable.flag` o `appsettings.json` junto al ejecutable, la configuración y los archivos `Logs\` se conservan junto al binario.

---

## Configuración y Acceso Inicial

- **Usuario administrador predeterminado**: `barriagada` / contraseña: `Cambias0` (administración completa desde *Configuración -> Usuarios y Permisos...*).
- **Conexión BD local o remota**: En **Login** (`Servidor BD`) o en la barra superior principal (**Configurar Servidor BD...**), ingrese la ruta UNC compartida (ej.: `\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB`), presione **Probar Conexión** y guarde con **Aplicar (Sin Cerrar)** o **Guardar y Conectar**.

```powershell
# Probar conexión remota UNC por línea de comandos
PesajeProCamion.App.exe --probe "\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB"

# Migrar archivo legacy ODS 9.1 hacia base moderna Firebird UTF8
PesajeProCamion.App.exe --migrate "D:\ruta\WMDBSPC.GDB" "C:\ProgramData\PesajeProCamion\Data\WMDBSPC.FDB"
```

---

## Uso y Compilación Rápida

```powershell
# Compilar en Debug y Release (0 errores, 0 advertencias)
dotnet build PesajeProCamion.sln -c Debug
dotnet build PesajeProCamion.sln -c Release

# Ejecutar suite completa (Unit, Integration, Network UNC, Functional, UI, Stress)
dotnet test PesajeProCamion.sln -c Release --no-build

# Generar Instalador + Edición Portable autocontenidos
dotnet publish src\PesajeProCamion.App\PesajeProCamion.App.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64
& "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe" installer\PesajeProCamion.iss
dotnet publish src\PesajeProCamion.App\PesajeProCamion.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish\portable-win-x64
```

Documentación detallada en `docs/`:
- [Auditoría Legacy](docs/AUDITORIA_LEGACY.md)
- [Arquitectura Técnica](docs/ARQUITECTURA.md)
- [Manual Funcional](docs/MANUAL_FUNCIONAL.md)
- [Guía de Compilación](docs/COMPILACION.md)
- [Guía de Despliegue y Conexión Remota](docs/DESPLIEGUE.md)

---

## Versionado e Historial de Cambios

Versionado semántico (`MAJOR.MINOR.PATCH`). Versión actual: **`2.2.1`**.
Ver historial completo en [CHANGELOG.md](CHANGELOG.md).

## Créditos

PesajeProCamion — Plataforma industrial de pesaje vehicular, control de ejes M.O.P. y analítica BI sobre **.NET 10** y **Firebird SQL**.
