# Guía de Despliegue y Migración — PesajeProCamion

## 1. Requisitos en Equipo Destino
- Windows 10 / Windows 11 (64 bits).
- Servicio **Firebird 2.5+** instalado localmente o accesible en red TCP puerto `3050`.
- Si instala mediante el paquete `PesajeProCamion_Setup_v2.0.0.exe`, no requiere instalar .NET Runtime previamente (distribución autocontenida).

## 2. Instalación y Actualización
- **Instalación gráfica**: Ejecute `PesajeProCamion_Setup_v2.0.0.exe` y siga el asistente.
- **Instalación silenciosa**:
  ```powershell
  PesajeProCamion_Setup_v2.0.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
  ```
- **Actualización**: Ejecute el nuevo instalador sobre la versión anterior (`AppId` compartido). La base de datos y configuración en `%ProgramData%\PesajeProCamion` se conservan intactas.
- **Desinstalación**: Desde *Agregar o quitar programas* o ejecutando `unins000.exe /VERYSILENT` dentro de la carpeta de instalación.

## 3. Migración desde Base Legacy ODS 9.1 (`.GDB`)
### Opción A — Línea de Comandos
```powershell
PesajeProCamion.App.exe --migrate "C:\ruta\WMDBSPC.GDB" "C:\ProgramData\PesajeProCamion\Data\WMDBSPC.FDB"
```

### Opción B — Asistente Visual
1. Inicie **PesajeProCamion.App.exe** y presione **Base de Datos**.
2. Indique el archivo `.GDB` legacy de origen y la ruta destino `.FDB`.
3. Presione **Migrar GDB Legacy -> FDB** y revise el reporte de saneamiento antes de guardar.

## 4. Modo Simulación (`/TEST`)
Para validar pantallas, tickets y pesaje sin báscula física conectada:
```powershell
PesajeProCamion.App.exe /TEST
```
