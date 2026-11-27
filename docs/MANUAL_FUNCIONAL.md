# Manual Funcional — PesajeProCamion v2.1

## 1. Arranque, Inicio de Sesión y Servidor de BD Remota
1. Al iniciar **PesajeProCamion**, se muestra la pantalla corporativa de carga (`v2.1.0`) y luego el **Inicio de Sesión**.
2. Cuenta administradora principal integrada: **`barriagada`** / **`Cambias0`** (puede escribirse en mayúsculas o minúsculas o seleccionarse del listado).
3. Presione **Servidor BD** (en Login) o **Configurar Servidor BD...** (en la barra superior) para conectarse a un servidor local o remoto:
   - Ingrese directamente una ruta compartida de red UNC como `\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB`.
   - Use **Probar Conexión** para validar puerto TCP `3050`, ruta en el servidor, latencia y cantidad de pesajes.
   - Use **Aplicar (Sin Cerrar)** o **Guardar y Conectar** para persistir en `appsettings.json`.
4. Ante cualquier incidencia técnica, abra **Logs de Errores** en el Login, en la barra superior o en *Acerca de* para revisar o exportar `.log`.

## 2. Gestión Completa de Usuarios y Permisos RBAC
1. Ingrese a *Configuración -> Usuarios y Permisos...* (`UsuariosForm`).
2. Filtre usuarios por texto en el buscador superior o haga clic derecho sobre la tabla de usuarios para crear, editar, restablecer contraseña, activar/desactivar, eliminar o exportar la nómina a Excel.
3. Utilice las plantillas rápidas **Admin Total**, **Operador Pesaje** o **Solo Consulta**, o marque individualmente los privilegios (`Agregar`, `Modificar`, `Eliminar`, `Imprimir`, `Sup. Sistema`, `Jefe Turno`) y permisos de ingreso a módulos (`Pesaje` / `Gestión`). El sistema impide eliminar o desactivar el último administrador activo.

## 3. Visor de Báscula RS-232 y Pesaje Comercial
1. En **Pesaje de Camiones -> Visor / COM**, seleccione puerto (`COM1..COM16`), velocidad, paridad y trama (`STX > 0` con posición/largo, o `STX = 0` por línea `CR/LF`). Use **Probar Trama STX** o `/TEST` en modo simulación.
2. Ingrese **Patente** y presione `Tab` (autocompleta datos en tránsito o última tara vigente), complete Cliente/Producto/Origen/Destino/Transportista/Chofer (todos autoresuelven nombre o cuentan con búsqueda rápida y menú contextual por clic derecho), capture pesos con **PESAR CAMIÓN**, **PESAR CARRO / 2°** o **APLICAR TARA REGISTRADA** y presione **GRABAR PESAJE**.
3. Haga clic derecho sobre cualquier camión pendiente en **Camiones En Tránsito** para cargarlo a báscula (2° pesaje), visualizar el ticket en proceso, modificar sus datos o anularlo.

## 4. CRUD de Tickets sin Conflictos de Folio y Visor de Tickets
1. Tanto desde **Pesaje de Camiones -> Crear Ticket Manual** como desde **Administración de Pesajes -> Nuevo Ticket** (o clic derecho), abra `TicketEditorDialog`:
   - Elija **Folio automático seguro** (asignación libre de conflicto que respeta `DBPESAJES`, `DBTRANSITO` En Proceso, `DBTRAN_ANUL` y `DBFOLIOS`) o ingrese un folio manual y valídelo con **Validar Folio**.
   - Active **Crear En Proceso (Tránsito)** si el camión aún permanece en planta sin segundo peso.
2. Haga doble clic o seleccione **Visualizar** (o clic derecho -> *Visualizar Ticket...*) para abrir `TicketViewerDialog` con logotipo corporativo, estado, selector de copias, impresión, **Exportar PDF**, **Modificar Ticket** y **Copiar Texto**.
3. Desde **Administración de Pesajes**, puede completar salidas de registros *En Proceso*, modificar históricos/en proceso, duplicar como nuevo ticket, anular/reactivar y exportar a **PDF**, **Excel (`.xlsx`)**, **DBF** o **CSV**.

## 5. Centro Analítico BI KPI, Segmentación y Reportes
1. En **Informes y Auditoría** (`0. Panel Analítico KPI & Segmentación`), use el selector de fechas (`Desde` / `Hasta`) o los botones rápidos **Hoy**, **7D**, **Mes** y **Todo**.
2. Combine en tiempo real los segmentadores **Patente**, **Proveedor/Origen**, **Cliente**, **Producto**, **Tipo**, **Estado** y **Búsqueda** para recalcular instantáneamente las 5 tarjetas **KPI**, las pestañas segmentadas (`Detalle Filtrado`, `Por Cliente`, `Por Proveedor / Origen`, `Por Producto`, `Por Patente`) y los **Gráficos KPI**.
3. Exporte todo el análisis a **PDF** corporativo o a un libro **Excel (`.xlsx`) de 6 hojas** (`Resumen_KPI`, `Detalle_Pesajes` y resúmenes por cada dimensión), o seleccione cualquiera de los informes clásicos `1..11` (incluyendo MOP, Tonelaje por Turno, Tránsito Eliminado y Logs de Errores).
