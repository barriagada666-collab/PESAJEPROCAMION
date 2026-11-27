# Guía de Compilación — PesajeProCamion

## Requisitos
- **SDK**: .NET 10 SDK (`10.0.100` o superior con Windows Desktop targeting pack).
- **Sistema Operativo**: Windows 10 / 11 (x64).
- **Motor BD para pruebas de integración**: Firebird 2.5.9 (o 3.x/4.x/5.x) ejecutándose en `localhost:3050`.
- **Empaquetado**: Inno Setup 7 (`ISCC.exe`).

## Comandos de Compilación

```powershell
# Restaurar dependencias
dotnet restore PesajeProCamion.sln

# Compilación Debug (0 errores, 0 advertencias requeridas)
dotnet build PesajeProCamion.sln -c Debug

# Compilación Release (0 errores, 0 advertencias requeridas)
dotnet build PesajeProCamion.sln -c Release

# Ejecutar suite completa de pruebas (Unit, Integration, Functional, UI, Stress)
dotnet test PesajeProCamion.sln -c Release
```

## Publicación Autocontenida e Instalador

```powershell
# Publicación autocontenida Windows x64
dotnet publish src\PesajeProCamion.App\PesajeProCamion.App.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64

# Compilar instalador Inno Setup 7
& "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe" installer\PesajeProCamion.iss
```

El instalador generado se depositará en `installer\Output\PesajeProCamion_Setup_v2.0.0.exe`.
