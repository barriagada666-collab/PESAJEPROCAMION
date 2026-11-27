# Guía de Compilación — PesajeProCamion

## Requisitos de Desarrollo
- **Sistema Operativo**: Windows 10 / Windows 11 (x64)
- **SDK**: .NET 10 SDK (`10.0.100+` con carga Desktop WinForms)
- **Motor BD para pruebas locales/remotas**: Firebird 2.5+ (local `localhost:3050` o servidor remoto en red TCP `3050`)
- **Empaquetado**: Inno Setup 7 (`ISCC.exe`)

## Compilación (Debug y Release: 0 Errores, 0 Advertencias)

```powershell
dotnet restore PesajeProCamion.sln
dotnet build PesajeProCamion.sln -c Debug
dotnet build PesajeProCamion.sln -c Release
dotnet test PesajeProCamion.sln -c Debug --no-build
dotnet test PesajeProCamion.sln -c Release --no-build
```

## Publicación Autocontenida (Zero-Dependencias)

### 1. Edición Portable Single-File (`win-x64`)
No requiere .NET instalado ni permisos de administrador en el equipo cliente. Guarda `appsettings.json` y carpeta `Logs\` junto al ejecutable al activar `portable.flag`:

```powershell
dotnet publish src\PesajeProCamion.App\PesajeProCamion.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish\portable-win-x64
```

### 2. Edición Estándar + Instalador Inno Setup 7
```powershell
dotnet publish src\PesajeProCamion.App\PesajeProCamion.App.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64
& "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe" installer\PesajeProCamion.iss
```

Artefactos resultantes en `installer\Output\`:
- `PesajeProCamion_Setup_v2.2.1.exe` (Instalador moderno Inno Setup 7 autocontenido)
- `PesajeProCamion_Portable_v2.2.1_win-x64.zip` (Paquete portable ejecutable sin instalación)
