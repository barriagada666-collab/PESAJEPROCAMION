# Manual Funcional — PesajeProCamion v2.0

## 1. Inicio de Sesión y Conexión
1. Al iniciar **PesajeProCamion**, seleccione su usuario e ingrese su contraseña (las contraseñas migradas conservan su valor inicial y se actualizan automáticamente a hash `PBKDF2-SHA256` al primer ingreso).
2. Seleccione el módulo deseado (**Pesaje Camiones**, **Gestión e Informes** o **Suite Completa**).
3. Si necesita apuntar a otro servidor Firebird o importar una base legacy `.GDB`, presione **Base de Datos** antes de iniciar sesión.

## 2. Pesaje Comercial (Dos Pasadas o Tara Registrada)
1. En la pestaña **Pesaje de Camiones**, ingrese la **Patente** y presione `Tab` (o use el botón de búsqueda).
   - Si el camión tiene un **Primer Pesaje pendiente** en `DBTRANSITO`, el sistema carga automáticamente sus datos en modo *Segundo Pesaje*.
   - Si es nuevo ingreso, puede seleccionar **Cliente**, **Producto**, **Origen**, **Destino**, **Transportista**, **Chofer** y **Tipo de Pesaje** (*Recepción*, *Despacho*, *Movimiento Interno*).
2. Presione **Capturar Peso** para leer el valor estable del indicador RS-232 (o **Ingreso Manual** en modo contingencia/prueba).
3. Si el camión tiene **Tara Registrada vigente**, presione **Aplicar Tara Reg.** para completar el pesaje en una sola pasada (validando vencimiento en días `NDIAS` y `TARAREG`).
4. Presione **Guardar Pesaje** para registrar ingreso en tránsito o finalizar la salida con ticket comercial.

## 3. Pesaje por Ejes Normativa M.O.P. (Estático y Dinámico)
1. Desde **Operación -> Pesaje por Ejes M.O.P.** (o desde el botón **Pesaje Ejes MOP**), verifique **Tipo MOP** (ej. `510`) y **Largo (m)**.
2. Elija modo **Estático** (captura manual eje por eje) o **Dinámico** (detección automática por umbrales `VMOPUNDER`/`VMOPOVER` y recorte porcentual `VMOPCUTLEFT`/`VMOPCUTRIGHT`).
3. Presione **Iniciar Pesaje** (o **Reproducir Demo** para probar la curva `DEMO1`), capture todos los ejes y revise el semáforo **DENTRO DE NORMA** / **FUERA DE NORMA** según Decreto MOP 158/80 y Ley 19.171/92.
4. Presione **Terminar e Imprimir** para guardar el folio MOP y visualizar el *Boleto Oficial para Control Caminero M.O.P.*.

## 4. Administración de Pesajes, Informes y Catálogos
- **Administración de Pesajes**: filtre por fechas, tipo de operación, patente o folio exacto. Permite **Modificar Datos**, **Anular / Reactivar**, **Imprimir Ticket** y exportar a **Excel (.xlsx)**, **DBF (.dbf)** o **CSV (.csv)**.
- **Informes y Auditoría**: 10 reportes integrados (por Cliente, Patente, Producto, Transportista, Taras de Camiones, Tonelaje por Turno, Tránsito Eliminados, Pesajes MOP, Logs de Ingreso y Auditoría Histórica) exportables en un clic.
- **Catálogos**: alta, edición, búsqueda y eliminación de Camiones, Clientes (validación RUT Módulo 11), Productos, Transportistas, Choferes, Orígenes y Destinos.
