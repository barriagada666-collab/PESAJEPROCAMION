# Arquitectura Técnica — PesajeProCamion (.NET 10)

## 1. Diagrama de Capas

```mermaid
flowchart TD
    APP["PesajeProCamion.App (WinForms .NET 10 High-DPI | Petrol Teal + Gold)"]
    DOMAIN["PesajeProCamion.Domain (Entidades, AnalyticsCalculator, RBAC, MOP, AppLogger)"]
    DATA["PesajeProCamion.Data (FirebirdSql, RemoteResolver, Capacidades, Migrador ODS 9.1)"]
    HW["PesajeProCamion.Hardware (Visor RS-232, Dinámico, Tickets + Logo, PDF/XLSX/DBF)"]
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
| `PesajeProCamion.Domain` | Entidades de pesaje y analítica (`AnalyticsModels`), motor de segmentación multidimensional y KPIs (`AnalyticsCalculator`), validadores chilenos Módulo 11 y patentes (`ChileanValidators`), normativa MOP (`WeighingCalculator`), hashing `PBKDF2-SHA256` (`PasswordHasher`), RBAC (`PermissionService`) y diagnóstico (`AppLogger`). |
| `PesajeProCamion.Data` | `RemoteDatabaseResolver` para rutas UNC de red y TCP 3050, detección automática de capacidades `DatabaseCapabilities`, `FirebirdSchemaManager`, `WeighingRepository` (asignación de folios libre de conflicto respetando `DBTRANSITO` y CRUD unificado de tickets), `SecurityRepository` (CRUD RBAC + protección último admin), `Ods9BinaryReader` y `Ods9Migrator`. |
| `PesajeProCamion.Hardware` | Parser serial `SerialFrameParser` (modos STX + CR/LF sin STX), monitor/simulador `ScaleReaderService`, algoritmo dinámico `DynamicAxleProcessor`, impresión gráfica/RAW de tickets + PDF unitario (`TicketPrinterService`), generador nativo PDF 1.4 WinAnsi (`PdfDocumentWriter`) y exportación multi-hoja OpenXML `.xlsx` / `.dbf` / `.csv` (`DataExporter`). |
| `PesajeProCamion.App` | Tema corporativo Petrol Teal + Dorado (`AppTheme`, `IconCatalog`), `SplashForm`, `LoginForm`, `DatabaseConnectionForm`, `UsuariosForm`, `ErrorLogViewerDialog`, `VisorSetupForm`, `PesajeWorkspaceControl`, `PesajeMopForm`, `GestionPesajesControl`, `TicketEditorDialog`, `TicketViewerDialog`, `ReportsWorkspaceControl` y catálogos CRUD. |
| `PesajeProCamion.Tests` | Suite xUnit automatizada: pruebas unitarias (validadores, AnalyticsCalculator, PDF/XLSX), integración real Firebird/UNC, funcionales CRUD/folios sin colisión, auditoría UI WinForms STA y pruebas de estrés. |
