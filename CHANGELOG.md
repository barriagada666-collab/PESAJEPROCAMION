# Historial de Cambios (CHANGELOG)

## [2.0.0] - 2026-11-27
### Agregado
- Migración completa a **.NET 10 (`net10.0-windows`)** con arquitectura limpia en capas (`Domain`, `Data`, `Hardware`, `App`, `Tests`).
- Lector binario nativo en C# (`Ods9BinaryReader`) y migrador automático (`Ods9Migrator`) para convertir bases legacy **InterBase ODS 9.1 (.GDB)** hacia **Firebird ODS 11.2+ UTF8 Dialecto 3**.
- Interfaz WinForms unificada de pesaje comercial, pesaje por ejes MOP (estático y dinámico), administración, catálogos e informes con íconos vectoriales High-DPI y visor digital de 7 segmentos.
- Seguridad modernizada: cifrado de contraseñas **PBKDF2-SHA256** con actualización transparente en primer login, RBAC centralizado y tabla `DBAUDITLOG`.
- Exportación nativa a **Excel (.xlsx OpenXML)**, **dBASE III (.dbf)** y **CSV UTF-8 BOM** con diálogo de guardado configurable.
- 10 informes completos incluyendo *Tonelaje por Turno*, *Tránsito Eliminados* y *Histórico de Auditoría*.
- Suite automatizada de 28 pruebas xUnit (Unit, Integration, Functional, UI, Regression, Stress).
- Instalador moderno **Inno Setup 7** autocontenido para Windows x64.

### Corregido
- **Crítico SQL (`PA_DEL_PESAJES`)**: corregido bug de anulación masiva sin filtro (`WHERE FLPESAJE = FLPESAJE`) y cálculo negativo de neto en despachos mediante `ABS(BRUTO - TARA)`.
- **Integridad de datos**: prevención de `FLPESAJE NULL`, deduplicación y normalización de patentes con espacios, limpieza de fechas centinela `1899-12-30` y eliminación de huérfanos en `DBMOPDETPJES`.
- **Correcciones UI/UX**: corregidos errores ortográficos (`Excell`, `Salir delSistema`, `Pueto Serial`, `Impirmir`, `CAMIMERO`), etiquetas copiadas entre formularios y opciones muertas en menús.
- **Consultas de informes**: corregidas ordenaciones copiadas (`Por Patente`, `Por Producto`, `Por Transportista`) y eliminadas rutas hardcodeadas `c:\DataLogging.dev\...`.
