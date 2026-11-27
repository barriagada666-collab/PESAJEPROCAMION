# Historial de Cambios (CHANGELOG)

## [2.0.0] - 2026-11-27
### Agregado
- **Identidad Visual Corporativa Petrol Teal + Logo**: Nueva paleta `#082D45` / `#005781` / `#CCA969` / `#DBE4E5`, pantalla de arranque `SplashForm`, logotipo oficial embebido (`logo_cambiaso.png`) e icono multi-resolución.
- **Conexión Remota Inteligente (Rutas UNC y Servidores en Red)**: `RemoteDatabaseResolver` con soporte para rutas `\\IP\recurso\archivo.GDB` (validado contra `\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB`), autodetección de Dialecto (`1` / `3`) y Charset (`UTF8` / `WIN1252` / `NONE`), diagnóstico de capacidades (`DatabaseCapabilities`) y modal mejorado con botones **Probar Conexión**, **Aplicar (Sin Cerrar)** y **Guardar y Conectar**.
- **Visor y Trazabilidad de Errores (`AppLogger` + `ErrorLogViewerDialog`)**: Registro diario rotativo + consola en vivo de errores UI/SQL/Serial exportable a `.log` e integrado como informe #11.
- **Edición Portable Zero-Dependencias + Instalador Inno Setup 7**: Publicación `SingleFile` autocontenida win-x64 portable (`portable.flag` + configuración local junto al EXE) e instalador Inno Setup 7 con gráficas renovadas.
- **Visor RS-232 Ampliado**: Soporte para tramas `STX=0` delimitadas por CR/LF, puertos COM1..COM16, inyector ASCII **Probar Trama STX**, simulación `/TEST` y botones operativos **Pesar Camión / Pesar Carro / Tara Registrada / Grabar**.
- **Diálogo de Copias y Totales en Grilla**: Selector de `Nro de Copias` previo a impresión, diseño mejorado del ticket con logo, y barra inferior de totales (`Bruto`, `Tara`, `Neto`) en Administración de Pesajes.

### Corregido
- **Crítico SQL (`PA_DEL_PESAJES`) y negativos en despachos**: anulación estricta por folio y cálculo simétrico `ABS(BRUTO - TARA)`.
- **Compatibilidad BD Remota Legacy ODS 9.1**: fallback automático cuando no existen generadores `GEN_*`, tabla `DBAUDITLOG`, ni columnas `ID` / `ID_MUESTRA`, y respeto automático a `DBUSUARIOS.PASS_WORD VARCHAR(10)` en servidores compartidos sin provocar errores de truncamiento.
- **Lectura segura ODS 9.1 en archivos bloqueados**: `Ods9BinaryReader` usa `FileShare.ReadWrite`, y `GDESPACHO` ampliado a `VARCHAR(50)`.
- **Autoresolución en pantalla de pesaje**: los campos Cliente, Producto, Origen, Destino, Transportista y Chofer ahora resuelven sus descripciones al digitar el código y presionar `Tab`.
- **Correcciones ortográficas y consultas UI**: corregidos textos heredados (`Excell`, `Salir delSistema`, `Pueto Serial`, `Impirmir`, `CAMIMERO`), ordenamientos de informes y eliminada ruta fija `c:\DataLogging.dev\...`.
