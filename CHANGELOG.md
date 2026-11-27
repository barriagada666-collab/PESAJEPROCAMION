# Historial de Cambios (CHANGELOG)

## [2.4.0] - 2026-11-27
### Optimizado, Endurecido y Corregido
- **Eliminación de Fugas de Memoria, GDI y Suscripciones WinForms**:
  - Agregada desuscripción garantizada en `Dispose(bool)` para `VisorSetupForm` y `PesajeMopForm` (frente al singleton `ScaleReaderService.ReadingReceived`), eliminando retención de formularios completos y handles GDI en aperturas/cierres repetidos.
  - Centralizadas fuentes compartidas (`AppTheme.FontSmall`, `FontSmallBold`, `FontBodyBoldLarge`, `FontChartTitle`, `FontInput`, `FontKpiTitle`, `FontKpiSub`, `FontKpiValue`) y singleton `SharedFormIcon` en `AppTheme`, eliminando fugas de objetos `Font`/`Icon` por refresco de grilla y eventos `Paint` (`GestionPesajesControl`, `ReportsWorkspaceControl`, `TicketViewerDialog`, `ErrorLogViewerDialog`, `CatalogCrudBaseForm`).
  - Ciclo de vida determinístico de `ContextMenuStrip` (`UniversalContextMenuService.ReplaceContextMenu`) y desuscripción segura en `ErrorLogViewerDialog.Dispose`.
- **Rendimiento de Consultas y Asignaciones (`MopRepository`, `ScaleReaderService`)**:
  - Eliminado patrón N+1 en `MopRepository.GetPesajesMopAsync`: reemplazada lectura detalle fila a fila por una sola consulta batch con `INNER JOIN` sobre `DBMOPDETPJES`.
  - Uso de `ArrayPool<byte>.Shared` en lectura serial RS-232 (`ScaleReaderService.OnSerialDataReceived`) y registro explícito CodePages `1252` en `TicketPrinterService.SendRawTextToPrinter`.
- **Sistema Centralizado de Logs, Sanitización y Manejo de Errores (`AppLogger`, `Program`, `AppTheme`)**:
  - Agregados niveles `Debug` y `Critical`, `NewCorrelationId()` (`OP-...`), sanitización automática de secretos/credenciales (`Password=`, `PWD=`, `Bearer`, `ghp_`), deduplicación de errores repetidos (`RepeatCount`), rotación por tamaño (`5 MB` por defecto) y depuración por retención (`30` días), con fallback seguro a `%TEMP%` y registro no recursivo en `LastWriteWarning`.
  - Diálogos de error amigables con código de correlación sin exposición de credenciales ni trazas sensibles; `CatalogCrudBaseForm` protegido con `AppTheme.SafeLoadAsync`.
- **Resiliencia Firebird (`DatabaseSettings`, `DatabaseSessionFactory`, `WeighingRepository`, `CatalogRepository`)**:
  - Soporte configurable de `CommandTimeout` (rango `[5..300]s`, defecto `30s`) aplicado mediante `DatabaseSessionFactory.CreateCommand` y persistido en `appsettings.json` (`AppConfigStore`).
  - Rollback transaccional seguro con `CancellationToken.None` y registro aislado sin enmascarar la excepción original.
- **Cobertura de Pruebas Automatizadas**:
  - Nuevas pruebas para rotación, retención, deduplicación, sanitización de secretos, `CommandTimeout`, benchmark analítico/exportación de `5,000` tickets y verificación de fugas GDI/heap en ciclos repetidos de UI.

## [2.3.0] - 2026-11-27
### Agregado y Estandarizado
- **Checkbox de Visibilidad para `Estado` y `Cantidad de Fardos` (`TicketViewerDialog`, `AppConfigStore`, `TicketFieldVisibility`)**:
  - Nuevas opciones **Estado** y **Cant. de Fardos** en el menú de campos configurables del ticket, persistidas automáticamente en `appsettings.json` (`MostrarEstado`, `MostrarFardos`) y aplicadas de forma consistente al cargar la aplicación en visor gráfico, vista previa, impresión, exportación PDF, Excel (`.xlsx`) y copia de texto.
  - Ocultar un campo solo afecta su presentación; nunca borra ni altera el valor almacenado en Firebird.
- **Campo Numérico `Cantidad de Fardos` en Todo el Proyecto + Migración Idempotente**:
  - Campo entero no negativo (`>= 0`) y checkbox de visualización sincronizado en Pesaje Comercial (`PesajeWorkspaceControl`), Creación/Edición Manual (`TicketEditorDialog`, `ModPesajeDialog`), Visor (`TicketViewerDialog`), Administración (`GestionPesajesControl`), Centro de Informes BI (`ReportsWorkspaceControl`) y Migrador ODS 9 (`Ods9Migrator`).
  - Migración segura e idempotente (`FirebirdSchemaManager.EnsureAdditionalColumnsAsync`: `FARDOS INTEGER DEFAULT 0` en `DBTRANSITO`, `DBTRAN_ANUL`, `DBPESAJES`), preservando todos los registros históricos y manteniendo compatibilidad total (`-206`-free) frente a servidores Firebird remotos heredados sin columna `FARDOS`.
  - En **Dividir Ticket (`SplitTicketDialog` / `WeighingRepository.SplitTicketAsync`)**, distribución proporcional explícita al Neto (`Fardos 1 + Fardos 2 = Original`) con confirmación obligatoria si el usuario ingresa una suma distinta.
- **Jerarquía Visual Corporativa para `Bruto`, `Tara`, `Neto`, `N° Folio: xxxxxx` y `Tipo de Pesaje`**:
  - Formato uniforme **`N° Folio: xxxxxx`** en negrita (preservando ceros iniciales) y **`Tipo de Pesaje: RECEPCIÓN DE CARGA` / `DESPACHO DE CARGA` / `MOVIMIENTO INTERNO`** destacado sin recortes en PDF, impresión, visor y listados.
  - **Bruto** y **Tara** en negrita; **Neto** en negrita con tamaño de fuente mayor en formularios, visor, impresión, PDF y Excel.
- **Servicio Centralizado de Menú Contextual Universal (`UniversalContextMenuService`)**:
  - Estandarizado en todas las grillas, resultados de búsqueda y formularios aplicables (`PesajeWorkspaceControl`, `GestionPesajesControl`, `ReportsWorkspaceControl` — 6 grillas + panel de gráficos, `UsuariosForm`, `PesajeMopForm`, `ErrorLogViewerDialog`, `CatalogLookupDialog`, `CatalogCrudBaseForm` y `TicketViewerDialog`), ocultando dinámicamente acciones incompatibles o no autorizadas por RBAC.
- **Exportación e Impresión Estandarizada a Página Completa (`StandardTableReport`, `PdfDocumentWriter`, `DataExporter`, `TicketPrinterService`)**:
  - Títulos, criterios de filtros, KPIs, encabezados repetidos por página (`Print_Titles` en Excel y multipágina PDF/Impresión), totales al pie, formato numérico `#,##0` / `000000` y ajuste automático a página (`fitToPage`).

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
