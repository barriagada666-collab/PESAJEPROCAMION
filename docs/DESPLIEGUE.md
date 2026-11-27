# Guía de Despliegue, Conexión Remota y Portabilidad

## 1. Requisitos en Equipo Cliente (Cero Dependencias)
- **Windows 10 / 11 (64 bits)**, resolución mínima `1024x700`.
- **Cero dependencias de .NET ni Excel**: tanto el instalador como la versión Portable son compilaciones **autocontenidas (.NET 10 self-contained)** con exportación nativa integrada a `.pdf`, `.xlsx`, `.dbf` y `.csv`.
- Acceso TCP al puerto `3050` del servidor **Firebird / InterBase** (local `localhost` o equipo en red LAN como `192.168.2.56`).

## 2. Despliegue por Instalador o Modo Portable
- **Instalador Windows (`PesajeProCamion_Setup_v2.1.0.exe`)**:
  ```powershell
  PesajeProCamion_Setup_v2.1.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
  ```
- **Versión Portable (`PesajeProCamion_Portable_v2.1.0_win-x64.zip`)**: descomprimir y ejecutar `PesajeProCamion.App.exe` directamente desde USB o carpeta de red local. Con `portable.flag` o `appsettings.json` junto al ejecutable, toda configuración y logs permanecen portables sin escribir en `Program Files`.

## 3. Credenciales Administrativas e Inicio de Sesión
- Al inicializar una base nueva o migrar un respaldo legacy ODS 9.1, se crea y habilita la cuenta administradora **`barriagada`** (clave **`Cambias0`**) respetando automáticamente el límite `PASS_WORD VARCHAR(10)` en servidores Firebird heredados.
- Desde *Configuración -> Usuarios y Permisos...*, el administrador puede crear operadores, restablecer claves, aplicar plantillas de perfil y gestionar permisos RBAC completos.

## 4. Conexión a Base de Datos Remota (Incluyendo Rutas UNC de Red)
En el modal **Servidor BD** (disponible en el Login y en la barra superior principal):
1. Pegue una ruta compartida UNC de Windows, por ejemplo `\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB`, y presione **Analizar Ruta Red** / **Probar Conexión**.
2. El motor extrae automáticamente host, ruta del servidor, **Dialecto (1 legacy o 3 moderno)**, **Charset (`WIN1252` / `UTF8` / `NONE`)**, latencia, empresa y registros.
3. Use **Aplicar (Sin Cerrar)** o **Guardar y Conectar**, o valide desde consola:
   ```powershell
   PesajeProCamion.App.exe --probe "\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB"
   ```

## 5. Visor RS-232 de Báscula y Diagnóstico
- Desde **Visor / COM** en la barra operativa principal o **Configuración -> Visor Serial / Báscula**: configure `COM1..COM16`, velocidad, paridad, `STX` (`0` para tramas por línea CR/LF), inyecte tramas ASCII con **Probar Trama STX**, o inicie en modo simulación con `/TEST`.
- Desde **Logs de Errores** en barra superior o Login: inspeccione en vivo excepciones UI/SQL, errores COM y trazas diarias almacenadas en `Logs\pesaje_yyyyMMdd.log`.
