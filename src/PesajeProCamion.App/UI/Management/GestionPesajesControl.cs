using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Theme;
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
        ToolStripButton btnImprimir = AppTheme.CreateToolButton(AppIcon.Print, "Imprimir", "Elegir copias, previsualizar e imprimir ticket", AppTheme.NavyPrimary);
        ToolStripButton btnPdf = AppTheme.CreateToolButton(AppIcon.Pdf, "PDF", "Exportar listado actual a PDF", AppTheme.AccentDanger);
        ToolStripButton btnExcel = AppTheme.CreateToolButton(AppIcon.ExportExcel, "Excel", "Exportar listado a Excel (.xlsx)", AppTheme.AccentEmerald);
        ToolStripButton btnDbf = AppTheme.CreateToolButton(AppIcon.ExportDbf, "DBF", "Exportar tabla dBASE III (.dbf)", AppTheme.AccentAmber);
        ToolStripButton btnCsv = AppTheme.CreateToolButton(AppIcon.List, "CSV", "Exportar a CSV UTF-8", AppTheme.TextPrimary);

        btnConsultar.Click += async (_, _) => await RefreshAsync();
        btnNuevo.Click += async (_, _) => await CreateTicketAsync();
        btnVisualizar.Click += async (_, _) => await ViewSelectedTicketAsync();
        btnModificar.Click += async (_, _) => await ModifySelectedAsync();
        btnDividir.Click += async (_, _) => await SplitSelectedAsync();
        btnCompletar.Click += async (_, _) => await CompleteInProcessAsync();
        btnAnular.Click += async (_, _) => await ToggleAnularSelectedAsync();
        btnImprimir.Click += (_, _) => PrintSelected();
        btnPdf.Click += (_, _) => ExportPdf();
        btnExcel.Click += (_, _) => ExportExcel();
        btnDbf.Click += (_, _) => ExportDbf();
        btnCsv.Click += (_, _) => ExportCsv();

        toolbar.Items.AddRange([
            btnConsultar, new ToolStripSeparator(),
            btnNuevo, btnVisualizar, btnModificar, btnDividir, btnCompletar, btnAnular, new ToolStripSeparator(),
            btnImprimir, btnPdf, btnExcel, btnDbf, btnCsv
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
        _grid.Columns.AddRange([
            new DataGridViewTextBoxColumn { HeaderText = "Folio", FillWeight = 58 },
            new DataGridViewTextBoxColumn { HeaderText = "Fecha", FillWeight = 78 },
            new DataGridViewTextBoxColumn { HeaderText = "Hora", FillWeight = 52 },
            new DataGridViewTextBoxColumn { HeaderText = "Patente", FillWeight = 70 },
            new DataGridViewTextBoxColumn { HeaderText = "Tipo", FillWeight = 82 },
            new DataGridViewTextBoxColumn { HeaderText = "Cliente", FillWeight = 155 },
            new DataGridViewTextBoxColumn { HeaderText = "Producto", FillWeight = 145 },
            new DataGridViewTextBoxColumn { HeaderText = "Bruto (kg)", FillWeight = 76 },
            new DataGridViewTextBoxColumn { HeaderText = "Tara (kg)", FillWeight = 76 },
            new DataGridViewTextBoxColumn { HeaderText = "Neto (kg)", FillWeight = 78 },
            new DataGridViewTextBoxColumn { HeaderText = "Estado", FillWeight = 88 }
        ]);
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await ViewSelectedTicketAsync(); };
        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                _grid.ClearSelection();
                _grid.Rows[e.RowIndex].Selected = true;
                _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[0];
            }
        };
        _grid.ContextMenuStrip = BuildContextMenu();

        _lblResumen.Dock = DockStyle.Bottom;
        _lblResumen.Height = 30;
        _lblResumen.BackColor = AppTheme.NavyDark;
        _lblResumen.ForeColor = Color.White;
        _lblResumen.Font = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        _lblResumen.TextAlign = ContentAlignment.MiddleLeft;
        _lblResumen.Padding = new Padding(12, 0, 12, 0);

        Controls.AddRange([_grid, _lblResumen, filterPanel, toolbar]);
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var cms = new ContextMenuStrip();
        AppTheme.StyleContextMenu(cms);

        var mView = new ToolStripMenuItem("Visualizar Ticket...", IconCatalog.Get(AppIcon.ViewTicket, 16, AppTheme.NavyPrimary));
        mView.Click += async (_, _) => await ViewSelectedTicketAsync();

        var mNew = new ToolStripMenuItem("Crear Nuevo Ticket...", IconCatalog.Get(AppIcon.Add, 16, AppTheme.AccentEmerald));
        mNew.Click += async (_, _) => await CreateTicketAsync();

        var mMod = new ToolStripMenuItem("Modificar Ticket Seleccionado...", IconCatalog.Get(AppIcon.EditTicket, 16, AppTheme.NavyDark));
        mMod.Click += async (_, _) => await ModifySelectedAsync();

        var mSplit = new ToolStripMenuItem("Dividir Ticket en 2...", IconCatalog.Get(AppIcon.Split, 16, AppTheme.NavyPrimary));
        mSplit.Click += async (_, _) => await SplitSelectedAsync();

        var mComplete = new ToolStripMenuItem("Completar Salida (En Proceso)...", IconCatalog.Get(AppIcon.Check, 16, AppTheme.AccentEmerald));
        mComplete.Click += async (_, _) => await CompleteInProcessAsync();

        var mDup = new ToolStripMenuItem("Duplicar como Nuevo Ticket...", IconCatalog.Get(AppIcon.Duplicate, 16, AppTheme.NavyPrimary));
        mDup.Click += async (_, _) => await DuplicateSelectedAsync();

        var mAnul = new ToolStripMenuItem("Anular / Reactivar Ticket", IconCatalog.Get(AppIcon.Delete, 16, AppTheme.AccentDanger));
        mAnul.Click += async (_, _) => await ToggleAnularSelectedAsync();

        var mPrint = new ToolStripMenuItem("Imprimir Ticket (Copias)...", IconCatalog.Get(AppIcon.Print, 16, AppTheme.NavyPrimary));
        mPrint.Click += (_, _) => PrintSelected();

        var mPdfTicket = new ToolStripMenuItem("Exportar Ticket a PDF...", IconCatalog.Get(AppIcon.Pdf, 16, AppTheme.AccentDanger));
        mPdfTicket.Click += (_, _) => ExportSingleSelectedTicketPdf();

        var mExportXlsx = new ToolStripMenuItem("Exportar Listado a Excel (.xlsx)", IconCatalog.Get(AppIcon.ExportExcel, 16, AppTheme.AccentEmerald));
        mExportXlsx.Click += (_, _) => ExportExcel();

        var mFilterPat = new ToolStripMenuItem("Filtrar por esta Patente", IconCatalog.Get(AppIcon.Filter, 16, AppTheme.NavyPrimary));
        mFilterPat.Click += async (_, _) =>
        {
            if (GetSelected() is { } sel)
            {
                _txtBuscar.Text = sel.Pesaje.Patente;
                await RefreshAsync();
            }
        };

        var mCopyFolio = new ToolStripMenuItem("Copiar Folio al Portapapeles", IconCatalog.Get(AppIcon.Copy, 16, AppTheme.TextPrimary));
        mCopyFolio.Click += (_, _) => { if (GetSelected() is { } sel) Clipboard.SetText(sel.Pesaje.FolioPesaje.ToString(CultureInfo.InvariantCulture)); };

        var mCopyPat = new ToolStripMenuItem("Copiar Patente al Portapapeles", IconCatalog.Get(AppIcon.Truck, 16, AppTheme.TextPrimary));
        mCopyPat.Click += (_, _) => { if (GetSelected() is { } sel && !string.IsNullOrEmpty(sel.Pesaje.Patente)) Clipboard.SetText(sel.Pesaje.Patente); };

        var mRefresh = new ToolStripMenuItem("Actualizar Listado", IconCatalog.Get(AppIcon.Refresh, 16, AppTheme.NavyPrimary));
        mRefresh.Click += async (_, _) => await RefreshAsync();

        cms.Opening += (_, _) =>
        {
            TicketGridItem? sel = GetSelected();
            bool hasSel = sel is not null;
            bool isProc = sel?.EsEnProceso ?? false;
            mView.Enabled = hasSel;
            mMod.Enabled = hasSel && PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ModificarRegistro);
            mSplit.Enabled = hasSel && !isProc && !(sel?.Pesaje.Anulado ?? true) && (sel?.Pesaje.Neto ?? 0) >= 2 &&
                             PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AgregarRegistro) &&
                             PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ModificarRegistro);
            mNew.Enabled = PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AgregarRegistro);
            mDup.Enabled = hasSel && PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AgregarRegistro);
            mComplete.Enabled = hasSel && isProc && PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AgregarRegistro);
            mAnul.Enabled = hasSel && PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AnularPesaje);
            mPrint.Enabled = hasSel && PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ImprimirTicket);
            mPdfTicket.Enabled = hasSel;
            mFilterPat.Enabled = hasSel;
            mCopyFolio.Enabled = hasSel;
            mCopyPat.Enabled = hasSel;
        };

        cms.Items.AddRange([
            mView, mNew, mMod, mSplit, mComplete, mDup, mAnul, new ToolStripSeparator(),
            mPrint, mPdfTicket, mExportXlsx, new ToolStripSeparator(),
            mFilterPat, mCopyFolio, mCopyPat, mRefresh
        ]);
        return cms;
    }

    private void RenderGrid()
    {
        _grid.Rows.Clear();
        long sumBruto = 0, sumTara = 0, sumNeto = 0;
        int activos = 0, enProceso = 0;

        foreach (TicketGridItem item in _rows)
        {
            Pesaje p = item.Pesaje;
            string estado = item.EsEnProceso ? "EN PROCESO" : (p.Anulado ? "ANULADO" : "ACTIVO");
            DateTime fMostrar = item.EsEnProceso ? p.FechaIngreso : p.FechaSalida;
            string hMostrar = item.EsEnProceso ? p.HoraIngreso : p.HoraSalida;

            int idx = _grid.Rows.Add(
                p.FolioPesaje,
                fMostrar.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                hMostrar,
                p.Patente,
                p.TipoPesaje.ToString(),
                p.NombreCliente,
                p.NombreProducto,
                $"{p.Bruto:N0}",
                $"{p.Tara:N0}",
                $"{p.Neto:N0}",
                estado);
            _grid.Rows[idx].Tag = item;

            if (item.EsEnProceso)
            {
                enProceso++;
                _grid.Rows[idx].DefaultCellStyle.ForeColor = AppTheme.AccentAmber;
                _grid.Rows[idx].DefaultCellStyle.Font = AppTheme.FontBodyBold;
            }
            else if (p.Anulado)
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
        _lblResumen.Text = $"  Registros: {_rows.Count:N0} ({activos:N0} activos, {enProceso:N0} en proceso)   |   Bruto: {sumBruto:N0} kg   |   Tara: {sumTara:N0} kg   |   Neto: {sumNeto:N0} kg ({sumNeto / 1000.0:N2} t)";
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
        using var sfd = new SaveFileDialog { Filter = "Documento PDF|*.pdf", FileName = $"Ticket_Folio_{sel.Pesaje.FolioPesaje}_{sel.Pesaje.Patente}.pdf" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        TicketPrinterService.ExportCommercialTicketToPdf(sfd.FileName, sel.Pesaje, _services.BasculaActual, _services.ConfigActual, sel.EsEnProceso, _services.TicketFields);
        MessageBox.Show(this, $"Ticket exportado a PDF:\r\n{sfd.FileName}", "Exportar PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportPdf()
    {
        using var sfd = new SaveFileDialog { Filter = "Documento PDF|*.pdf", FileName = $"Listado_Pesajes_{DateTime.Today:yyyyMMdd}.pdf" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        string[] headers = ["Folio", "Fecha", "Hora", "Patente", "Tipo", "Cliente", "Producto", "Bruto (kg)", "Tara (kg)", "Neto (kg)", "Estado"];
        List<IReadOnlyList<object?>> rows = _rows.Select(x => (IReadOnlyList<object?>)
        [
            x.Pesaje.FolioPesaje,
            (x.EsEnProceso ? x.Pesaje.FechaIngreso : x.Pesaje.FechaSalida).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            x.EsEnProceso ? x.Pesaje.HoraIngreso : x.Pesaje.HoraSalida,
            x.Pesaje.Patente, x.Pesaje.TipoPesaje.ToString(), x.Pesaje.NombreCliente, x.Pesaje.NombreProducto,
            x.Pesaje.Bruto, x.Pesaje.Tara, x.Pesaje.Neto,
            x.EsEnProceso ? "EN PROCESO" : (x.Pesaje.Anulado ? "ANULADO" : "ACTIVO")
        ]).ToList();
        long neto = _rows.Where(r => !r.EsEnProceso && !r.Pesaje.Anulado).Sum(r => (long)r.Pesaje.Neto);
        DataExporter.ExportToPdf(
            sfd.FileName,
            "LISTADO DE GESTIÓN DE PESAJES",
            $"Período: {_dtDesde.Value:dd/MM/yyyy} al {_dtHasta.Value:dd/MM/yyyy} | Filtro: {_cmbEstado.Text}",
            [("Registros", _rows.Count.ToString("N0", CultureInfo.InvariantCulture)), ("Total Neto (kg)", $"{neto:N0} kg"), ("Toneladas", $"{neto / 1000.0:N2} t")],
            headers,
            rows);
        MessageBox.Show(this, $"Archivo PDF generado correctamente:\r\n{sfd.FileName}", "Exportar PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportExcel()
    {
        using var sfd = new SaveFileDialog { Filter = "Excel OpenXML|*.xlsx", FileName = $"Pesajes_{DateTime.Today:yyyyMMdd}.xlsx" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        string[] headers = ["Folio", "Fecha", "Hora", "Patente", "Acoplado", "Tipo", "Cliente", "Producto", "Bruto", "Tara", "Neto", "Estado"];
        List<IReadOnlyList<object?>> rows = _rows.Select(x => (IReadOnlyList<object?>)
        [
            x.Pesaje.FolioPesaje, (x.EsEnProceso ? x.Pesaje.FechaIngreso : x.Pesaje.FechaSalida).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            x.EsEnProceso ? x.Pesaje.HoraIngreso : x.Pesaje.HoraSalida,
            x.Pesaje.Patente, x.Pesaje.Acoplado, x.Pesaje.TipoPesaje.ToString(), x.Pesaje.NombreCliente, x.Pesaje.NombreProducto,
            x.Pesaje.Bruto, x.Pesaje.Tara, x.Pesaje.Neto,
            x.EsEnProceso ? "EN PROCESO" : (x.Pesaje.Anulado ? "ANULADO" : "ACTIVO")
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
        List<IReadOnlyList<string?>> rows = _rows.Select(x => (IReadOnlyList<string?>)
        [
            x.Pesaje.FolioPesaje.ToString(CultureInfo.InvariantCulture), x.Pesaje.Patente, x.Pesaje.Producto, x.Pesaje.NombreProducto,
            x.Pesaje.Cliente, x.Pesaje.NombreCliente, x.Pesaje.FechaSalida.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            x.Pesaje.Bruto.ToString(CultureInfo.InvariantCulture), x.Pesaje.Tara.ToString(CultureInfo.InvariantCulture), x.Pesaje.Neto.ToString(CultureInfo.InvariantCulture)
        ]).ToList();
        DataExporter.ExportToDbf(sfd.FileName, cols, rows);
        MessageBox.Show(this, $"Archivo DBF generado correctamente:\r\n{sfd.FileName}", "Exportar DBF", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportCsv()
    {
        using var sfd = new SaveFileDialog { Filter = "CSV UTF-8|*.csv", FileName = $"Pesajes_{DateTime.Today:yyyyMMdd}.csv" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        string[] headers = ["Folio", "Fecha", "Hora", "Patente", "Cliente", "Producto", "Bruto", "Tara", "Neto", "Estado"];
        List<IReadOnlyList<string?>> rows = _rows.Select(x => (IReadOnlyList<string?>)
        [
            x.Pesaje.FolioPesaje.ToString(CultureInfo.InvariantCulture), x.Pesaje.FechaSalida.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            x.Pesaje.HoraSalida, x.Pesaje.Patente, x.Pesaje.NombreCliente, x.Pesaje.NombreProducto,
            x.Pesaje.Bruto.ToString(CultureInfo.InvariantCulture), x.Pesaje.Tara.ToString(CultureInfo.InvariantCulture),
            x.Pesaje.Neto.ToString(CultureInfo.InvariantCulture),
            x.EsEnProceso ? "EN PROCESO" : (x.Pesaje.Anulado ? "ANULADO" : "ACTIVO")
        ]).ToList();
        DataExporter.ExportToCsv(sfd.FileName, headers, rows);
        MessageBox.Show(this, $"Archivo CSV generado:\r\n{sfd.FileName}", "Exportar CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
