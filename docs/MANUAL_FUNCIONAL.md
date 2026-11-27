# Manual Funcional — PesajeProCamion v2.0

## 1. Arranque, Inicio de Sesión y Servidor de BD Remota
1. Al iniciar **PesajeProCamion**, se muestra la pantalla corporativa de carga y luego el **Inicio de Sesión**.
2. Presione **Servidor BD** (en Login) o **Configurar Servidor BD...** (en la barra superior) para conectarse a un servidor local o remoto:
   - Puede ingresar directamente una ruta compartida de red UNC como `\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB`.
   - Use **Probar Conexión** para verificar TCP puerto `3050`, ruta en el servidor, latencia y conteo de pesajes.
   - Use **Aplicar (Sin Cerrar)** para aplicar en vivo o **Guardar y Conectar** para guardar en `appsettings.json`.
3. Ante cualquier incidencia técnica, abra **Logs de Errores** en el Login, en la barra superior o en *Acerca de* para revisar o exportar la traza detallada.

## 2. Visor de Báscula RS-232 y Modo Simulación
1. En **Pesaje de Camiones -> Visor / COM** (o *Configuración -> Visor Serial / Báscula*), seleccione el puerto (`COM1..COM16`), velocidad, paridad y trama (`STX > 0` con posición/largo, o `STX = 0` por salto de línea `CR/LF`).
2. Use **Probar Trama STX** para validar parsing ASCII o **Simular** para pruebas sin balanza física conectada (también mediante `/TEST`).

## 3. Pesaje Comercial (Pesar Camión / Pesar Carro / Tara Registrada)
1. Ingrese **Patente** y presione `Tab` (autocompleta datos de tránsito o última tara del camión).
2. Digite o busque con lupa **Cliente**, **Producto**, **Origen**, **Destino**, **Transportista** y **Chofer** (todos autoresuelven nombre al salir del campo).
3. Capture pesos con **PESAR CAMIÓN**, **PESAR CARRO / 2° PESO** o **APLICAR TARA REGISTRADA**, y presione **GRABAR PESAJE**.

## 4. Pesaje por Ejes M.O.P. y Administración
- En **Pesaje Ejes MOP**: seleccione modo **Estático** o **Dinámico**, ejecute **Reproducir Demo** (`DEMO1`) y obtenga evaluación automática Decreto 158/80 y Ley 19.171/92.
- En **Administración de Pesajes**: filtre por fechas/tipo/folio, revise totales inferiores (**Bruto**, **Tara**, **Neto**), elija **Nro de Copias** al emitir **Ticket de Pesaje** o exporte a `.xlsx`, `.dbf` o `.csv`.
- En **Informes y Auditoría**: 11 informes integrados incluyendo *Tonelaje por Turno*, *Tránsito Eliminados*, *Pesajes MOP*, *Auditoría* y *Logs de Errores*.
