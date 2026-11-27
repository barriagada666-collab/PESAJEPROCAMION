# Manual Funcional — PesajeProCamion v2.2

## 1. Arranque, Inicio de Sesión y Servidor de BD Remota
1. Al iniciar **PesajeProCamion**, se muestra la pantalla corporativa de carga (`v2.2.0`) y luego el **Inicio de Sesión**.
2. Cuenta administradora principal integrada: **`barriagada`** / **`Cambias0`** (puede escribirse en mayúsculas o minúsculas o seleccionarse del listado).
3. Presione **Servidor BD** (en Login) o **Configurar Servidor BD...** (en la barra superior) para conectarse a un servidor local o remoto (ej.: `\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB`). Use **Probar Conexión**, **Aplicar (Sin Cerrar)** o **Guardar y Conectar**.

## 2. Gestión Completa de Usuarios y Permisos RBAC
1. Ingrese a *Configuración -> Usuarios y Permisos...* (`UsuariosForm`).
2. Use el buscador superior, plantillas rápidas (**Admin Total**, **Operador Pesaje**, **Solo Consulta**), restablecimiento de clave con visor **Mostrar clave**, activación/desactivación y menú contextual clic derecho.

## 3. Visor de Báscula RS-232 y Pesaje Comercial
1. En **Pesaje de Camiones -> Visor / COM**, configure `COM1..COM16`, velocidad, paridad y trama (`STX > 0` o `STX = 0` por línea `CR/LF`).
2. Ingrese **Patente**, complete catálogos con lupa/Tab, capture pesos con **PESAR CAMIÓN / PESAR CARRO / APLICAR TARA REGISTRADA** y grabe con **GRABAR PESAJE**. Use clic derecho sobre **Camiones En Tránsito** para cargar, visualizar, modificar o anular.

## 4. Visor Personalizable, Edición en Vivo, Pesos Manuales y División de Tickets
1. **Personalización por Checkboxes (`TicketViewerDialog`)**:
   - Al visualizar cualquier ticket, marque/desmarque libremente los checkboxes de **Campos Visibles del Ticket** (*Patente, Cliente, Producto, Origen, Destino, Transportista, Chofer, Guía Despacho, Fechas/Oper., Observaciones*) o use **Marcar Todos** / **Solo Básicos**. Las preferencias se guardan automáticamente y aplican al visor, impresión, PDF y copia de texto.
   - La parte superior derecha muestra de forma destacada el **Tipo de Pesaje** (`RECEPCIÓN DE CARGA`, `DESPACHO DE CARGA`, `MOVIMIENTO INTERNO`).
   - Edite en el panel derecho la información/pesos en tiempo real y presione **Guardar Cambios en BD** si desea persistirlos.
2. **CRUD con Pesos Manuales (`TicketEditorDialog`)**:
   - Puede ingresar manualmente **Bruto**, **Tara** y **Neto** (activando o desactivando **Auto-calcular Neto**) sin conflictos de folio y respetando vehículos **En Proceso**.
3. **Dividir Ticket en 2 Folios (`SplitTicketDialog`)**:
   - Desde **Administración de Pesajes -> Dividir** (o clic derecho -> *Dividir Ticket en 2...*, o botón **Dividir en 2** en el visor), seleccione un ticket cerrado con Neto ≥ 2 kg, ajuste el reparto (ej.: Neto `10` -> Folio origen `6` + Folio nuevo automático `4`, o presets `50%/50%`, `60%/40%`, `70%/30%`) y confirme. El sistema conserva el folio origen y asigna al segundo ticket un folio libre sin colisión con **En Proceso**.

## 5. Centro Analítico BI KPI, Segmentación y Reportes
1. En **Informes y Auditoría** (`0. Panel Analítico KPI & Segmentación`), filtre con `DatePicker` (**Hoy / 7D / Mes / Todo**) y segmentadores **Patente / Proveedor/Origen / Cliente / Producto / Tipo / Estado**.
2. Exporte a **PDF** corporativo o **Excel `.xlsx` de 6 hojas** sin dependencias externas.
