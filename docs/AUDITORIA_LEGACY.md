# Auditoría Técnica del Sistema Legacy (Base de Datos y Ejecutables)

Este documento resume los hallazgos empíricos obtenidos al auditar:
- **Base de datos**: `placillaPRUEBA\WMDBSPC.GDB` (7,918,592 bytes) y comparativa contra `Data\WMDBSPC.GDB` y `Data\db.sql`.
- **Ejecutables**: `WeighMaster.exe` (3,545,600 bytes) y `WMGestion.exe` (3,355,136 bytes) compilados en Delphi 5 + BDE.

---

## 1. Hallazgos en Base de Datos (`placillaPRUEBA\WMDBSPC.GDB`)

| Aspecto | Estado Legacy | Remediación en PesajeProCamion (.NET 10) |
| :--- | :--- | :--- |
| **Versión ODS** | InterBase **ODS 9.1**, Dialecto 1, página de 1024 B (incompatible con Firebird 2.5+). | Migración automática a **Firebird ODS 11.2, Dialecto 3, página 8192 B, charset UTF8**. |
| **Brecha de Transacciones** | OIT `448560` vs Next `515614` (brecha de `67,054` transacciones sin sweep). | Migración limpia sin versiones fantasma y con manejo explícito de transacciones `ReadCommitted`. |
| **Registro corrupto en `DBPESAJES`** | 1 fila de 2,735 con `FLPESAJE = NULL` (ausencia de `PRIMARY KEY NOT NULL`). | Excluida durante la migración y prevención mediante `FLPESAJE INTEGER NOT NULL PRIMARY KEY`. |
| **Duplicados en `DBCAMIONES`** | 22 patentes con espacios iniciales/finales (`' DLBT45'`) generando duplicados al hacer `Trim()`. | Normalización de patentes (`NormalizarPatente`) consolidando 2,725 filas en **2,701 patentes únicas**. |
| **Fechas centinela** | 38 registros con `FTARAREG = 1899-12-30`. | Saneamiento automático a `NULL`. |
| **Huérfanos MOP** | 6 registros en `DBMOPDETPJES` para `FLMOP=1` sin cabecera en `DBMOPPJES`. | Limpieza en migración e integridad relacional en `DBMOPDETPJES`. |
| **Seguridad de Usuarios** | 4 usuarios con `PASS_WORD` en texto plano (`VARCHAR(6)`). | Hash criptográfico **PBKDF2-SHA256** (100,000 iteraciones) con salto aleatorio y rehash automático. |
| **Bug crítico en `PA_DEL_PESAJES`** | `WHERE FLPESAJE = FLPESAJE` (sin `:`) reactivaba **todos** los pesajes y calculaba `BRUTO - TARA` negativo en despachos. | Anulación por parámetro estricto `@FP` y cálculo simétrico `ABS(BRUTO - TARA)`. |

---

## 2. Hallazgos en Ejecutables (`WeighMaster.exe` & `WMGestion.exe`)

1. **Dependencia Obsoleta BDE (`IDAPI32.DLL`)**: Conexión acoplada a configuración de máquina local (`E:\pesajefinal.CFG`) apuntando a servidor remoto. Reemplazada por configuración JSON portable y proveedor nativo `FirebirdSql.Data.FirebirdClient`.
2. **Errores Tipográficos e Inconsistencias UI**:
   - Login: `'Salir delSistema'`, etiqueta partida `'Nombre de '` / `'Usuario'`.
   - Configuración Visor: `'Pueto Serial'`.
   - Listados/Informes: `'Impirmir listado'`, `'Exportar a Excell'`, `'CAMIMERO M.O.P.'`.
   - Administración Pesajes: título de ventana `'.'`.
   - Orígenes y Destinos: etiqueta copiada `'Producto'`.
   - Barras/Menús: botones `'New Item'` sin texto ni acción, Hints intercambiados entre `Clientes` y `En Tránsito`.
3. **Menús Muertos / Consultas con Copy-Paste**:
   - `TfrmInfPjePatente`, `TfrmInfPjeProd` y `TfrmInfPjeTransp` ordenaban erróneamente por `CLIENTE, FLPESAJE` y restaban `BRUTO - TARA` en lugar de usar `NETO` real.
   - `Tonelaje por Turno`, `Camiones en Tránsito Eliminados` y `Registro Histórico de Modificaciones` estaban deshabilitados/sin cablear en `WMGestion.exe`.
4. **Rutas Hardcodeadas**: Exportación `.DBF` fija a `c:\DataLogging.dev\WeighMaster.comaco\data\EXPORT.dbf`. Reemplazada por diálogo estándar del sistema para `.xlsx`, `.dbf` y `.csv`.
