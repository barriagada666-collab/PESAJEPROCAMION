# Guía de Despliegue, Conexión Remota y Portabilidad

## 1. Requisitos en Equipo Cliente (Cero Dependencias)
- **Windows 10 / 11 (64 bits)**.
- **Cero dependencias de .NET**: tanto el instalador como la versión Portable son compilaciones **autocontenidas (.NET 10 self-contained)**.
- Acceso TCP al puerto `3050` del servidor **Firebird / InterBase** (local `localhost` o equipo en red LAN como `192.168.2.56`).

## 2. Despliegue por Instalador o Modo Portable
- **Instalador Windows (`PesajeProCamion_Setup_v2.0.0.exe`)**:
  ```powershell
  PesajeProCamion_Setup_v2.0.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
  ```
- **Versión Portable (`PesajeProCamion_Portable_v2.0.0_win-x64.zip`)**: descomprimir y ejecutar `PesajeProCamion.App.exe` directamente desde USB o carpeta de red local. Si incluye `portable.flag` o `appsettings.json` junto al ejecutable, toda configuración y logs permanecen portables sin escribir en `Program Files`.

## 3. Conexión a Base de Datos Remota (Incluyendo Rutas UNC de Red)
En el modal **Servidor BD** (disponible en el Login y en la barra superior principal):
1. Puede pegar directamente una ruta compartida UNC de Windows, por ejemplo:
   ```text
   \\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB
   ```
   y presionar **Analizar Ruta Red** / **Probar Conexión**.
2. El motor de resolución extrae automáticamente el host (`192.168.2.56`), evalúa los candidatos de ruta del servidor (`/bdwm/placilla2/WMDBSPC.GDB`, `C:\bdwm\...`, etc.), detecta **Dialecto (1 legacy o 3 moderno)**, **Charset (`WIN1252` / `UTF8` / `NONE`)**, latencia, empresa y cantidad de registros.
3. Use **Aplicar (Sin Cerrar)** para activar la conexión al instante o **Guardar y Conectar** para persistirla en `appsettings.json`.
4. También puede validarse desde línea de comandos sin interfaz:
   ```powershell
   PesajeProCamion.App.exe --probe "\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB"
   ```

## 4. Visor RS-232 de Báscula y Diagnóstico
- Desde **Visor / COM** en la barra operativa principal o **Configuración -> Visor Serial / Báscula**: configure `COM1..COM16`, velocidad, paridad, `STX` (`0` para tramas por línea CR/LF), inyecte tramas ASCII con **Probar Trama STX**, o inicie en modo simulación con `/TEST`.
- Desde **Logs de Errores** en barra superior o Login: inspeccione en vivo excepciones UI/SQL, errores COM y trazas diarias almacenadas en `Logs\pesaje_yyyyMMdd.log`.
