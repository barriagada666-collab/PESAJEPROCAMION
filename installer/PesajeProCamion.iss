; ============================================================================
; PesajeProCamion - Script de Instalador Inno Setup 7
; Versión: 2.2.0 | Plataforma: .NET 10 Windows Desktop (x64) + Firebird SQL
; ============================================================================

#ifndef PublishDir
  #define PublishDir "..\publish\win-x64"
#endif

#ifndef AppVersion
  #define AppVersion "2.2.0"
#endif

[Setup]
AppId={{8F9E3A21-7D4C-4B89-9E2A-4D5F6A7B8C9D}
AppName=PesajeProCamion
AppVersion={#AppVersion}
AppVerName=PesajeProCamion {#AppVersion}
AppPublisher=PesajeProCamion
AppPublisherURL=https://github.com/barriagada666-collab/PESAJEPROCAMION
AppSupportURL=https://github.com/barriagada666-collab/PESAJEPROCAMION
DefaultDirName={autopf}\PesajeProCamion
DefaultGroupName=PesajeProCamion
AllowNoIcons=yes
OutputDir=Output
OutputBaseFilename=PesajeProCamion_Setup_v{#AppVersion}
SetupIconFile=..\src\PesajeProCamion.App\Assets\app.ico
WizardImageFile=assets\wizard-banner.bmp
WizardSmallImageFile=assets\wizard-small.bmp
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=force
RestartApplications=no
UninstallDisplayIcon={app}\PesajeProCamion.App.exe
UninstallDisplayName=PesajeProCamion - Sistema de Pesaje de Camiones
VersionInfoVersion=2.2.0.0
VersionInfoCompany=PesajeProCamion
VersionInfoDescription=Instalador del Sistema de Pesaje de Camiones
VersionInfoProductName=PesajeProCamion
VersionInfoProductVersion=2.2.0.0
InfoBeforeFile=..\CHANGELOG.md

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "testshortcut"; Description: "Crear acceso directo en Modo Prueba (/TEST)"; GroupDescription: "Opciones Adicionales"; Flags: unchecked

[Dirs]
Name: "{commonappdata}\PesajeProCamion\Data"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\CHANGELOG.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\PesajeProCamion"; Filename: "{app}\PesajeProCamion.App.exe"; Comment: "Sistema Integral de Pesaje de Camiones"
Name: "{group}\PesajeProCamion (Modo Prueba)"; Filename: "{app}\PesajeProCamion.App.exe"; Parameters: "/TEST"; Comment: "Iniciar con visor simulado"
Name: "{group}\Documentación"; Filename: "{app}\docs\README.md"
Name: "{group}\{cm:UninstallProgram,PesajeProCamion}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\PesajeProCamion"; Filename: "{app}\PesajeProCamion.App.exe"; Tasks: desktopicon; Comment: "Sistema Integral de Pesaje de Camiones"
Name: "{autodesktop}\PesajeProCamion (Prueba)"; Filename: "{app}\PesajeProCamion.App.exe"; Parameters: "/TEST"; Tasks: testshortcut; Comment: "Iniciar en modo simulación"

[Run]
Filename: "{app}\PesajeProCamion.App.exe"; Description: "{cm:LaunchProgram,PesajeProCamion}"; Flags: nowait postinstall skipifsilent unchecked
