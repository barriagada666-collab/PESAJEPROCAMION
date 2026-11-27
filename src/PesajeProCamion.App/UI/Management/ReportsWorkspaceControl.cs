using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Data.Repositories;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Services;
using PesajeProCamion.Hardware.Export;

namespace PesajeProCamion.App.UI.Management;

/// <summary>
/// Centro Analítico BI e Informes de Producción, KPIs, Segmentación Multidimensional
/// (Patente, Proveedor/Origen, Cliente, Producto, DatePicker) y Exportación PDF/XLSX/CSV.
/// </summary>
public sealed class ReportsWorkspaceControl : UserControl
{
    private readonly AppServices _services;
    private readonly ToolTip _tt = new();
    private readonly ComboBox _cmbReportType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker _dtDesde = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker _dtHasta = new() { Format = DateTimePickerFormat.Short };

    // Slicers de segmentación de datos
    private readonly ComboBox _cmbPatente = new() { DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
    private readonly ComboBox _cmbProveedor = new() { DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
    private readonly ComboBox _cmbCliente = new() { DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
    private readonly ComboBox _cmbProducto = new() { DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
    private readonly ComboBox _cmbTipoOp = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbEstado = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtFiltroClave = new();

    // KPI cards
    private readonly Panel _kpiBand = new();
    private readonly Label[] _kpiValues = new Label[5];
    private readonly Label[] _kpiSubs = new Label[5];

    // Vistas BI vs Grilla clásica
    private readonly TabControl _biTabs = new();
    private readonly DataGridView _grid = new();
    private readonly DataGridView _gridCliente = new();
    private readonly DataGridView _gridProveedor = new();
    private readonly DataGridView _gridProducto = new();
    private readonly DataGridView _gridPatente = new();
    private readonly Panel _chartCanvas = new();
    private readonly Label _lblTotals = new();

    private List<Pesaje> _rawRangeCache = [];
    private AnalyticsResult? _lastAnalytics;
    private List<string> _currentHeaders = [];
    private List<IReadOnlyList<object?>> _currentRows = [];
    private string _currentTitle = "Informe de Pesaje";
    private bool _suppressReactive;

    public ReportsWorkspaceControl(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        Dock = DockStyle.Fill;
        BackColor = AppTheme.BackgroundLight;
        BuildUi();
        Load += async (_, _) => await AppTheme.SafeLoadAsync(this, nameof(ReportsWorkspaceControl), GenerateAsync);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tt.Dispose();
        }
        base.Dispose(disposing);
    }

    public async Task GenerateAsync()
    {
        DateTime desde = _dtDesde.Value.Date;
        DateTime hasta = _dtHasta.Value.Date;
        int reportIdx = _cmbReportType.SelectedIndex;

        bool isAnalyticsMode = reportIdx == 0;
        _kpiBand.Visible = isAnalyticsMode;
        _biTabs.Visible = isAnalyticsMode;
        _grid.Visible = !isAnalyticsMode;

        if (isAnalyticsMode)
        {
            _rawRangeCache = await _services.Weighing.QueryPesajesAsync(
                desde: desde,
                hasta: hasta,
                incluirAnulados: true,
                maxRows: 50000);
            PopulateSlicerDropdowns(_rawRangeCache);
            ApplyAnalyticsFilters();
            return;
        }

        string key = _txtFiltroClave.Text.Trim();
        _grid.Columns.Clear();
        _grid.Rows.Clear();
        _currentHeaders = [];
        _currentRows = [];

        switch (reportIdx)
        {
            case 1:
            {
                List<Pesaje> items = await _services.Weighing.QueryPesajesAsync(desde, hasta, clienteRut: string.IsNullOrEmpty(key) ? null : key, incluirAnulados: false);
                PopulatePesajesReport(items.OrderBy(p => p.NombreCliente).ThenBy(p => p.FolioPesaje).ToList(), "Informe por Cliente");
                break;
            }
            case 2:
            {
                List<Pesaje> items = await _services.Weighing.QueryPesajesAsync(desde, hasta, patente: string.IsNullOrEmpty(key) ? null : key, incluirAnulados: false);
                PopulatePesajesReport(items.OrderBy(p => p.Patente).ThenBy(p => p.FolioPesaje).ToList(), "Informe por Patente");
                break;
            }
            case 3:
            {
                List<Pesaje> items = await _services.Weighing.QueryPesajesAsync(desde, hasta, productoCod: string.IsNullOrEmpty(key) ? null : key, incluirAnulados: false);
                PopulatePesajesReport(items.OrderBy(p => p.NombreProducto).ThenBy(p => p.FolioPesaje).ToList(), "Informe por Producto");
                break;
            }
            case 4:
            {
                List<Pesaje> items = await _services.Weighing.QueryPesajesAsync(desde, hasta, transpRut: string.IsNullOrEmpty(key) ? null : key, incluirAnulados: false);
                PopulatePesajesReport(items.OrderBy(p => p.NombreTransportista).ThenBy(p => p.FolioPesaje).ToList(), "Informe por Transportista");
                break;
            }
            case 5:
            {
                List<TaraCamionReporte> taras = await _services.Weighing.GetTarasCamionesReporteAsync(soloTarareg: !string.IsNullOrEmpty(key));
                SetGrid(
                    ["Patente", "Acoplado", "Transportista", "Chofer", "Tara (kg)", "Fecha Tara", "Hora", "Tara Reg.", "Vencimiento (días)"],
                    taras.Select(t => (IReadOnlyList<object?>)
                    [
                        t.Patente, t.Acoplado, t.NombreTransportista, t.NombreChofer,
                        t.TaraKg, t.FechaTara?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "-",
                        t.HoraTara, t.TaraRegistrada ? "Sí" : "No", t.DiasValidez
                    ]).ToList(),
                    $"Total camiones con tara registrada: {taras.Count:N0}",
                    "Informe de Taras de Camiones");
                break;
            }
            case 6:
            {
                List<ResumenTurno> turnos = await _services.Weighing.GetTonelajePorTurnoAsync(desde, hasta);
                SetGrid(
                    ["Turno", "Descripción", "Pesajes", "Total Bruto (kg)", "Total Tara (kg)", "Total Neto (kg)", "Toneladas Netas"],
                    turnos.Select(t => (IReadOnlyList<object?>)
                    [
                        t.Turno, t.NombreTurno, t.CantidadPesajes, t.TotalBrutoKg, t.TotalTaraKg, t.TotalNetoKg, Math.Round(t.TotalNetoKg / 1000.0, 2)
                    ]).ToList(),
                    $"Total general: {turnos.Sum(t => t.CantidadPesajes):N0} pesajes | {turnos.Sum(t => t.TotalNetoKg) / 1000.0:N2} toneladas",
                    "Tonelaje por Turno");
                break;
            }
            case 7:
            {
                List<RegistroTransitoAnulado> anul = await _services.Weighing.GetTransitoAnuladoAsync(desde, hasta);
                SetGrid(
                    ["Folio", "Patente", "Cliente", "Producto", "Bruto", "Tara", "Fecha Anul.", "Hora", "Usuario", "Motivo"],
                    anul.Select(a => (IReadOnlyList<object?>)
                    [
                        a.FolioPesaje, a.Patente, a.Cliente, a.Producto, a.Bruto, a.Tara,
                        a.FechaAnulacion.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), a.HoraAnulacion, a.NombreUsuarioAnulacion, a.Motivo
                    ]).ToList(),
                    $"Registros eliminados de tránsito: {anul.Count:N0}",
                    "Camiones en Tránsito Eliminados");
                break;
            }
            case 8:
            {
                List<PesajeMop> mops = await _services.Mop.GetPesajesMopAsync(desde, hasta);
                SetGrid(
                    ["Folio MOP", "Fecha", "Hora", "Patente", "Tipo MOP", "Largo", "Bruto (kg)", "Máximo (kg)", "Sobrepeso", "Estado"],
                    mops.Select(m => (IReadOnlyList<object?>)
                    [
                        m.FolioMop, m.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), m.Hora, m.Patente,
                        m.TipoMop, m.Largo, m.Bruto, m.Maximo, m.Sobrepeso, m.Estado.ToString()
                    ]).ToList(),
                    $"Pesajes MOP registrados: {mops.Count:N0}",
                    "Pesajes por Ejes MOP");
                break;
            }
            case 9:
            {
                List<LogIngreso> logs = await _services.Security.GetLogsIngresoAsync(desde, hasta);
                SetGrid(
                    ["ID", "Fecha", "Hora", "Usuario", "Sistema", "Acción", "Turno"],
                    logs.Select(l => (IReadOnlyList<object?>)
                    [
                        l.IdLog, l.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), l.Hora,
                        l.NombreUsuario, l.DescripcionSistema, l.DescripcionAccion, l.Turno
                    ]).ToList(),
                    $"Total eventos de acceso: {logs.Count:N0}",
                    "Logs de Ingreso de Usuarios");
                break;
            }
            case 10:
            {
                List<AuditLogEntry> audits = await _services.Security.GetAuditLogsAsync(desde, hasta);
                if (audits.Count == 0)
                {
                    IReadOnlyList<Domain.Diagnostics.AppLogEntry> localEvents = Domain.Diagnostics.AppLogger.GetEntries()
                        .Where(l => string.Equals(l.Source, "AUDIT", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    SetGrid(
                        ["Fecha / Hora", "Nivel", "Módulo", "Evento de Auditoría", "Detalle"],
                        localEvents.Select(l => (IReadOnlyList<object?>)
                        [
                            l.Timestamp.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                            l.Level.ToString(), l.Source, l.Message, l.Detail ?? string.Empty
                        ]).ToList(),
                        $"Auditoría en BD: 0 (o BD remota legacy sin DBAUDITLOG) | Eventos locales: {localEvents.Count:N0}",
                        "Histórico de Auditoría Local");
                    break;
                }
                SetGrid(
                    ["ID", "Fecha", "Hora", "Usuario", "Módulo", "Entidad", "Clave", "Acción", "Detalle"],
                    audits.Select(a => (IReadOnlyList<object?>)
                    [
                        a.IdAudit, a.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), a.Hora,
                        a.NombreUsuario, a.Modulo, a.Entidad, a.Clave, a.Accion, a.Detalle
                    ]).ToList(),
                    $"Eventos de auditoría registrados: {audits.Count:N0}",
                    "Histórico de Auditoría");
                break;
            }
            case 11:
            {
                IReadOnlyList<Domain.Diagnostics.AppLogEntry> errLogs = Domain.Diagnostics.AppLogger.GetEntries();
                SetGrid(
                    ["Fecha / Hora", "Nivel", "Origen", "Mensaje", "Detalle"],
                    errLogs.Select(l => (IReadOnlyList<object?>)
                    [
                        l.Timestamp.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                        l.Level.ToString().ToUpperInvariant(), l.Source, l.Message, l.Detail ?? string.Empty
                    ]).ToList(),
                    $"Eventos de diagnóstico / errores registrados: {errLogs.Count:N0}",
                    "Logs de Errores del Sistema");
                break;
            }
        }
    }

    private void BuildUi()
    {
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = AppTheme.SurfaceCard, Padding = new Padding(8, 4, 8, 4), CanOverflow = true };
        ToolStripButton btnGen = AppTheme.CreateToolButton(AppIcon.Search, "Generar / Analizar", "Consultar rango de fechas y recalcular KPIs y segmentación", AppTheme.NavyPrimary);
        ToolStripButton btnClear = AppTheme.CreateToolButton(AppIcon.Refresh, "Limpiar Filtros", "Restaurar todos los segmentadores y fechas", AppTheme.AccentAmber);
        ToolStripButton btnPdf = AppTheme.CreateToolButton(AppIcon.Pdf, "Exportar PDF", "Exportar análisis / informe actual a PDF profesional", AppTheme.AccentDanger);
        ToolStripButton btnExcel = AppTheme.CreateToolButton(AppIcon.ExportExcel, "Exportar Excel (.xlsx)", "Exportar libro multi-hoja (KPI + Detalle + Segmentación) a Excel", AppTheme.AccentEmerald);
        ToolStripButton btnCsv = AppTheme.CreateToolButton(AppIcon.List, "Exportar CSV", "Exportar tabla actual a .csv");

        btnGen.Click += async (_, _) => await GenerateAsync();
        btnClear.Click += async (_, _) => await ResetFiltersAsync();
        btnPdf.Click += (_, _) => ExportPdf();
        btnExcel.Click += (_, _) => ExportExcel();
        btnCsv.Click += (_, _) => ExportCsv();
        toolbar.Items.AddRange([btnGen, btnClear, new ToolStripSeparator(), btnPdf, btnExcel, btnCsv]);

        // Barra responsiva estructurada en 2 filas exactas: Fecha + Presets | Segmentación Multidimensional
        var filterPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 76,
            BackColor = AppTheme.SurfaceCard,
            Padding = new Padding(8, 4, 8, 4)
        };
        var filterRows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        filterRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        filterRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        filterRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));

        _cmbReportType.Items.AddRange([
            "0. Panel Analítico KPI & Segmentación (Todos)",
            "1. Pesajes por Cliente",
            "2. Pesajes por Patente",
            "3. Pesajes por Producto",
            "4. Pesajes por Transportista",
            "5. Taras de Camiones",
            "6. Tonelaje por Turno",
            "7. Camiones en Tránsito Eliminados",
            "8. Pesajes por Ejes MOP",
            "9. Logs de Ingreso de Usuarios",
            "10. Histórico de Auditoría y Modificaciones",
            "11. Logs de Errores y Diagnóstico del Sistema"
        ]);
        _cmbReportType.SelectedIndex = 0;
        _cmbReportType.SelectedIndexChanged += async (_, _) => { if (!_suppressReactive) await GenerateAsync(); };

        _dtDesde.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddYears(-1);
        _dtHasta.Value = DateTime.Today;
        _dtDesde.ValueChanged += async (_, _) => { if (!_suppressReactive) await GenerateAsync(); };
        _dtHasta.ValueChanged += async (_, _) => { if (!_suppressReactive) await GenerateAsync(); };

        var btnHoy = MakeQuickRangeBtn("Hoy", () => { SetDateRange(DateTime.Today, DateTime.Today); });
        var btn7D = MakeQuickRangeBtn("7D", () => { SetDateRange(DateTime.Today.AddDays(-6), DateTime.Today); });
        var btnMes = MakeQuickRangeBtn("Mes", () => { SetDateRange(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today); });
        var btnTodo = MakeQuickRangeBtn("Todo", () => { SetDateRange(new DateTime(2015, 1, 1), DateTime.Today); });

        var row1 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 10, RowCount = 1, Margin = Padding.Empty };
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102f));
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102f));
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row1.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _cmbReportType.Dock = DockStyle.Fill; _cmbReportType.Margin = new Padding(2, 3, 6, 0);
        _dtDesde.Dock = DockStyle.Fill; _dtDesde.Margin = new Padding(2, 3, 6, 0);
        _dtHasta.Dock = DockStyle.Fill; _dtHasta.Margin = new Padding(2, 3, 6, 0);
        foreach (Button qb in new[] { btnHoy, btn7D, btnMes, btnTodo })
        {
            qb.Dock = DockStyle.None;
            qb.Height = 28;
            qb.Margin = new Padding(2, 2, 2, 2);
        }

        row1.Controls.Add(MakeFilterLbl("Informe:"), 0, 0); row1.Controls.Add(_cmbReportType, 1, 0);
        row1.Controls.Add(MakeFilterLbl("Desde:"), 2, 0); row1.Controls.Add(_dtDesde, 3, 0);
        row1.Controls.Add(MakeFilterLbl("Hasta:"), 4, 0); row1.Controls.Add(_dtHasta, 5, 0);
        row1.Controls.Add(btnHoy, 6, 0); row1.Controls.Add(btn7D, 7, 0);
        row1.Controls.Add(btnMes, 8, 0); row1.Controls.Add(btnTodo, 9, 0);

        _cmbPatente.Items.Add("(Todas)"); _cmbPatente.SelectedIndex = 0;
        _cmbProveedor.Items.Add("(Todos)"); _cmbProveedor.SelectedIndex = 0;
        _cmbCliente.Items.Add("(Todos)"); _cmbCliente.SelectedIndex = 0;
        _cmbProducto.Items.Add("(Todos)"); _cmbProducto.SelectedIndex = 0;
        _cmbTipoOp.Items.AddRange(["Todos", "Recepción", "Despacho", "Mov. Interno"]); _cmbTipoOp.SelectedIndex = 0;
        _cmbEstado.Items.AddRange(["Activos", "Todos", "Anulados"]); _cmbEstado.SelectedIndex = 0;
        _txtFiltroClave.PlaceholderText = "Texto / Guía...";

        foreach (ComboBox cb in new[] { _cmbPatente, _cmbProveedor, _cmbCliente, _cmbProducto, _cmbTipoOp, _cmbEstado })
        {
            cb.SelectedIndexChanged += (_, _) => ApplyReactiveIfReady();
            cb.TextChanged += (_, _) => ApplyReactiveIfReady();
            cb.Dock = DockStyle.Fill;
            cb.Margin = new Padding(2, 3, 5, 0);
        }
        _txtFiltroClave.Dock = DockStyle.Fill; _txtFiltroClave.Margin = new Padding(2, 3, 0, 0);
        _txtFiltroClave.TextChanged += (_, _) => ApplyReactiveIfReady();

        var row2 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 14, RowCount = 1, Margin = Padding.Empty };
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14f)); // Patente
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f)); // Prov/Orig
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f)); // Cliente
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18f)); // Producto
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88f)); // Tipo
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82f)); // Estado
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f)); // Buscar
        row2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        row2.Controls.Add(MakeFilterLbl("Patente:"), 0, 0); row2.Controls.Add(_cmbPatente, 1, 0);
        row2.Controls.Add(MakeFilterLbl("Prov/Orig:"), 2, 0); row2.Controls.Add(_cmbProveedor, 3, 0);
        row2.Controls.Add(MakeFilterLbl("Cliente:"), 4, 0); row2.Controls.Add(_cmbCliente, 5, 0);
        row2.Controls.Add(MakeFilterLbl("Producto:"), 6, 0); row2.Controls.Add(_cmbProducto, 7, 0);
        row2.Controls.Add(MakeFilterLbl("Tipo:"), 8, 0); row2.Controls.Add(_cmbTipoOp, 9, 0);
        row2.Controls.Add(MakeFilterLbl("Estado:"), 10, 0); row2.Controls.Add(_cmbEstado, 11, 0);
        row2.Controls.Add(MakeFilterLbl("Buscar:"), 12, 0); row2.Controls.Add(_txtFiltroClave, 13, 0);

        filterRows.Controls.Add(row1, 0, 0);
        filterRows.Controls.Add(row2, 0, 1);
        filterPanel.Controls.Add(filterRows);

        // Banda de 5 Tarjetas KPI Responsivas
        _kpiBand.Dock = DockStyle.Top;
        _kpiBand.Height = 82;
        _kpiBand.Padding = new Padding(10, 6, 10, 6);
        var kpiLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
        for (int i = 0; i < 5; i++) kpiLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
        kpiLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        string[] kpiTitles = ["TOTAL TICKETS", "NETO TOTAL (t)", "BRUTO / TARA ACUM.", "PROMEDIO NETO", "LÍDERES SEGMENTO"];
        Color[] kpiAccents = [AppTheme.NavyPrimary, AppTheme.AccentEmerald, AppTheme.NavyDark, AppTheme.AccentAmber, AppTheme.GoldAccent];
        for (int i = 0; i < 5; i++)
        {
            var card = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.SurfaceCard, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(i == 0 ? 0 : 4, 0, i == 4 ? 0 : 4, 0) };
            var strip = new Panel { Dock = DockStyle.Top, Height = 4, BackColor = kpiAccents[i] };
            var lblT = new Label { Dock = DockStyle.Top, Height = 18, Padding = new Padding(8, 2, 6, 0), Text = kpiTitles[i], ForeColor = AppTheme.TextMuted, Font = new Font("Segoe UI", 7.75f, FontStyle.Bold) };
            _kpiValues[i] = new Label { Dock = DockStyle.Top, Height = 26, Padding = new Padding(8, 0, 6, 0), Text = "-", ForeColor = AppTheme.NavyDark, Font = new Font("Segoe UI", 12f, FontStyle.Bold), AutoEllipsis = true };
            _kpiSubs[i] = new Label { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 6, 2), Text = "-", ForeColor = AppTheme.TextMuted, Font = new Font("Segoe UI", 7.75f), AutoEllipsis = true };
            card.Controls.AddRange([_kpiSubs[i], _kpiValues[i], lblT, strip]);
            kpiLayout.Controls.Add(card, i, 0);
        }
        _kpiBand.Controls.Add(kpiLayout);

        // Grilla clásica para modos 1..11 (oculta en modo 0 BI)
        AppTheme.StyleDataGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Visible = false;

        // Contenedor BI con 6 pestañas (Detalle, Cliente, Proveedor/Origen, Producto, Patente, Gráficos KPI)
        _biTabs.Dock = DockStyle.Fill;
        _biTabs.Font = AppTheme.FontBodyBold;

        var detalleGrid = new DataGridView();
        var tDetalle = BuildSegmentTab("Detalle Filtrado", detalleGrid,
            ("Folio", 56), ("Fecha", 76), ("Patente", 68), ("Tipo", 76), ("Cliente", 140), ("Proveedor/Origen", 130), ("Producto", 135), ("Bruto (kg)", 72), ("Tara (kg)", 72), ("Neto (kg)", 76), ("Estado", 68));
        detalleGrid.Name = "_detalleBiGrid";
        detalleGrid.Tag = _grid; // reuse reference pattern cleanly via _detalleBiGrid field? Keep dedicated context menu below!
        AttachDetailBiEvents(detalleGrid);

        var tCli = BuildSegmentTab("Por Cliente", _gridCliente, ("Código", 85), ("Cliente", 230), ("Tickets", 70), ("Bruto (kg)", 90), ("Tara (kg)", 90), ("Neto (kg)", 95), ("Toneladas", 85), ("Prom. kg", 85), ("% Neto", 70));
        var tProv = BuildSegmentTab("Por Proveedor / Origen", _gridProveedor, ("Código", 85), ("Proveedor / Origen / Transp.", 230), ("Tickets", 70), ("Bruto (kg)", 90), ("Tara (kg)", 90), ("Neto (kg)", 95), ("Toneladas", 85), ("Prom. kg", 85), ("% Neto", 70));
        var tProd = BuildSegmentTab("Por Producto", _gridProducto, ("Código", 85), ("Producto", 230), ("Tickets", 70), ("Bruto (kg)", 90), ("Tara (kg)", 90), ("Neto (kg)", 95), ("Toneladas", 85), ("Prom. kg", 85), ("% Neto", 70));
        var tPat = BuildSegmentTab("Por Patente", _gridPatente, ("Patente", 90), ("Vehículo", 170), ("Tickets", 70), ("Bruto (kg)", 90), ("Tara (kg)", 90), ("Neto (kg)", 95), ("Toneladas", 85), ("Prom. kg", 85), ("% Neto", 70));

        var tCharts = new TabPage("Gráficos KPI") { BackColor = AppTheme.BackgroundLight };
        _chartCanvas.Dock = DockStyle.Fill;
        _chartCanvas.BackColor = AppTheme.SurfaceCard;
        _chartCanvas.Paint += PaintAnalyticsCharts;
        _chartCanvas.Resize += (_, _) => _chartCanvas.Invalidate();
        tCharts.Controls.Add(_chartCanvas);

        _biTabs.TabPages.AddRange([tDetalle, tCli, tProv, tProd, tPat, tCharts]);

        _lblTotals.Dock = DockStyle.Bottom;
        _lblTotals.Height = 30;
        _lblTotals.BackColor = AppTheme.NavyDark;
        _lblTotals.ForeColor = Color.White;
        _lblTotals.Font = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        _lblTotals.TextAlign = ContentAlignment.MiddleLeft;
        _lblTotals.Padding = new Padding(12, 0, 12, 0);

        Controls.AddRange([_biTabs, _grid, _lblTotals, _kpiBand, filterPanel, toolbar]);
    }

    private DataGridView? _detalleBiGrid;

    private static Label MakeFilterLbl(string text)
        => new() { Text = text, AutoSize = true, Margin = new Padding(6, 7, 2, 0), Font = new Font("Segoe UI", 8.75f, FontStyle.Bold) };

    private Button MakeQuickRangeBtn(string text, Action onClick)
    {
        var b = new Button { Size = new Size(56, 28), Margin = new Padding(2, 2, 2, 0) };
        AppTheme.StyleSecondaryButton(b, AppIcon.Filter, text, $"Aplicar rango rápido: {text}", AppTheme.NavyPrimary, _tt);
        b.Click += (_, _) => onClick();
        return b;
    }

    private async void SetDateRange(DateTime d, DateTime h)
    {
        _suppressReactive = true;
        _dtDesde.Value = d;
        _dtHasta.Value = h;
        _suppressReactive = false;
        await GenerateAsync();
    }

    private TabPage BuildSegmentTab(string title, DataGridView g, params (string Header, int Weight)[] cols)
    {
        AppTheme.StyleDataGrid(g);
        g.Dock = DockStyle.Fill;
        foreach ((string h, int w) in cols)
        {
            g.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = h, FillWeight = w, ReadOnly = true });
        }
        var tab = new TabPage(title) { BackColor = AppTheme.SurfaceCard };
        tab.Controls.Add(g);
        return tab;
    }

    private void AttachDetailBiEvents(DataGridView detalleGrid)
    {
        _detalleBiGrid = detalleGrid;
        detalleGrid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && detalleGrid.Rows[e.RowIndex].Tag is Pesaje p)
            {
                using var viewer = new TicketViewerDialog(_services, p);
                if (viewer.ShowDialog(this) == DialogResult.OK || viewer.Modified)
                {
                    await GenerateAsync();
                }
            }
        };
        detalleGrid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                detalleGrid.ClearSelection();
                detalleGrid.Rows[e.RowIndex].Selected = true;
                detalleGrid.CurrentCell = detalleGrid.Rows[e.RowIndex].Cells[0];
            }
        };

        var cms = new ContextMenuStrip();
        AppTheme.StyleContextMenu(cms);
        var mView = new ToolStripMenuItem("Visualizar Ticket...", IconCatalog.Get(AppIcon.ViewTicket, 16, AppTheme.NavyPrimary));
        mView.Click += async (_, _) =>
        {
            if (detalleGrid.CurrentRow?.Tag is Pesaje p)
            {
                using var dlg = new TicketViewerDialog(_services, p);
                if (dlg.ShowDialog(this) == DialogResult.OK || dlg.Modified) await GenerateAsync();
            }
        };
        var mPdf = new ToolStripMenuItem("Exportar Análisis a PDF...", IconCatalog.Get(AppIcon.Pdf, 16, AppTheme.AccentDanger));
        mPdf.Click += (_, _) => ExportPdf();
        var mXlsx = new ToolStripMenuItem("Exportar Libro KPI a Excel (.xlsx)", IconCatalog.Get(AppIcon.ExportExcel, 16, AppTheme.AccentEmerald));
        mXlsx.Click += (_, _) => ExportExcel();
        var mFilterPat = new ToolStripMenuItem("Segmentar por esta Patente", IconCatalog.Get(AppIcon.Filter, 16, AppTheme.NavyPrimary));
        mFilterPat.Click += (_, _) =>
        {
            if (detalleGrid.CurrentRow?.Tag is Pesaje p && !string.IsNullOrEmpty(p.Patente)) _cmbPatente.Text = p.Patente;
        };
        cms.Items.AddRange([mView, mFilterPat, new ToolStripSeparator(), mPdf, mXlsx]);
        detalleGrid.ContextMenuStrip = cms;
    }

    private void PopulateSlicerDropdowns(List<Pesaje> rows)
    {
        _suppressReactive = true;
        try
        {
            FillComboKeepingSelection(_cmbPatente, rows.Select(r => r.Patente).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
            FillComboKeepingSelection(_cmbProveedor,
                rows.SelectMany(r => new[] { r.NombreOrigen, r.NombreTransportista })
                    .Where(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x.Trim(), "(SIN ORIGEN)", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x));
            FillComboKeepingSelection(_cmbCliente, rows.Select(r => r.NombreCliente).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
            FillComboKeepingSelection(_cmbProducto, rows.Select(r => r.NombreProducto).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
        }
        finally
        {
            _suppressReactive = false;
        }
    }

    private static void FillComboKeepingSelection(ComboBox cmb, IEnumerable<string> values)
    {
        string prev = cmb.Text;
        cmb.Items.Clear();
        cmb.Items.Add("(Todos)");
        foreach (string v in values) cmb.Items.Add(v.Trim());
        int idx = cmb.FindStringExact(prev);
        cmb.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void ApplyReactiveIfReady()
    {
        if (!_suppressReactive && _cmbReportType.SelectedIndex == 0 && _rawRangeCache.Count > 0)
        {
            ApplyAnalyticsFilters();
        }
    }

    private ReportFilterCriteria BuildCriteria() => new()
    {
        Desde = _dtDesde.Value.Date,
        Hasta = _dtHasta.Value.Date,
        Patente = _cmbPatente.Text,
        ProveedorOrigen = _cmbProveedor.Text,
        Cliente = _cmbCliente.Text,
        Producto = _cmbProducto.Text,
        TipoPesaje = _cmbTipoOp.SelectedIndex switch
        {
            1 => TipoPesaje.Recepcion,
            2 => TipoPesaje.Despacho,
            3 => TipoPesaje.MovimientoInterno,
            _ => null
        },
        Estado = _cmbEstado.SelectedIndex switch
        {
            1 => EstadoTicketFiltro.Todos,
            2 => EstadoTicketFiltro.SoloAnulados,
            _ => EstadoTicketFiltro.SoloActivos
        },
        TextoLibre = _txtFiltroClave.Text
    };

    private void ApplyAnalyticsFilters()
    {
        ReportFilterCriteria criteria = BuildCriteria();
        AnalyticsResult res = AnalyticsCalculator.FilterAndAnalyze(_rawRangeCache, criteria);
        _lastAnalytics = res;

        ReportKpiSummary k = res.Kpis;
        _kpiValues[0].Text = $"{k.TotalTickets:N0}";
        _kpiSubs[0].Text = $"Activos: {k.TicketsActivos:N0} | Anul.: {k.TicketsAnulados:N0}";

        _kpiValues[1].Text = $"{k.ToneladasNetas:N2} t";
        _kpiSubs[1].Text = $"{k.TotalNetoKg:N0} kg acumulados";

        _kpiValues[2].Text = $"{k.TotalBrutoKg / 1000.0:N1} t Br.";
        _kpiSubs[2].Text = $"Tara: {k.TotalTaraKg / 1000.0:N1} t";

        _kpiValues[3].Text = $"{k.PromedioNetoKg:N0} kg";
        _kpiSubs[3].Text = $"Máx: {k.MaximoNetoKg:N0} | Mín: {k.MinimoNetoKg:N0}";

        _kpiValues[4].Text = k.TopPatente;
        _kpiSubs[4].Text = $"Cli: {k.TopCliente}";

        if (_detalleBiGrid is not null)
        {
            _detalleBiGrid.Rows.Clear();
            foreach (Pesaje p in res.Filtrados.Take(5000))
            {
                int idx = _detalleBiGrid.Rows.Add(
                    p.FolioPesaje,
                    p.FechaSalida.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    p.Patente,
                    p.TipoPesaje.ToString(),
                    p.NombreCliente,
                    string.IsNullOrWhiteSpace(p.NombreOrigen) ? p.NombreTransportista : p.NombreOrigen,
                    p.NombreProducto,
                    $"{p.Bruto:N0}",
                    $"{p.Tara:N0}",
                    $"{p.Neto:N0}",
                    p.Anulado ? "ANULADO" : "ACTIVO");
                _detalleBiGrid.Rows[idx].Tag = p;
                if (p.Anulado) _detalleBiGrid.Rows[idx].DefaultCellStyle.ForeColor = AppTheme.AccentDanger;
            }
        }

        FillSegmentGrid(_gridCliente, res.PorCliente);
        FillSegmentGrid(_gridProveedor, res.PorProveedorOrigen);
        FillSegmentGrid(_gridProducto, res.PorProducto);
        FillSegmentGrid(_gridPatente, res.PorPatente);
        _chartCanvas.Invalidate();

        _currentTitle = "Panel Analítico KPI y Segmentación de Pesajes";
        _currentHeaders = ["Folio", "Fecha", "Patente", "Tipo", "Cliente", "Proveedor/Origen", "Producto", "Bruto (kg)", "Tara (kg)", "Neto (kg)", "Estado"];
        _currentRows = res.Filtrados.Select(p => (IReadOnlyList<object?>)
        [
            p.FolioPesaje, p.FechaSalida.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), p.Patente,
            p.TipoPesaje.ToString(), p.NombreCliente, string.IsNullOrWhiteSpace(p.NombreOrigen) ? p.NombreTransportista : p.NombreOrigen,
            p.NombreProducto, p.Bruto, p.Tara, p.Neto, p.Anulado ? "ANULADO" : "ACTIVO"
        ]).ToList();

        _lblTotals.Text = $"  BI Segmentación: {k.TotalTickets:N0} tickets | Neto: {k.TotalNetoKg:N0} kg ({k.ToneladasNetas:N2} t) | Top Patente: {k.TopPatente} | Top Producto: {k.TopProducto}";
    }

    private static void FillSegmentGrid(DataGridView grid, IReadOnlyList<SegmentSummaryRow> rows)
    {
        grid.Rows.Clear();
        foreach (SegmentSummaryRow r in rows)
        {
            grid.Rows.Add(
                r.Clave,
                r.Nombre,
                r.Tickets.ToString("N0", CultureInfo.InvariantCulture),
                r.TotalBrutoKg.ToString("N0", CultureInfo.InvariantCulture),
                r.TotalTaraKg.ToString("N0", CultureInfo.InvariantCulture),
                r.TotalNetoKg.ToString("N0", CultureInfo.InvariantCulture),
                r.ToneladasNetas.ToString("N2", CultureInfo.InvariantCulture),
                r.PromedioNetoKg.ToString("N0", CultureInfo.InvariantCulture),
                $"{r.PorcentajeNeto:N1}%");
        }
    }

    private void PaintAnalyticsCharts(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(AppTheme.SurfaceCard);

        if (_lastAnalytics is null)
        {
            g.DrawString("Presione 'Generar / Analizar' para visualizar gráficos KPI.", AppTheme.FontBodyBold, Brushes.Gray, 24, 24);
            return;
        }

        int w = _chartCanvas.ClientSize.Width;
        int h = _chartCanvas.ClientSize.Height;
        if (w < 200 || h < 120) return;

        int mid = w / 2;
        DrawHorizontalBars(g, new Rectangle(16, 14, mid - 24, h - 28), "Top Clientes por Tonelaje Neto (t)", _lastAnalytics.PorCliente.Take(8).ToList(), AppTheme.NavyPrimary);
        DrawHorizontalBars(g, new Rectangle(mid + 8, 14, w - mid - 24, h - 28), "Top Productos por Tonelaje Neto (t)", _lastAnalytics.PorProducto.Take(8).ToList(), AppTheme.GoldAccent);
    }

    private static void DrawHorizontalBars(Graphics g, Rectangle bounds, string title, IReadOnlyList<SegmentSummaryRow> items, Color barColor)
    {
        using var borderPen = new Pen(AppTheme.BorderSubtle);
        g.DrawRectangle(borderPen, bounds);
        using var titleBrush = new SolidBrush(AppTheme.NavyDark);
        using var barBrush = new SolidBrush(barColor);
        using var lblFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var txtBrush = new SolidBrush(AppTheme.TextPrimary);

        g.DrawString(title, new Font("Segoe UI", 10f, FontStyle.Bold), titleBrush, bounds.X + 12, bounds.Y + 10);
        if (items.Count == 0)
        {
            g.DrawString("Sin datos para el segmento actual.", AppTheme.FontBody, Brushes.Gray, bounds.X + 12, bounds.Y + 42);
            return;
        }

        double maxVal = Math.Max(0.01, items.Max(x => x.ToneladasNetas));
        int topY = bounds.Y + 40;
        int rowH = Math.Min(36, Math.Max(22, (bounds.Height - 54) / items.Count));
        int labelW = Math.Min(180, (int)(bounds.Width * 0.36));
        int chartW = Math.Max(60, bounds.Width - labelW - 95);

        for (int i = 0; i < items.Count; i++)
        {
            SegmentSummaryRow item = items[i];
            int y = topY + i * rowH;
            string name = item.Nombre.Length > 22 ? item.Nombre[..20] + ".." : item.Nombre;
            g.DrawString(name, lblFont, txtBrush, bounds.X + 10, y + 3);

            int bw = (int)Math.Max(4, chartW * (item.ToneladasNetas / maxVal));
            g.FillRectangle(barBrush, bounds.X + labelW, y + 3, bw, Math.Max(12, rowH - 10));
            g.DrawString($"{item.ToneladasNetas:N1} t ({item.PorcentajeNeto:N0}%)", lblFont, titleBrush, bounds.X + labelW + bw + 6, y + 3);
        }
    }

    private async Task ResetFiltersAsync()
    {
        _suppressReactive = true;
        _dtDesde.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddYears(-1);
        _dtHasta.Value = DateTime.Today;
        _cmbPatente.SelectedIndex = 0; _cmbPatente.Text = "(Todos)";
        _cmbProveedor.SelectedIndex = 0; _cmbProveedor.Text = "(Todos)";
        _cmbCliente.SelectedIndex = 0; _cmbCliente.Text = "(Todos)";
        _cmbProducto.SelectedIndex = 0; _cmbProducto.Text = "(Todos)";
        _cmbTipoOp.SelectedIndex = 0;
        _cmbEstado.SelectedIndex = 0;
        _txtFiltroClave.Text = string.Empty;
        _suppressReactive = false;
        await GenerateAsync();
    }

    private void PopulatePesajesReport(List<Pesaje> items, string title)
    {
        long totalNeto = items.Sum(x => (long)x.Neto);
        SetGrid(
            ["Folio", "Fecha", "Hora", "Patente", "Cliente", "Producto", "Transportista", "Bruto (kg)", "Tara (kg)", "Neto (kg)"],
            items.Select(p => (IReadOnlyList<object?>)
            [
                p.FolioPesaje, p.FechaSalida.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), p.HoraSalida,
                p.Patente, p.NombreCliente, p.NombreProducto, p.NombreTransportista, p.Bruto, p.Tara, p.Neto
            ]).ToList(),
            $"{title}: {items.Count:N0} pesajes | Total Neto: {totalNeto:N0} kg ({totalNeto / 1000.0:N2} t)",
            title);
    }

    private void SetGrid(IReadOnlyList<string> headers, List<IReadOnlyList<object?>> rows, string summary, string title)
    {
        _currentTitle = title;
        _currentHeaders = headers.ToList();
        _currentRows = rows;
        foreach (string h in headers)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = h, ReadOnly = true });
        }
        foreach (IReadOnlyList<object?> r in rows)
        {
            object[] formatted = r.Select(v => v is int iv ? iv.ToString("N0", CultureInfo.InvariantCulture) : v ?? string.Empty).ToArray();
            _grid.Rows.Add(formatted);
        }
        _lblTotals.Text = "  " + summary;
    }

    private void ExportPdf()
    {
        if (_currentHeaders.Count == 0 && _lastAnalytics is null) return;
        using var sfd = new SaveFileDialog { Filter = "Documento PDF|*.pdf", FileName = $"Informe_PesajeProCamion_{DateTime.Today:yyyyMMdd}.pdf" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        IReadOnlyList<(string Label, string Value)> kpis = _cmbReportType.SelectedIndex == 0 && _lastAnalytics is { } a
            ? [
                ("Total Tickets", $"{a.Kpis.TotalTickets:N0}"),
                ("Total Neto", $"{a.Kpis.ToneladasNetas:N2} t ({a.Kpis.TotalNetoKg:N0} kg)"),
                ("Promedio / Ticket", $"{a.Kpis.PromedioNetoKg:N0} kg"),
                ("Top Patente", a.Kpis.TopPatente),
                ("Top Cliente", a.Kpis.TopCliente)
              ]
            : [("Resumen", _lblTotals.Text.Trim())];

        string sub = $"Período: {_dtDesde.Value:dd/MM/yyyy} al {_dtHasta.Value:dd/MM/yyyy}  |  Pat: {_cmbPatente.Text}  |  Cli: {_cmbCliente.Text}  |  Prod: {_cmbProducto.Text}";
        DataExporter.ExportToPdf(sfd.FileName, _currentTitle.ToUpperInvariant(), sub, kpis, _currentHeaders, _currentRows);
        MessageBox.Show(this, $"Informe PDF exportado correctamente:\r\n{sfd.FileName}", "Exportar PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportExcel()
    {
        if (_cmbReportType.SelectedIndex == 0 && _lastAnalytics is not null)
        {
            using var sfd = new SaveFileDialog { Filter = "Excel OpenXML|*.xlsx", FileName = $"Analisis_KPI_Pesajes_{DateTime.Today:yyyyMMdd}.xlsx" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            DataExporter.ExportAnalyticsWorkbookToXlsx(sfd.FileName, BuildCriteria(), _lastAnalytics, _lastAnalytics.Filtrados);
            MessageBox.Show(this, $"Libro Excel analítico (6 hojas KPI + segmentación) exportado:\r\n{sfd.FileName}", "Exportar Excel", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentHeaders.Count == 0) return;
        using var sfdStd = new SaveFileDialog { Filter = "Excel OpenXML|*.xlsx", FileName = $"Informe_{DateTime.Today:yyyyMMdd}.xlsx" };
        if (sfdStd.ShowDialog(this) != DialogResult.OK) return;
        DataExporter.ExportToXlsx(sfdStd.FileName, "Informe", _currentHeaders, _currentRows);
        MessageBox.Show(this, $"Informe exportado:\r\n{sfdStd.FileName}", "Exportar", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportCsv()
    {
        if (_currentHeaders.Count == 0) return;
        using var sfd = new SaveFileDialog { Filter = "CSV UTF-8|*.csv", FileName = $"Informe_{DateTime.Today:yyyyMMdd}.csv" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        List<IReadOnlyList<string?>> strRows = _currentRows.Select(r => (IReadOnlyList<string?>)r.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).ToList()).ToList();
        DataExporter.ExportToCsv(sfd.FileName, _currentHeaders, strRows);
        MessageBox.Show(this, $"Informe exportado:\r\n{sfd.FileName}", "Exportar", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
