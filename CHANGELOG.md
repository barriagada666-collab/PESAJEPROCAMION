# Historial de Cambios (CHANGELOG)

## [2.2.1] - 2026-11-27
### Corregido (Compatibilidad BD Remota Legacy ODS 9 / Firebird)
- **`DBLOGSINGRESO` sin columna `ID_LOG` (error SQL `-206 Column unknown ID_LOG`)**: `DatabaseCapabilities` detecta `HasLogIngresoId`; tanto `RegistrarLogIngresoAsync` como `GetLogsIngresoAsync` omiten `ID_LOG` automáticamente en bases remotas legacy, eliminando el error al iniciar sesión e inspeccionar logs.
- **`DBTRAN_ANUL` legacy sin `FECHAANUL`/`USUAANUL`/`MOTIVO` y `GDESPACHO` entero**: `DatabaseCapabilities` detecta `HasTranAnulAuditColumns` y `TranAnulGdespachoIsInteger`; `EliminarTransitoAsync` y `GetTransitoAnuladoAsync` archivan y consultan anulaciones de tránsito de forma nativa sin errores SQL `-206`.
- **Protección contra truncamientos en campos legacy (`VARCHAR(50)` / `VARCHAR(12)` / `VARCHAR(20)` / `VARCHAR(10)`)**: `FitLength` ajusta longitudes dinámicamente en `OBSERVA1`/`OBSERVA2`, `CHOFER`, `DBUSUARIOS.NOMBRE`/`CARGO` y `EnsureBarriagadaAdminAsync`.

## [2.2.0] - 2026-11-27
### Agregado
- **Personalización y Edición en Vivo en Visor de Tickets (`TicketViewerDialog` + `TicketFieldVisibility`)**:
  - Panel derecho interactivo con **Checkboxes de visibilidad por campo** (`Patente / Acoplado`, `Cliente`, `Producto`, `Origen / Prov.`, `Destino`, `Transportista`, `Chofer`, `Guía Despacho`, `Fechas / Operador` y `Observaciones`) más botones rápidos **Marcar Todos** y **Solo Básicos**, persistidos automáticamente en `appsettings.json` (`AppConfigStore`) y aplicados de forma consistente en **Vista Previa / Impresión**, **Exportación PDF** y **Copiar Texto**.
  - Edición directa del contenido del ticket en vivo (Empresa, Tipo de Pesaje, Patente, Acoplado, Cliente, Producto, Origen, Destino, Transportista, Chofer, Guía, Bruto, Tara, Neto y Observaciones) con reflejo inmediato en pantalla y botón **Guardar Cambios en BD**.
- **Tipo de Pesaje Destacado en Encabezado Superior Derecho**:
  - Reemplaza el indicador genérico superior derecho por una insignia destacada de operación (**RECEPCIÓN DE CARGA**, **DESPACHO DE CARGA** o **MOVIMIENTO INTERNO**, reflejando también `EN PROCESO` o `ANULADO` cuando corresponde) tanto en el visor gráfico como en impresión y PDF, conservando `ESTADO` claro junto al número de folio.
- **Ingreso Manual Independiente de Bruto, Tara y Neto en CRUD de Tickets (`TicketEditorDialog`)**:
  - Campo **Neto (kg)** editable libremente junto a opción **Auto-calcular Neto (`|Bruto - Tara|`)**; al editar manualmente se conserva íntegramente `Bruto`, `Tara` y `Neto` en `DBPESAJES` (`preserveManualNeto: true`).
- **División de Ticket en 2 Folios sin Conflicto (`SplitTicketDialog` + `WeighingRepository.SplitTicketAsync`)**:
  - Asistente accesible desde la barra y clic derecho de **Administración de Pesajes**, y desde **Visor de Ticket**: divide un ticket cerrado en 2 tickets con reparto exacto de neto (ej.: Folio #1 con Neto 10 dividido en Folio #1 con Neto 6 + nuevo Folio #2 con Neto 4), presets **50%/50%**, **60%/40%**, **70%/30%**, ajuste opcional de Bruto/Tara y asignación automática del nuevo folio respetando estrictamente históricos y folios **En Proceso** (`DBTRANSITO`).

## [2.1.0] - 2026-11-27
### Agregado
- **Gestión Completa de Usuarios, Claves y Matriz RBAC (`UsuariosForm` + `SecurityRepository`)**:
  - Cuenta administradora principal **`barriagada`** (contraseña **`Cambias0`**) sembrada y normalizada automáticamente con privilegios totales en bases locales, migraciones ODS 9.1 y validación de acceso.
  - Formulario renovado con buscador en vivo, plantillas rápidas de rol (**Admin Total**, **Operador Pesaje**, **Solo Consulta**), restablecimiento de clave con toggle Mostrar/Ocultar, activación/desactivación con protección del último administrador activo, exportación Excel y menú contextual por clic derecho.
- **CRUD Completo de Tickets sin Conflictos de Folio (`TicketEditorDialog` + `WeighingRepository`)**:
  - Creación manual de tickets terminados (`DBPESAJES`) o En Proceso (`DBTRANSITO`) y modificación de registros históricos o en tránsito respetando estrictamente `DBTRANSITO`, `DBTRAN_ANUL` y `DBFOLIOS`.
- **Visor Gráfico de Tickets (`TicketViewerDialog`)**:
  - Previsualización corporativa con logotipo oficial, selector de copias, impresión directa, exportación unitaria a PDF y copia de texto.
- **Menús Contextuales por Clic Derecho (`ContextMenuStrip`)**:
  - Acciones rápidas CRUD, duplicar, completar salida, anular/reactivar, exportar PDF, copiar y actualizar en Pesaje En Tránsito, Administración de Pesajes, Usuarios, Detalle BI y Búsquedas.
- **Centro Analítico BI KPI y Segmentación (`ReportsWorkspaceControl` + `AnalyticsCalculator`)**:
  - Rango `DatePicker` con accesos rápidos (**Hoy / 7D / Mes / Todo**), segmentadores reactivos (**Patente, Proveedor/Origen, Cliente, Producto, Tipo, Estado, Búsqueda**), 5 tarjetas KPI, pestañas segmentadas, gráficos GDI+ y exportación zero-dependencias a **PDF** y libro **Excel `.xlsx` de 6 hojas**.

## [2.0.0] - 2026-11-27
### Agregado
- **Identidad Visual Corporativa Petrol Teal + Logo**, **Conexión Remota Inteligente UNC** (`\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB`), **Visor y Trazabilidad de Errores (`AppLogger` + `ErrorLogViewerDialog`)**, **Edición Portable Single-File + Instalador Inno Setup 7** y **Visor RS-232 Ampliado** (`STX=0`).
