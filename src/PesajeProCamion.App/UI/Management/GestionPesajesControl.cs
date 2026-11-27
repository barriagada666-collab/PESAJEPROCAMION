using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Domain.Diagnostics;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;
using PesajeProCamion.Hardware.Export;
using PesajeProCamion.Hardware.Printing;

namespace PesajeProCamion.App.UI.Management;

/// <summary>
/// Administración completa de tickets de pesaje (Histórico + En Proceso):
/// CRUD completo sin conflictos de folio, visualizador gráfico, exportación PDF/XLSX/DBF/CSV y menú contextual clic derecho.
/// </summary>
public sealed class GestionPesajesControl : UserControl
{
    private sealed record TicketGridItem(Pesaje Pesaje, bool EsEnProceso);

    private readonly AppServices _services;
    private readonly DateTimePicker _dtDesde = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker _dtHasta = new() { Format = DateTimePickerFormat.Short };
    private readonly ComboBox _cmbTipo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbEstado = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtBuscar = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblResumen = new();
    private readonly CheckBox _chkVerFardos = new();
    private DataGridViewTextBoxColumn? _colFardos;

    private List<TicketGridItem> _rows = [];

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

        string rawTerm = _txtBuscar.Text.Trim();
        int? folioExacto = int.TryParse(rawTerm, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fParsed) ? fParsed : null;
        string? pat = folioExacto is null && !string.IsNullOrWhiteSpace(rawTerm) ? rawTerm : null;

        var merged = new List<TicketGridItem>();
        int estadoIdx = _cmbEstado.SelectedIndex; // 0=Todos, 1=Cerrados Activos, 2=En Proceso, 3=Anulados

        if (estadoIdx is 0 or 2)
        {
            List<RegistroTransito> transitos = await _services.Weighing.GetTransitoAsync();
            foreach (RegistroTransito tr in transitos)
            {
                if (tipo.HasValue && tr.TipoPesaje != tipo.Value) continue;
                if (folioExacto.HasValue && tr.FolioPesaje != folioExacto.Value) continue;
                if (!string.IsNullOrWhiteSpace(pat) &&
                    !tr.Patente.Contains(pat, StringComparison.OrdinalIgnoreCase) &&
                    !tr.NombreCliente.Contains(pat, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                merged.Add(new TicketGridItem(TicketEditorDialog.ToPesajeView(tr), EsEnProceso: true));
            }
        }

        if (estadoIdx is 0 or 1 or 3)
        {
            bool includeAnul = estadoIdx != 1;
            List<Pesaje> pesajes = await _services.Weighing.QueryPesajesAsync(
                desde: folioExacto.HasValue ? null : _dtDesde.Value.Date,
                hasta: folioExacto.HasValue ? null : _dtHasta.Value.Date,
                tipoPesaje: tipo,
                patente: pat,
                folioExacto: folioExacto,
                incluirAnulados: includeAnul,
                maxRows: 10000);

            foreach (Pesaje p in pesajes)
            {
                if (estadoIdx == 3 && !p.Anulado) continue;
                merged.Add(new TicketGridItem(p, EsEnProceso: false));
            }
        }

        _rows = merged.OrderByDescending(r => r.EsEnProceso).ThenByDescending(r => r.Pesaje.FolioPesaje).ToList();
        RenderGrid();
    }

    private void BuildUi()
    {
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = AppTheme.SurfaceCard, Padding = new Padding(6, 3, 6, 3), CanOverflow = true };
        ToolStripButton btnConsultar = AppTheme.CreateToolButton(AppIcon.Search, "Consultar", "Aplicar filtros de fecha/tipo/estado/folio", AppTheme.NavyPrimary);
        ToolStripButton btnNuevo = AppTheme.CreateToolButton(AppIcon.Add, "Nuevo Ticket", "Crear nuevo ticket manual sin conflicto de folio", AppTheme.AccentEmerald);
        ToolStripButton btnVisualizar = AppTheme.CreateToolButton(AppIcon.ViewTicket, "Visualizar", "Abrir vista visual del ticket seleccionado", AppTheme.NavyPrimary);
        ToolStripButton btnModificar = AppTheme.CreateToolButton(AppIcon.EditTicket, "Modificar", "Editar datos del ticket o en proceso", AppTheme.NavyDark);
        ToolStripButton btnDividir = AppTheme.CreateToolButton(AppIcon.Split, "Dividir", "Dividir ticket en 2 folios asignando nuevo folio automático", AppTheme.NavyPrimary);
        ToolStripButton btnCompletar = AppTheme.CreateToolButton(AppIcon.Check, "Completar", "Cerrar ticket En Proceso registrando peso faltante", AppTheme.AccentEmerald);
        ToolStripButton btnAnular = AppTheme.CreateToolButton(AppIcon.Delete, "Anular", "Alternar anulación o retirar de tránsito", AppTheme.AccentDanger);
        ToolStripButton btnImprimir = AppTheme.CreateToolButton(AppIcon.Print, "Imp. Ticket", "Elegir copias, previsualizar e imprimir ticket", AppTheme.NavyPrimary);
        ToolStripButton btnImprimirLista = AppTheme.CreateToolButton(AppIcon.Print, "Imp. Lista", "Previsualizar e imprimir listado completo", AppTheme.NavyDark);
        ToolStripButton btnPdf = AppTheme.CreateToolButton(AppIcon.Pdf, "PDF", "Exportar listado actual a PDF profesional", AppTheme.AccentDanger);
        ToolStripButton btnExcel = AppTheme.CreateToolButton(AppIcon.ExportExcel, "Excel", "Exportar listado a Excel (.xlsx)", AppTheme.AccentEmerald);
        ToolStripButton btnDbf = AppTheme.CreateToolButton(AppIcon.ExportDbf, "DBF", "Exportar tabla dBASE III (.dbf)", AppTheme.AccentAmber);
        ToolStripButton btnCsv = AppTheme.CreateToolButton(AppIcon.List, "CSV", "Exportar a CSV UTF-8", AppTheme.TextPrimary);

        _chkVerFardos.Text = "Fardos";
        _chkVerFardos.AutoSize = true;
        _chkVerFardos.Checked = _services.TicketFields.MostrarFardos;
        _chkVerFardos.Font = AppTheme.FontBodyBold;
        _chkVerFardos.BackColor = AppTheme.SurfaceCard;
        _chkVerFardos.Margin = new Padding(4, 3, 4, 2);
        _chkVerFardos.CheckedChanged += (_, _) =>
        {
            _services.TicketFields.MostrarFardos = _chkVerFardos.Checked;
            AppConfigStore.SaveTicketFields(_services.TicketFields);
            if (_colFardos is not null) _colFardos.Visible = _chkVerFardos.Checked;
        };
        var hostFardos = new ToolStripControlHost(_chkVerFardos) { Margin = new Padding(6, 2, 2, 2) };

        btnConsultar.Click += async (_, _) => await RefreshAsync();
        btnNuevo.Click += async (_, _) => await CreateTicketAsync();
        btnVisualizar.Click += async (_, _) => await ViewSelectedTicketAsync();
        btnModificar.Click += async (_, _) => await ModifySelectedAsync();
        btnDividir.Click += async (_, _) => await SplitSelectedAsync();
        btnCompletar.Click += async (_, _) => await CompleteInProcessAsync();
        btnAnular.Click += async (_, _) => await ToggleAnularSelectedAsync();
        btnImprimir.Click += (_, _) => PrintSelected();
        btnImprimirLista.Click += (_, _) => PrintList();
        btnPdf.Click += (_, _) => ExportPdf();
        btnExcel.Click += (_, _) => ExportExcel();
        btnDbf.Click += (_, _) => ExportDbf();
        btnCsv.Click += (_, _) => ExportCsv();

        toolbar.Items.AddRange([
            btnConsultar, new ToolStripSeparator(),
            btnNuevo, btnVisualizar, btnModificar, btnDividir, btnCompletar, btnAnular, new ToolStripSeparator(),
            btnImprimir, btnImprimirLista, btnPdf, btnExcel, btnDbf, btnCsv, new ToolStripSeparator(),
            hostFardos
        ]);

        var filterPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 42,
            BackColor = AppTheme.SurfaceCard,
            Padding = new Padding(8, 6, 8, 6)
        };
        var filterRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 10,
            RowCount = 1,
            Margin = Padding.Empty
        };
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102f));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102f));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122f));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174f));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        filterRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _dtDesde.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddYears(-1);
        _dtHasta.Value = DateTime.Today;
        _dtDesde.Dock = DockStyle.Fill; _dtDesde.Margin = new Padding(2, 2, 6, 0);
        _dtHasta.Dock = DockStyle.Fill; _dtHasta.Margin = new Padding(2, 2, 6, 0);
        _cmbTipo.Items.AddRange(["Todos", "Recepción", "Despacho", "Mov. Interno"]);
        _cmbTipo.SelectedIndex = 0;
        _cmbTipo.Dock = DockStyle.Fill; _cmbTipo.Margin = new Padding(2, 2, 6, 0);
        _cmbEstado.Items.AddRange(["Todos (Hist. + En Proceso)", "Cerrados / Activos", "En Proceso (Tránsito)", "Solo Anulados"]);
        _cmbEstado.SelectedIndex = 0;
        _cmbEstado.Dock = DockStyle.Fill; _cmbEstado.Margin = new Padding(2, 2, 6, 0);
        _cmbEstado.SelectedIndexChanged += async (_, _) => await RefreshAsync();
        _txtBuscar.Dock = DockStyle.Fill; _txtBuscar.Margin = new Padding(2, 2, 0, 0);
        _txtBuscar.PlaceholderText = "Folio o Patente...";
        _txtBuscar.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await RefreshAsync(); } };

        static Label L(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(2, 5, 2, 0), Font = AppTheme.FontBodyBold, Anchor = AnchorStyles.Left };
        filterRow.Controls.Add(L("Desde:"), 0, 0); filterRow.Controls.Add(_dtDesde, 1, 0);
        filterRow.Controls.Add(L("Hasta:"), 2, 0); filterRow.Controls.Add(_dtHasta, 3, 0);
        filterRow.Controls.Add(L("Tipo:"), 4, 0); filterRow.Controls.Add(_cmbTipo, 5, 0);
        filterRow.Controls.Add(L("Estado:"), 6, 0); filterRow.Controls.Add(_cmbEstado, 7, 0);
        filterRow.Controls.Add(L("Buscar:"), 8, 0); filterRow.Controls.Add(_txtBuscar, 9, 0);
        filterPanel.Controls.Add(filterRow);

        AppTheme.StyleDataGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _colFardos = new DataGridViewTextBoxColumn { Name = "colFardos", HeaderText = "Fardos", FillWeight = 58, Visible = _services.TicketFields.MostrarFardos };
        _grid.Columns.AddRange([
            new DataGridViewTextBoxColumn { HeaderText = "N° Folio", FillWeight = 64 },
            new DataGridViewTextBoxColumn { HeaderText = "Fecha", FillWeight = 76 },
            new DataGridViewTextBoxColumn { HeaderText = "Hora", FillWeight = 52 },
            new DataGridViewTextBoxColumn { HeaderText = "Patente", FillWeight = 68 },
            new DataGridViewTextBoxColumn { HeaderText = "Tipo de Pesaje", FillWeight = 115 },
            new DataGridViewTextBoxColumn { HeaderText = "Cliente", FillWeight = 148 },
            new DataGridViewTextBoxColumn { HeaderText = "Producto", FillWeight = 138 },
            _colFardos,
            new DataGridViewTextBoxColumn { HeaderText = "Bruto (kg)", FillWeight = 76 },
            new DataGridViewTextBoxColumn { HeaderText = "Tara (kg)", FillWeight = 76 },
            new DataGridViewTextBoxColumn { HeaderText = "Neto (kg)", FillWeight = 82 },
            new DataGridViewTextBoxColumn { HeaderText = "Estado", FillWeight = 84 }
        ]);
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await ViewSelectedTicketAsync(); };
        AttachUniversalContextMenu();

        _lblResumen.Dock = DockStyle.Bottom;
        _lblResumen.Height = 30;
        _lblResumen.BackColor = AppTheme.NavyDark;
        _lblResumen.ForeColor = Color.White;
        _lblResumen.Font = AppTheme.FontBodyBold;
        _lblResumen.TextAlign = ContentAlignment.MiddleLeft;
        _lblResumen.Padding = new Padding(12, 0, 12, 0);

        Controls.AddRange([_grid, _lblResumen, filterPanel, toolbar]);
    }

    private void AttachUniversalContextMenu()
    {
        static TicketGridItem? AsItem(object? sel) => sel as TicketGridItem;

        UniversalContextMenuService.AttachToGrid(
            _grid,
            () => _services.UsuarioActual,
            customActions:
            [
                new ContextActionSpec
                {
                    Text = "Visualizar Ticket...",
                    Icon = AppIcon.ViewTicket,
                    IconTint = AppTheme.NavyPrimary,
                    Group = 10,
                    RequiresRow = true,
                    IsVisible = sel => AsItem(sel) is not null,
                    ExecuteAsync = _ => ViewSelectedTicketAsync()
                },
                new ContextActionSpec
                {
                    Text = "Crear Nuevo Ticket...",
                    Icon = AppIcon.Add,
                    IconTint = AppTheme.AccentEmerald,
                    Group = 10,
                    RequiredPermission = AccionAutorizacion.AgregarRegistro,
                    ExecuteAsync = _ => CreateTicketAsync()
                },
                new ContextActionSpec
                {
                    Text = "Modificar Ticket Seleccionado...",
                    Icon = AppIcon.EditTicket,
                    IconTint = AppTheme.NavyDark,
                    Group = 10,
                    RequiresRow = true,
                    RequiredPermission = AccionAutorizacion.ModificarRegistro,
                    IsVisible = sel => AsItem(sel) is not null,
                    ExecuteAsync = _ => ModifySelectedAsync()
                },
                new ContextActionSpec
                {
                    Text = "Dividir Ticket en 2...",
                    Icon = AppIcon.Split,
                    IconTint = AppTheme.NavyPrimary,
                    Group = 10,
                    RequiresRow = true,
                    IsVisible = sel =>
                    {
                        TicketGridItem? item = AsItem(sel);
                        return item is not null && !item.EsEnProceso && !item.Pesaje.Anulado && item.Pesaje.Neto >= 2 &&
                               PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AgregarRegistro) &&
                               PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ModificarRegistro);
                    },
                    ExecuteAsync = _ => SplitSelectedAsync()
                },
                new ContextActionSpec
                {
                    Text = "Completar Salida (En Proceso)...",
                    Icon = AppIcon.Check,
                    IconTint = AppTheme.AccentEmerald,
                    Group = 10,
                    RequiresRow = true,
                    RequiredPermission = AccionAutorizacion.AgregarRegistro,
                    IsVisible = sel => AsItem(sel)?.EsEnProceso == true,
                    ExecuteAsync = _ => CompleteInProcessAsync()
                },
                new ContextActionSpec
                {
                    Text = "Duplicar como Nuevo Ticket...",
                    Icon = AppIcon.Duplicate,
                    IconTint = AppTheme.NavyPrimary,
                    Group = 10,
                    RequiresRow = true,
                    RequiredPermission = AccionAutorizacion.AgregarRegistro,
                    IsVisible = sel => AsItem(sel) is not null,
                    ExecuteAsync = _ => DuplicateSelectedAsync()
                },
                new ContextActionSpec
                {
                    Text = "Anular / Reactivar Ticket",
                    Icon = AppIcon.Delete,
                    IconTint = AppTheme.AccentDanger,
                    Group = 20,
                    RequiresRow = true,
                    RequiredPermission = AccionAutorizacion.AnularPesaje,
                    IsVisible = sel => AsItem(sel) is not null,
                    ExecuteAsync = _ => ToggleAnularSelectedAsync()
                },
                new ContextActionSpec
                {
                    Text = "Imprimir Ticket (Copias)...",
                    Icon = AppIcon.Print,
                    IconTint = AppTheme.NavyPrimary,
                    Group = 30,
                    RequiresRow = true,
                    RequiredPermission = AccionAutorizacion.ImprimirTicket,
                    IsVisible = sel => AsItem(sel) is not null,
                    ExecuteAsync = _ => { PrintSelected(); return Task.CompletedTask; }
                },
                new ContextActionSpec
                {
                    Text = "Exportar Ticket a PDF...",
                    Icon = AppIcon.Pdf,
                    IconTint = AppTheme.AccentDanger,
                    Group = 30,
                    RequiresRow = true,
                    IsVisible = sel => AsItem(sel) is not null,
                    ExecuteAsync = _ => { ExportSingleSelectedTicketPdf(); return Task.CompletedTask; }
                },
                new ContextActionSpec
                {
                    Text = "Exportar Ticket a Excel (.xlsx)...",
                    Icon = AppIcon.ExportExcel,
                    IconTint = AppTheme.AccentEmerald,
                    Group = 30,
                    RequiresRow = true,
                    IsVisible = sel => AsItem(sel) is not null,
                    ExecuteAsync = _ => { ExportSingleSelectedTicketXlsx(); return Task.CompletedTask; }
                },
                new ContextActionSpec
                {
                    Text = "Imprimir / Vista Previa Listado...",
                    Icon = AppIcon.Print,
                    IconTint = AppTheme.NavyDark,
                    Group = 40,
                    RequiredPermission = AccionAutorizacion.ImprimirTicket,
                    IsVisible = _ => _rows.Count > 0,
                    ExecuteAsync = _ => { PrintList(); return Task.CompletedTask; }
                },
                new ContextActionSpec
                {
                    Text = "Exportar Listado Estándar PDF...",
                    Icon = AppIcon.Pdf,
                    IconTint = AppTheme.AccentDanger,
                    Group = 40,
                    IsVisible = _ => _rows.Count > 0,
                    ExecuteAsync = _ => { ExportPdf(); return Task.CompletedTask; }
                },
                new ContextActionSpec
                {
                    Text = "Exportar Listado Estándar Excel (.xlsx)...",
                    Icon = AppIcon.ExportExcel,
                    IconTint = AppTheme.AccentEmerald,
                    Group = 40,
                    IsVisible = _ => _rows.Count > 0,
                    ExecuteAsync = _ => { ExportExcel(); return Task.CompletedTask; }
                },
                new ContextActionSpec
                {
                    Text = "Exportar Informe Completo (Todos los Campos) PDF...",
                    Icon = AppIcon.Pdf,
                    IconTint = AppTheme.NavyDark,
                    Group = 40,
                    IsVisible = _ => _rows.Count > 0 && !_services.TicketFields.MostrarFardos,
                    ExecuteAsync = _ => { ExportPdf(includeHiddenFields: true); return Task.CompletedTask; }
                },
                new ContextActionSpec
                {
                    Text = "Exportar Informe Completo (Todos los Campos) Excel...",
                    Icon = AppIcon.ExportExcel,
                    IconTint = AppTheme.NavyDark,
                    Group = 40,
                    IsVisible = _ => _rows.Count > 0 && !_services.TicketFields.MostrarFardos,
                    ExecuteAsync = _ => { ExportExcel(includeHiddenFields: true); return Task.CompletedTask; }
                },
                new ContextActionSpec
                {
                    Text = "Exportar Listado CSV...",
                    Icon = AppIcon.List,
                    IconTint = AppTheme.TextPrimary,
                    Group = 40,
                    IsVisible = _ => _rows.Count > 0,
                    ExecuteAsync = _ => { ExportCsv(); return Task.CompletedTask; }
                },
                new ContextActionSpec
                {
                    Text = "Filtrar por esta Patente",
                    Icon = AppIcon.Filter,
                    IconTint = AppTheme.NavyPrimary,
                    Group = 50,
                    RequiresRow = true,
                    IsVisible = sel => !string.IsNullOrWhiteSpace(AsItem(sel)?.Pesaje.Patente),
                    ExecuteAsync = async sel =>
                    {
                        if (AsItem(sel) is { } s)
                        {
                            _txtBuscar.Text = s.Pesaje.Patente;
                            await RefreshAsync();
                        }
                    }
                },
                new ContextActionSpec
                {
                    Text = "Copiar Folio (000000)",
                    Icon = AppIcon.Copy,
                    IconTint = AppTheme.TextPrimary,
                    Group = 50,
                    RequiresRow = true,
                    IsVisible = sel => AsItem(sel) is not null,
                    ExecuteAsync = sel =>
                    {
                        if (AsItem(sel) is { } s) Clipboard.SetText(s.Pesaje.FolioPesaje.ToString("D6", CultureInfo.InvariantCulture));
                        return Task.CompletedTask;
                    }
                }
            ],
            exportTitle: "LISTADO DE GESTIÓN DE PESAJES",
            sheetName: "Pesajes",
            refreshAsync: RefreshAsync,
            allowPrint: false,
            allowExports: false);
    }

    private void RenderGrid()
    {
        _grid.Rows.Clear();
        long sumBruto = 0, sumTara = 0, sumNeto = 0, sumFardos = 0;
        int activos = 0, enProceso = 0;

        foreach (TicketGridItem item in _rows)
        {
            Pesaje p = item.Pesaje;
            string estado = item.EsEnProceso ? "EN PROCESO" : (p.Anulado ? "ANULADO" : "ACTIVO");
            DateTime fMostrar = item.EsEnProceso ? p.FechaIngreso : p.FechaSalida;
            string hMostrar = item.EsEnProceso ? p.HoraIngreso : p.HoraSalida;

            int idx = _grid.Rows.Add(
                p.FolioPesaje.ToString("D6", CultureInfo.InvariantCulture),
                fMostrar.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                hMostrar,
                p.Patente,
                TicketCustomizationHelper.FormatTipoPesajeNombre(p.TipoPesaje),
                p.NombreCliente,
                p.NombreProducto,
                p.Fardos,
                $"{p.Bruto:N0}",
                $"{p.Tara:N0}",
                $"{p.Neto:N0}",
                estado);
            DataGridViewRow row = _grid.Rows[idx];
            row.Tag = item;
            row.Cells[0].Style.Font = AppTheme.FontBodyBold;
            row.Cells[8].Style.Font = AppTheme.FontBodyBold;
            row.Cells[9].Style.Font = AppTheme.FontBodyBold;
            row.Cells[10].Style.Font = AppTheme.FontBodyBoldLarge;

            if (item.EsEnProceso)
            {
                enProceso++;
                row.DefaultCellStyle.ForeColor = AppTheme.AccentAmber;
                row.DefaultCellStyle.Font = AppTheme.FontBodyBold;
            }
            else if (p.Anulado)
            {
                row.DefaultCellStyle.ForeColor = AppTheme.AccentDanger;
            }
            else
            {
                activos++;
                sumBruto += p.Bruto;
                sumTara += p.Tara;
                sumNeto += p.Neto;
                sumFardos += p.Fardos;
                row.Cells[10].Style.ForeColor = AppTheme.NavyDark;
            }
        }
        _chkVerFardos.Checked = _services.TicketFields.MostrarFardos;
        if (_colFardos is not null) _colFardos.Visible = _services.TicketFields.MostrarFardos;
        string fardosText = _services.TicketFields.MostrarFardos ? $"   |   Fardos: {sumFardos:N0}" : string.Empty;
        _lblResumen.Text = $"  Registros: {_rows.Count:N0} ({activos:N0} activos, {enProceso:N0} en proceso){fardosText}   |   Bruto: {sumBruto:N0} kg   |   Tara: {sumTara:N0} kg   |   Neto: {sumNeto:N0} kg ({sumNeto / 1000.0:N2} t)";
    }

    private TicketGridItem? GetSelected() => _grid.CurrentRow?.Tag as TicketGridItem;

    private async Task CreateTicketAsync()
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AgregarRegistro))
        {
            MessageBox.Show(this, "No está autorizado para crear tickets de pesaje.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dlg = new TicketEditorDialog(_services, TicketEditorMode.CreateNew);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            await RefreshAsync();
        }
    }

    private async Task ViewSelectedTicketAsync()
    {
        TicketGridItem? sel = GetSelected();
        if (sel is null) return;
        using var dlg = new TicketViewerDialog(_services, sel.Pesaje, sel.EsEnProceso);
        if (dlg.ShowDialog(this) == DialogResult.OK || dlg.Modified)
        {
            await RefreshAsync();
        }
    }

    private async Task ModifySelectedAsync()
    {
        TicketGridItem? sel = GetSelected();
        if (sel is null) return;
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ModificarRegistro))
        {
            MessageBox.Show(this, "No está autorizado para modificar registros de pesaje.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        TicketEditorMode mode = sel.EsEnProceso ? TicketEditorMode.EditInProcess : TicketEditorMode.EditHistorical;
        using var dlg = new TicketEditorDialog(_services, mode, sel.Pesaje);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            await RefreshAsync();
        }
    }

    private async Task SplitSelectedAsync()
    {
        TicketGridItem? sel = GetSelected();
        if (sel is null) return;
        if (sel.EsEnProceso || sel.Pesaje.Anulado)
        {
            MessageBox.Show(this, "Solo pueden dividirse tickets completados y activos (no En Proceso ni Anulados).", "Dividir Ticket", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        int neto = sel.Pesaje.Neto > 0 ? sel.Pesaje.Neto : Math.Abs(sel.Pesaje.Bruto - sel.Pesaje.Tara);
        if (neto < 2)
        {
            MessageBox.Show(this, "El ticket seleccionado debe tener Neto de al menos 2 kg para dividirse en 2.", "Dividir Ticket", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dlg = new SplitTicketDialog(_services, sel.Pesaje);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            await RefreshAsync();
        }
    }

    private async Task CompleteInProcessAsync()
    {
        TicketGridItem? sel = GetSelected();
        if (sel is null || !sel.EsEnProceso)
        {
            MessageBox.Show(this, "Seleccione un ticket con estado EN PROCESO para completar su salida.", "En Proceso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dlg = new TicketEditorDialog(_services, TicketEditorMode.CompleteInProcess, sel.Pesaje);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            await RefreshAsync();
        }
    }

    private async Task DuplicateSelectedAsync()
    {
        TicketGridItem? sel = GetSelected();
        if (sel is null) return;
        Pesaje copy = new()
        {
            FolioPesaje = 0,
            Patente = sel.Pesaje.Patente,
            Acoplado = sel.Pesaje.Acoplado,
            Cliente = sel.Pesaje.Cliente,
            NombreCliente = sel.Pesaje.NombreCliente,
            Producto = sel.Pesaje.Producto,
            NombreProducto = sel.Pesaje.NombreProducto,
            Origen = sel.Pesaje.Origen,
            Destino = sel.Pesaje.Destino,
            Transportista = sel.Pesaje.Transportista,
            Chofer = sel.Pesaje.Chofer,
            TipoPesaje = sel.Pesaje.TipoPesaje,
            FechaIngreso = DateTime.Today,
            HoraIngreso = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
            FechaSalida = DateTime.Today,
            HoraSalida = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture)
        };
        using var dlg = new TicketEditorDialog(_services, TicketEditorMode.CreateNew, copy);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            await RefreshAsync();
        }
    }

    private async Task ToggleAnularSelectedAsync()
    {
        TicketGridItem? sel = GetSelected();
        if (sel is null)
        {
            MessageBox.Show(this, "No se ha seleccionado un pesaje para anular.", "Administración", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AnularPesaje))
        {
            MessageBox.Show(this, "No está autorizado para anular pesajes.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (sel.EsEnProceso)
        {
            if (MessageBox.Show(this, $"¿Retirar de tránsito el folio EN PROCESO #{sel.Pesaje.FolioPesaje} ({sel.Pesaje.Patente})?", "Confirmar Anulación Tránsito", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await _services.Weighing.EliminarTransitoAsync(sel.Pesaje.FolioPesaje, _services.UsuarioActual?.Id ?? 0, "Anulado desde Gestión de Pesajes");
            await RefreshAsync();
            return;
        }

        string msg = sel.Pesaje.Anulado ? $"¿Reactivar el folio #{sel.Pesaje.FolioPesaje}?" : $"¿Anular el folio #{sel.Pesaje.FolioPesaje} ({sel.Pesaje.Patente})?";
        if (MessageBox.Show(this, msg, "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        await _services.Weighing.ToggleAnularPesajeAsync(sel.Pesaje.FolioPesaje, _services.UsuarioActual?.Id ?? 0);
        await RefreshAsync();
    }

    private void PrintSelected()
    {
        TicketGridItem? sel = GetSelected();
        if (sel is null)
        {
            MessageBox.Show(this, "Seleccione un pesaje del listado para imprimir.", "Ticket de Pesaje", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ImprimirTicket))
        {
            MessageBox.Show(this, "No está autorizado para imprimir tickets.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var copDlg = new TicketCopiesDialog(_services.ConfigActual.NroCopiasTicket);
        if (copDlg.ShowDialog(this) != DialogResult.OK) return;
        using PrintDocument doc = TicketPrinterService.CreateCommercialTicketDocument(
            sel.Pesaje, _services.BasculaActual, _services.ConfigActual, copDlg.Copies, IconCatalog.GetCorporateLogo(), _services.TicketFields, sel.EsEnProceso);
        using var prev = new PrintPreviewDialog { Document = doc, Width = 840, Height = 640 };
        prev.ShowDialog(this);
    }

    private void ExportSingleSelectedTicketPdf()
    {
        TicketGridItem? sel = GetSelected();
        if (sel is null) return;
        try
        {
            using var sfd = new SaveFileDialog { Filter = "Documento PDF|*.pdf", FileName = $"Ticket_Folio_{sel.Pesaje.FolioPesaje:D6}_{sel.Pesaje.Patente}.pdf" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            TicketPrinterService.ExportCommercialTicketToPdf(sfd.FileName, sel.Pesaje, _services.BasculaActual, _services.ConfigActual, sel.EsEnProceso, _services.TicketFields);
            MessageBox.Show(this, $"Ticket exportado a PDF:\r\n{sfd.FileName}", "Exportar PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("GestionPesajes.ExportTicketPdf", $"Error exportando ticket {sel.Pesaje.FolioPesaje:D6} a PDF", ex);
            MessageBox.Show(this, $"Error exportando ticket a PDF:\r\n{ex.Message}", "Error PDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportSingleSelectedTicketXlsx()
    {
        TicketGridItem? sel = GetSelected();
        if (sel is null) return;
        try
        {
            using var sfd = new SaveFileDialog { Filter = "Excel OpenXML|*.xlsx", FileName = $"Ticket_Folio_{sel.Pesaje.FolioPesaje:D6}_{sel.Pesaje.Patente}.xlsx" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            TicketPrinterService.ExportCommercialTicketToXlsx(sfd.FileName, sel.Pesaje, _services.BasculaActual, _services.ConfigActual, sel.EsEnProceso, _services.TicketFields);
            MessageBox.Show(this, $"Ticket exportado a Excel:\r\n{sfd.FileName}", "Exportar Excel", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("GestionPesajes.ExportTicketXlsx", $"Error exportando ticket {sel.Pesaje.FolioPesaje:D6} a XLSX", ex);
            MessageBox.Show(this, $"Error exportando ticket a Excel:\r\n{ex.Message}", "Error Excel", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private StandardTableReport BuildStandardReport(bool includeHiddenFields = false)
    {
        bool showFardos = _services.TicketFields.MostrarFardos || includeHiddenFields;
        var headers = new List<string> { "N° Folio", "Fecha", "Hora", "Patente", "Tipo de Pesaje", "Cliente", "Producto" };
        if (showFardos) headers.Add("Fardos");
        headers.AddRange(["Bruto (kg)", "Tara (kg)", "Neto (kg)", "Estado"]);

        int off = showFardos ? 1 : 0;
        var rows = new List<IReadOnlyList<object?>>(_rows.Count);
        long sumBruto = 0, sumTara = 0, sumNeto = 0, sumFardos = 0;
        int activos = 0, enProceso = 0;

        foreach (TicketGridItem x in _rows)
        {
            Pesaje p = x.Pesaje;
            string est = x.EsEnProceso ? "EN PROCESO" : (p.Anulado ? "ANULADO" : "ACTIVO");
            if (x.EsEnProceso) enProceso++;
            else if (!p.Anulado)
            {
                activos++;
                sumBruto += p.Bruto;
                sumTara += p.Tara;
                sumNeto += p.Neto;
                sumFardos += p.Fardos;
            }
            var r = new List<object?>(headers.Count)
            {
                p.FolioPesaje,
                (x.EsEnProceso ? p.FechaIngreso : p.FechaSalida).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                x.EsEnProceso ? p.HoraIngreso : p.HoraSalida,
                p.Patente,
                TicketCustomizationHelper.FormatTipoPesajeNombre(p.TipoPesaje),
                p.NombreCliente,
                p.NombreProducto
            };
            if (showFardos) r.Add(p.Fardos);
            r.AddRange([p.Bruto, p.Tara, p.Neto, est]);
            rows.Add(r);
        }

        var totals = new List<object?>(headers.Count)
        {
            "TOTAL", $"{activos:N0} act.", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty
        };
        if (showFardos) totals.Add(sumFardos);
        totals.AddRange([sumBruto, sumTara, sumNeto, $"{enProceso:N0} proc."]);

        var kpis = new List<(string Label, string Value)>
        {
            ("Registros", $"{_rows.Count:N0} ({activos:N0} act.)")
        };
        if (showFardos) kpis.Add(("Fardos Activos", $"{sumFardos:N0}"));
        kpis.Add(("Total Bruto", $"{sumBruto:N0} kg"));
        kpis.Add(("Total Tara", $"{sumTara:N0} kg"));
        kpis.Add(("Total Neto", $"{sumNeto:N0} kg ({sumNeto / 1000.0:N2} t)"));

        return new StandardTableReport
        {
            Title = "LISTADO DE GESTIÓN DE PESAJES",
            Subtitle = $"Período: {_dtDesde.Value:dd/MM/yyyy} al {_dtHasta.Value:dd/MM/yyyy} | Filtro: {_cmbEstado.Text}",
            SheetName = "Pesajes",
            Headers = headers,
            Rows = rows,
            TotalsRow = totals,
            Kpis = kpis,
            FolioColumnIndex = 0,
            BoldHighlightColumnIndices = new HashSet<int> { 9 + off },
            Landscape = true
        };
    }

    private void PrintList()
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ImprimirTicket))
        {
            MessageBox.Show(this, "No está autorizado para imprimir listados.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        UniversalContextMenuService.PrintStandardReportInteractive(this, BuildStandardReport());
    }

    private void ExportPdf(bool includeHiddenFields = false)
        => UniversalContextMenuService.ExportReportToPdfInteractive(
            this,
            BuildStandardReport(includeHiddenFields),
            $"Listado_Pesajes_{DateTime.Today:yyyyMMdd}.pdf");

    private void ExportExcel(bool includeHiddenFields = false)
        => UniversalContextMenuService.ExportReportToXlsxInteractive(
            this,
            BuildStandardReport(includeHiddenFields),
            $"Pesajes_{DateTime.Today:yyyyMMdd}.xlsx");

    private void ExportDbf()
    {
        try
        {
            using var sfd = new SaveFileDialog { Filter = "dBASE III DBF|*.dbf", FileName = $"EXPORT_{DateTime.Today:yyyyMMdd}.dbf" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            var cols = new List<(string, int)>
            {
                ("FLPESAJE", 10), ("PATENTE", 8), ("PRODUCTO", 12), ("NMPROD", 40),
                ("CLIENTE", 10), ("NMCLIENTE", 40), ("FECHASAL", 10), ("BRUTO", 10), ("TARA", 10), ("NETO", 10)
            };
            bool includeFardos = _services.TicketFields.MostrarFardos;
            if (includeFardos) cols.Add(("FARDOS", 10));
            List<IReadOnlyList<string?>> rows = _rows.Select(x =>
            {
                var r = new List<string?>
                {
                    x.Pesaje.FolioPesaje.ToString("D6", CultureInfo.InvariantCulture), x.Pesaje.Patente, x.Pesaje.Producto, x.Pesaje.NombreProducto,
                    x.Pesaje.Cliente, x.Pesaje.NombreCliente, x.Pesaje.FechaSalida.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    x.Pesaje.Bruto.ToString(CultureInfo.InvariantCulture), x.Pesaje.Tara.ToString(CultureInfo.InvariantCulture), x.Pesaje.Neto.ToString(CultureInfo.InvariantCulture)
                };
                if (includeFardos) r.Add(x.Pesaje.Fardos.ToString(CultureInfo.InvariantCulture));
                return (IReadOnlyList<string?>)r;
            }).ToList();
            DataExporter.ExportToDbf(sfd.FileName, cols, rows);
            MessageBox.Show(this, $"Archivo DBF generado correctamente:\r\n{sfd.FileName}", "Exportar DBF", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("GestionPesajes.ExportDbf", "Error exportando listado DBF", ex);
            MessageBox.Show(this, $"Error exportando a DBF:\r\n{ex.Message}", "Error DBF", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportCsv()
    {
        StandardTableReport rep = BuildStandardReport();
        UniversalContextMenuService.ExportReportToCsvInteractive(this, rep, $"Pesajes_{DateTime.Today:yyyyMMdd}.csv");
    }
}
