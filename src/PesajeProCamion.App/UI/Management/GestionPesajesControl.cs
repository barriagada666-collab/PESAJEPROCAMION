using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;
using PesajeProCamion.Hardware.Export;
using PesajeProCamion.Hardware.Printing;

namespace PesajeProCamion.App.UI.Management;

/// <summary>Administración de pesajes históricos (título, exportación Excel/DBF/CSV y anulación corregidos).</summary>
public sealed class GestionPesajesControl : UserControl
{
    private readonly AppServices _services;
    private readonly DateTimePicker _dtDesde = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker _dtHasta = new() { Format = DateTimePickerFormat.Short };
    private readonly ComboBox _cmbTipo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtBuscar = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblResumen = new();
    private List<Pesaje> _rows = [];

    public GestionPesajesControl(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        Dock = DockStyle.Fill;
        BackColor = AppTheme.BackgroundLight;
        BuildUi();
    }

    public async Task RefreshAsync()
    {
        TipoPesaje? tipo = _cmbTipo.SelectedIndex switch
        {
            1 => TipoPesaje.Recepcion,
            2 => TipoPesaje.Despacho,
            3 => TipoPesaje.MovimientoInterno,
            _ => null
        };

        int? folioExacto = int.TryParse(_txtBuscar.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int fParsed) ? fParsed : null;
        string? pat = folioExacto is null && !string.IsNullOrWhiteSpace(_txtBuscar.Text) ? _txtBuscar.Text.Trim() : null;

        _rows = await _services.Weighing.QueryPesajesAsync(
            desde: folioExacto.HasValue ? null : _dtDesde.Value.Date,
            hasta: folioExacto.HasValue ? null : _dtHasta.Value.Date,
            tipoPesaje: tipo,
            patente: pat,
            folioExacto: folioExacto,
            incluirAnulados: true,
            maxRows: 10000);

        RenderGrid();
    }

    private void BuildUi()
    {
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = AppTheme.SurfaceCard, Padding = new Padding(8, 4, 8, 4) };
        ToolStripButton btnConsultar = AppTheme.CreateToolButton(AppIcon.Search, "Consultar", "Aplicar filtros de fecha/tipo/folio", AppTheme.NavyPrimary);
        ToolStripButton btnModificar = AppTheme.CreateToolButton(AppIcon.Settings, "Modificar Datos", "Editar datos del pesaje seleccionado", AppTheme.NavyDark);
        ToolStripButton btnAnular = AppTheme.CreateToolButton(AppIcon.Delete, "Anular / Reactivar", "Alternar estado anulado del folio seleccionado", AppTheme.AccentDanger);
        ToolStripButton btnImprimir = AppTheme.CreateToolButton(AppIcon.Print, "Ticket de Pesaje", "Elegir copias, previsualizar e imprimir ticket", AppTheme.NavyPrimary);
        ToolStripButton btnExcel = AppTheme.CreateToolButton(AppIcon.ExportExcel, "Exportar a Excel", "Exportar listado a Excel (.xlsx)", AppTheme.AccentEmerald);
        ToolStripButton btnDbf = AppTheme.CreateToolButton(AppIcon.ExportDbf, "Exportar a DBF", "Exportar tabla dBASE III (.dbf)", AppTheme.AccentAmber);
        ToolStripButton btnCsv = AppTheme.CreateToolButton(AppIcon.List, "Exportar a CSV", "Exportar a CSV UTF-8", AppTheme.TextPrimary);

        btnConsultar.Click += async (_, _) => await RefreshAsync();
        btnModificar.Click += async (_, _) => await ModifySelectedAsync();
        btnAnular.Click += async (_, _) => await ToggleAnularSelectedAsync();
        btnImprimir.Click += (_, _) => PrintSelected();
        btnExcel.Click += (_, _) => ExportExcel();
        btnDbf.Click += (_, _) => ExportDbf();
        btnCsv.Click += (_, _) => ExportCsv();

        toolbar.Items.AddRange([
            btnConsultar, new ToolStripSeparator(),
            btnModificar, btnAnular, btnImprimir, new ToolStripSeparator(),
            btnExcel, btnDbf, btnCsv
        ]);

        var filterPanel = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = AppTheme.SurfaceCard, Padding = new Padding(12, 10, 12, 8) };
        _dtDesde.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddYears(-1);
        _dtHasta.Value = DateTime.Today;
        _cmbTipo.Items.AddRange(["Todos", "Recepción", "Despacho", "Mov. Interno"]);
        _cmbTipo.SelectedIndex = 0;

        filterPanel.Controls.Add(new Label { Text = "Desde:", Location = new Point(14, 17), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        _dtDesde.SetBounds(68, 13, 110, 24);
        filterPanel.Controls.Add(new Label { Text = "Hasta:", Location = new Point(190, 17), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        _dtHasta.SetBounds(240, 13, 110, 24);
        filterPanel.Controls.Add(new Label { Text = "Tipo:", Location = new Point(365, 17), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        _cmbTipo.SetBounds(406, 13, 130, 24);
        filterPanel.Controls.Add(new Label { Text = "Folio o Patente:", Location = new Point(550, 17), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        _txtBuscar.SetBounds(664, 13, 150, 24);
        _txtBuscar.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await RefreshAsync(); } };

        filterPanel.Controls.AddRange([_dtDesde, _dtHasta, _cmbTipo, _txtBuscar]);

        AppTheme.StyleDataGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.AddRange([
            new DataGridViewTextBoxColumn { HeaderText = "Folio", FillWeight = 58 },
            new DataGridViewTextBoxColumn { HeaderText = "Fecha", FillWeight = 78 },
            new DataGridViewTextBoxColumn { HeaderText = "Hora", FillWeight = 52 },
            new DataGridViewTextBoxColumn { HeaderText = "Patente", FillWeight = 70 },
            new DataGridViewTextBoxColumn { HeaderText = "Tipo", FillWeight = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "Cliente", FillWeight = 160 },
            new DataGridViewTextBoxColumn { HeaderText = "Producto", FillWeight = 150 },
            new DataGridViewTextBoxColumn { HeaderText = "Bruto (kg)", FillWeight = 78 },
            new DataGridViewTextBoxColumn { HeaderText = "Tara (kg)", FillWeight = 78 },
            new DataGridViewTextBoxColumn { HeaderText = "Neto (kg)", FillWeight = 82 },
            new DataGridViewTextBoxColumn { HeaderText = "Estado", FillWeight = 72 }
        ]);
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await ModifySelectedAsync(); };

        _lblResumen.Dock = DockStyle.Bottom;
        _lblResumen.Height = 30;
        _lblResumen.BackColor = AppTheme.NavyDark;
        _lblResumen.ForeColor = Color.White;
        _lblResumen.Font = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        _lblResumen.TextAlign = ContentAlignment.MiddleLeft;
        _lblResumen.Padding = new Padding(12, 0, 12, 0);

        Controls.AddRange([_grid, _lblResumen, filterPanel, toolbar]);
    }

    private void RenderGrid()
    {
        _grid.Rows.Clear();
        long sumBruto = 0;
        long sumTara = 0;
        long sumNeto = 0;
        int activos = 0;
        foreach (Pesaje p in _rows)
        {
            int idx = _grid.Rows.Add(
                p.FolioPesaje,
                p.FechaSalida.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                p.HoraSalida,
                p.Patente,
                p.TipoPesaje.ToString(),
                p.NombreCliente,
                p.NombreProducto,
                $"{p.Bruto:N0}",
                $"{p.Tara:N0}",
                $"{p.Neto:N0}",
                p.Anulado ? "ANULADO" : "ACTIVO");
            _grid.Rows[idx].Tag = p;
            if (p.Anulado)
            {
                _grid.Rows[idx].DefaultCellStyle.ForeColor = AppTheme.AccentDanger;
            }
            else
            {
                activos++;
                sumBruto += p.Bruto;
                sumTara += p.Tara;
                sumNeto += p.Neto;
            }
        }
        _lblResumen.Text = $"  Registros: {_rows.Count:N0} ({activos:N0} activos)   |   Total Bruto: {sumBruto:N0} kg   |   Total Tara: {sumTara:N0} kg   |   Total Neto: {sumNeto:N0} kg ({sumNeto / 1000.0:N2} t)";
    }

    private Pesaje? GetSelected() => _grid.CurrentRow?.Tag as Pesaje;

    private async Task ModifySelectedAsync()
    {
        Pesaje? p = GetSelected();
        if (p is null) return;
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ModificarRegistro))
        {
            MessageBox.Show(this, "No está autorizado para modificar registros de pesaje.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dlg = new ModPesajeDialog(_services, p);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            await RefreshAsync();
        }
    }

    private async Task ToggleAnularSelectedAsync()
    {
        Pesaje? p = GetSelected();
        if (p is null)
        {
            MessageBox.Show(this, "No se ha seleccionado un pesaje para anular.", "Administración", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AnularPesaje))
        {
            MessageBox.Show(this, "No está autorizado para anular pesajes.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string msg = p.Anulado ? $"¿Reactivar el folio #{p.FolioPesaje}?" : $"¿Anular el folio #{p.FolioPesaje} ({p.Patente})?";
        if (MessageBox.Show(this, msg, "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        await _services.Weighing.ToggleAnularPesajeAsync(p.FolioPesaje, _services.UsuarioActual?.Id ?? 0);
        await RefreshAsync();
    }

    private void PrintSelected()
    {
        Pesaje? p = GetSelected();
        if (p is null)
        {
            MessageBox.Show(this, "Seleccione un pesaje del listado para imprimir.", "Ticket de Pesaje", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ImprimirTicket))
        {
            MessageBox.Show(this, "No está autorizado para imprimir tickets.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var copDlg = new Dialogs.TicketCopiesDialog(_services.ConfigActual.NroCopiasTicket);
        if (copDlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        using PrintDocument doc = TicketPrinterService.CreateCommercialTicketDocument(
            p, _services.BasculaActual, _services.ConfigActual, copDlg.Copies, IconCatalog.GetCorporateLogo());
        using var prev = new PrintPreviewDialog { Document = doc, Width = 840, Height = 640 };
        prev.ShowDialog(this);
    }

    private void ExportExcel()
    {
        using var sfd = new SaveFileDialog { Filter = "Excel OpenXML|*.xlsx", FileName = $"Pesajes_{DateTime.Today:yyyyMMdd}.xlsx" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        string[] headers = ["Folio", "Fecha", "Hora", "Patente", "Acoplado", "Tipo", "Cliente", "Producto", "Bruto", "Tara", "Neto", "Anulado"];
        List<IReadOnlyList<object?>> rows = _rows.Select(p => (IReadOnlyList<object?>)
        [
            p.FolioPesaje, p.FechaSalida.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), p.HoraSalida,
            p.Patente, p.Acoplado, p.TipoPesaje.ToString(), p.NombreCliente, p.NombreProducto,
            p.Bruto, p.Tara, p.Neto, p.Anulado ? "SI" : "NO"
        ]).ToList();
        DataExporter.ExportToXlsx(sfd.FileName, "Pesajes", headers, rows);
        MessageBox.Show(this, $"Archivo Excel generado correctamente:\r\n{sfd.FileName}", "Exportar Excel", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportDbf()
    {
        using var sfd = new SaveFileDialog { Filter = "dBASE III DBF|*.dbf", FileName = $"EXPORT_{DateTime.Today:yyyyMMdd}.dbf" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        (string, int)[] cols =
        [
            ("FLPESAJE", 10), ("PATENTE", 8), ("PRODUCTO", 12), ("NMPROD", 40),
            ("CLIENTE", 10), ("NMCLIENTE", 40), ("FECHASAL", 10), ("BRUTO", 10), ("TARA", 10), ("NETO", 10)
        ];
        List<IReadOnlyList<string?>> rows = _rows.Select(p => (IReadOnlyList<string?>)
        [
            p.FolioPesaje.ToString(CultureInfo.InvariantCulture), p.Patente, p.Producto, p.NombreProducto,
            p.Cliente, p.NombreCliente, p.FechaSalida.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            p.Bruto.ToString(CultureInfo.InvariantCulture), p.Tara.ToString(CultureInfo.InvariantCulture), p.Neto.ToString(CultureInfo.InvariantCulture)
        ]).ToList();
        DataExporter.ExportToDbf(sfd.FileName, cols, rows);
        MessageBox.Show(this, $"Archivo DBF generado correctamente:\r\n{sfd.FileName}", "Exportar DBF", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportCsv()
    {
        using var sfd = new SaveFileDialog { Filter = "CSV UTF-8|*.csv", FileName = $"Pesajes_{DateTime.Today:yyyyMMdd}.csv" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        string[] headers = ["Folio", "Fecha", "Hora", "Patente", "Cliente", "Producto", "Bruto", "Tara", "Neto", "Estado"];
        List<IReadOnlyList<string?>> rows = _rows.Select(p => (IReadOnlyList<string?>)
        [
            p.FolioPesaje.ToString(CultureInfo.InvariantCulture), p.FechaSalida.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            p.HoraSalida, p.Patente, p.NombreCliente, p.NombreProducto,
            p.Bruto.ToString(CultureInfo.InvariantCulture), p.Tara.ToString(CultureInfo.InvariantCulture),
            p.Neto.ToString(CultureInfo.InvariantCulture), p.Anulado ? "ANULADO" : "ACTIVO"
        ]).ToList();
        DataExporter.ExportToCsv(sfd.FileName, headers, rows);
        MessageBox.Show(this, $"Archivo CSV generado:\r\n{sfd.FileName}", "Exportar CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
