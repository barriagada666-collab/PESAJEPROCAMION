using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Data.Repositories;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Hardware.Export;

namespace PesajeProCamion.App.UI.Management;

/// <summary>Centro unificado de Informes de Producción, Auditoría y Logs (reemplaza formularios rotos y sin cablear).</summary>
public sealed class ReportsWorkspaceControl : UserControl
{
    private readonly AppServices _services;
    private readonly ComboBox _cmbReportType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker _dtDesde = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker _dtHasta = new() { Format = DateTimePickerFormat.Short };
    private readonly TextBox _txtFiltroClave = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblTotals = new();

    private List<string> _currentHeaders = [];
    private List<IReadOnlyList<object?>> _currentRows = [];

    public ReportsWorkspaceControl(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        Dock = DockStyle.Fill;
        BackColor = AppTheme.BackgroundLight;
        BuildUi();
    }

    public async Task GenerateAsync()
    {
        DateTime desde = _dtDesde.Value.Date;
        DateTime hasta = _dtHasta.Value.Date;
        string key = _txtFiltroClave.Text.Trim();
        _grid.Columns.Clear();
        _grid.Rows.Clear();
        _currentHeaders = [];
        _currentRows = [];

        switch (_cmbReportType.SelectedIndex)
        {
            case 0: // Por Cliente
            {
                List<Pesaje> items = await _services.Weighing.QueryPesajesAsync(desde, hasta, clienteRut: string.IsNullOrEmpty(key) ? null : key, incluirAnulados: false);
                PopulatePesajesReport(items.OrderBy(p => p.NombreCliente).ThenBy(p => p.FolioPesaje).ToList(), "Informe por Cliente");
                break;
            }
            case 1: // Por Patente
            {
                List<Pesaje> items = await _services.Weighing.QueryPesajesAsync(desde, hasta, patente: string.IsNullOrEmpty(key) ? null : key, incluirAnulados: false);
                PopulatePesajesReport(items.OrderBy(p => p.Patente).ThenBy(p => p.FolioPesaje).ToList(), "Informe por Patente");
                break;
            }
            case 2: // Por Producto
            {
                List<Pesaje> items = await _services.Weighing.QueryPesajesAsync(desde, hasta, productoCod: string.IsNullOrEmpty(key) ? null : key, incluirAnulados: false);
                PopulatePesajesReport(items.OrderBy(p => p.NombreProducto).ThenBy(p => p.FolioPesaje).ToList(), "Informe por Producto");
                break;
            }
            case 3: // Por Transportista
            {
                List<Pesaje> items = await _services.Weighing.QueryPesajesAsync(desde, hasta, transpRut: string.IsNullOrEmpty(key) ? null : key, incluirAnulados: false);
                PopulatePesajesReport(items.OrderBy(p => p.NombreTransportista).ThenBy(p => p.FolioPesaje).ToList(), "Informe por Transportista");
                break;
            }
            case 4: // Taras de Camiones
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
                    $"Total camiones con tara registrada: {taras.Count:N0}");
                break;
            }
            case 5: // Tonelaje por Turno
            {
                List<ResumenTurno> turnos = await _services.Weighing.GetTonelajePorTurnoAsync(desde, hasta);
                SetGrid(
                    ["Turno", "Descripción", "Pesajes", "Total Bruto (kg)", "Total Tara (kg)", "Total Neto (kg)", "Toneladas Netas"],
                    turnos.Select(t => (IReadOnlyList<object?>)
                    [
                        t.Turno, t.NombreTurno, t.CantidadPesajes, t.TotalBrutoKg, t.TotalTaraKg, t.TotalNetoKg, Math.Round(t.TotalNetoKg / 1000.0, 2)
                    ]).ToList(),
                    $"Total general: {turnos.Sum(t => t.CantidadPesajes):N0} pesajes | {turnos.Sum(t => t.TotalNetoKg) / 1000.0:N2} toneladas");
                break;
            }
            case 6: // Tránsito Eliminados
            {
                List<RegistroTransitoAnulado> anul = await _services.Weighing.GetTransitoAnuladoAsync(desde, hasta);
                SetGrid(
                    ["Folio", "Patente", "Cliente", "Producto", "Bruto", "Tara", "Fecha Anul.", "Hora", "Usuario", "Motivo"],
                    anul.Select(a => (IReadOnlyList<object?>)
                    [
                        a.FolioPesaje, a.Patente, a.Cliente, a.Producto, a.Bruto, a.Tara,
                        a.FechaAnulacion.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), a.HoraAnulacion, a.NombreUsuarioAnulacion, a.Motivo
                    ]).ToList(),
                    $"Registros eliminados de tránsito: {anul.Count:N0}");
                break;
            }
            case 7: // Pesajes MOP
            {
                List<PesajeMop> mops = await _services.Mop.GetPesajesMopAsync(desde, hasta);
                SetGrid(
                    ["Folio MOP", "Fecha", "Hora", "Patente", "Tipo MOP", "Largo", "Bruto (kg)", "Máximo (kg)", "Sobrepeso", "Estado"],
                    mops.Select(m => (IReadOnlyList<object?>)
                    [
                        m.FolioMop, m.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), m.Hora, m.Patente,
                        m.TipoMop, m.Largo, m.Bruto, m.Maximo, m.Sobrepeso, m.Estado.ToString()
                    ]).ToList(),
                    $"Pesajes MOP registrados: {mops.Count:N0}");
                break;
            }
            case 8: // Logs de Ingreso
            {
                List<LogIngreso> logs = await _services.Security.GetLogsIngresoAsync(desde, hasta);
                SetGrid(
                    ["ID", "Fecha", "Hora", "Usuario", "Sistema", "Acción", "Turno"],
                    logs.Select(l => (IReadOnlyList<object?>)
                    [
                        l.IdLog, l.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), l.Hora,
                        l.NombreUsuario, l.DescripcionSistema, l.DescripcionAccion, l.Turno
                    ]).ToList(),
                    $"Total eventos de acceso: {logs.Count:N0}");
                break;
            }
            case 9: // Auditoría Histórica de Modificaciones
            {
                List<AuditLogEntry> audits = await _services.Security.GetAuditLogsAsync(desde, hasta);
                SetGrid(
                    ["ID", "Fecha", "Hora", "Usuario", "Módulo", "Entidad", "Clave", "Acción", "Detalle"],
                    audits.Select(a => (IReadOnlyList<object?>)
                    [
                        a.IdAudit, a.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), a.Hora,
                        a.NombreUsuario, a.Modulo, a.Entidad, a.Clave, a.Accion, a.Detalle
                    ]).ToList(),
                    $"Eventos de auditoría registrados: {audits.Count:N0}");
                break;
            }
        }
    }

    private void BuildUi()
    {
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = AppTheme.SurfaceCard, Padding = new Padding(8, 4, 8, 4) };
        ToolStripButton btnGen = AppTheme.CreateToolButton(AppIcon.Search, "Generar Informe", "Consultar y generar informe", AppTheme.NavyPrimary);
        ToolStripButton btnExcel = AppTheme.CreateToolButton(AppIcon.ExportExcel, "Exportar Excel", "Exportar informe actual a .xlsx", AppTheme.AccentEmerald);
        ToolStripButton btnCsv = AppTheme.CreateToolButton(AppIcon.List, "Exportar CSV", "Exportar informe actual a .csv");
        btnGen.Click += async (_, _) => await GenerateAsync();
        btnExcel.Click += (_, _) => ExportExcel();
        btnCsv.Click += (_, _) => ExportCsv();
        toolbar.Items.AddRange([btnGen, new ToolStripSeparator(), btnExcel, btnCsv]);

        var filterPanel = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = AppTheme.SurfaceCard, Padding = new Padding(12, 10, 12, 8) };
        _cmbReportType.Items.AddRange([
            "1. Pesajes por Cliente",
            "2. Pesajes por Patente",
            "3. Pesajes por Producto",
            "4. Pesajes por Transportista",
            "5. Taras de Camiones",
            "6. Tonelaje por Turno",
            "7. Camiones en Tránsito Eliminados",
            "8. Pesajes por Ejes MOP",
            "9. Logs de Ingreso de Usuarios",
            "10. Histórico de Auditoría y Modificaciones"
        ]);
        _cmbReportType.SelectedIndex = 0;
        _dtDesde.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddYears(-1);
        _dtHasta.Value = DateTime.Today;

        filterPanel.Controls.Add(new Label { Text = "Informe:", Location = new Point(14, 17), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        _cmbReportType.SetBounds(74, 13, 250, 24);
        filterPanel.Controls.Add(new Label { Text = "Desde:", Location = new Point(338, 17), AutoSize = true });
        _dtDesde.SetBounds(386, 13, 108, 24);
        filterPanel.Controls.Add(new Label { Text = "Hasta:", Location = new Point(504, 17), AutoSize = true });
        _dtHasta.SetBounds(548, 13, 108, 24);
        filterPanel.Controls.Add(new Label { Text = "Filtro (RUT/Pat/Cód):", Location = new Point(668, 17), AutoSize = true });
        _txtFiltroClave.SetBounds(794, 13, 125, 24);
        filterPanel.Controls.AddRange([_cmbReportType, _dtDesde, _dtHasta, _txtFiltroClave]);

        AppTheme.StyleDataGrid(_grid);
        _grid.Dock = DockStyle.Fill;

        _lblTotals.Dock = DockStyle.Bottom;
        _lblTotals.Height = 30;
        _lblTotals.BackColor = AppTheme.NavyDark;
        _lblTotals.ForeColor = Color.White;
        _lblTotals.Font = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        _lblTotals.TextAlign = ContentAlignment.MiddleLeft;
        _lblTotals.Padding = new Padding(12, 0, 12, 0);

        Controls.AddRange([_grid, _lblTotals, filterPanel, toolbar]);
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
            $"{title}: {items.Count:N0} pesajes | Total Neto: {totalNeto:N0} kg ({totalNeto / 1000.0:N2} t)");
    }

    private void SetGrid(IReadOnlyList<string> headers, List<IReadOnlyList<object?>> rows, string summary)
    {
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

    private void ExportExcel()
    {
        if (_currentHeaders.Count == 0) return;
        using var sfd = new SaveFileDialog { Filter = "Excel OpenXML|*.xlsx", FileName = $"Informe_{DateTime.Today:yyyyMMdd}.xlsx" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        DataExporter.ExportToXlsx(sfd.FileName, "Informe", _currentHeaders, _currentRows);
        MessageBox.Show(this, $"Informe exportado:\r\n{sfd.FileName}", "Exportar", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
