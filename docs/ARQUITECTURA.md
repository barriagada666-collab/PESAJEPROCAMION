# Arquitectura Técnica — PesajeProCamion (.NET 10)

## 1. Diagrama de Capas

```mermaid
flowchart TD
    APP["PesajeProCamion.App (WinForms .NET 10 High-DPI | Petrol Teal + Gold)"]
    DOMAIN["PesajeProCamion.Domain (Entidades, TicketCustomization, AnalyticsCalculator, RBAC, MOP, AppLogger)"]
    DATA["PesajeProCamion.Data (FirebirdSql, RemoteResolver, SplitTicket/CRUD, Migrador ODS 9.1)"]
    HW["PesajeProCamion.Hardware (Visor RS-232, Dinámico, Tickets Personalizables, PDF/XLSX/DBF)"]
    FB_LOCAL[("Firebird 2.5+ Local (.FDB UTF8 Dialecto 3)")]
    FB_REMOTE[("Firebird/InterBase Remoto en Red (ej. 192.168.2.56:3050)")]
    LEGACY[("Archivo Legacy ODS 9.1 (.GDB)")]

    APP --> DOMAIN
    APP --> DATA
    APP --> HW
    DATA --> DOMAIN
    HW --> DOMAIN
    DATA --> FB_LOCAL
    DATA --> FB_REMOTE
    DATA -. "Lectura Binaria FileShare.ReadWrite" .-> LEGACY
```

## 2. Proyectos de la Solución

| Proyecto | Responsabilidad |
| :--- | :--- |
| `PesajeProCamion.Domain` | Entidades de pesaje, `TicketFieldVisibility`, `TicketSplitRequest`, `TicketCustomizationHelper`, `AnalyticsCalculator`, validadores chilenos Módulo 11 y patentes (`ChileanValidators`), `WeighingCalculator`, `PasswordHasher`, RBAC (`PermissionService`) y `AppLogger`. |
| `PesajeProCamion.Data` | `RemoteDatabaseResolver` para rutas UNC de red y TCP 3050, `DatabaseCapabilities`, `FirebirdSchemaManager`, `WeighingRepository` (CRUD con neto manual, división atómica `SplitTicketAsync` y folios libres respetando `DBTRANSITO`), `SecurityRepository`, `Ods9BinaryReader` y `Ods9Migrator`. |
| `PesajeProCamion.Hardware` | Parser serial `SerialFrameParser`, `ScaleReaderService`, `DynamicAxleProcessor`, impresión gráfica/RAW y PDF de tickets personalizables por campo (`TicketPrinterService`), `PdfDocumentWriter` y exportación OpenXML `.xlsx` / `.dbf` / `.csv` (`DataExporter`). |
| `PesajeProCamion.App` | `AppTheme`, `IconCatalog`, `AppConfigStore`, `SplashForm`, `LoginForm`, `DatabaseConnectionForm`, `UsuariosForm`, `ErrorLogViewerDialog`, `VisorSetupForm`, `PesajeWorkspaceControl`, `PesajeMopForm`, `GestionPesajesControl`, `TicketEditorDialog`, `TicketViewerDialog`, `SplitTicketDialog`, `ReportsWorkspaceControl` y catálogos CRUD. |
| `PesajeProCamion.Tests` | Suite xUnit automatizada: unitarias, integración Firebird/UNC, funcionales CRUD/folios/división, auditoría UI WinForms STA y pruebas de estrés. |
