using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;
using PesajeProCamion.Domain.Validation;
using PesajeProCamion.Hardware.Printing;

namespace PesajeProCamion.App.UI.Management;

/// <summary>
/// Visualizador y personalizador interactivo de Ticket Comercial:
/// - Permite marcar/desmarcar campos visibles (Patente, Cliente, Producto, Origen, Destino, Transportista, Chofer, Guía, Tiempos, Observaciones)
/// - Destaca el Tipo de Pesaje en el encabezado superior derecho
/// - Permite editar el contenido/pesos del ticket en vivo (y guardar cambios en BD) o dividir el ticket en 2 folios.
/// </summary>
public sealed class TicketViewerDialog : Form
{
    private readonly AppServices _services;
    private readonly ToolTip _tt = new();
    private readonly NumericUpDown _numCopias = new();
    private readonly Panel _ticketCanvas = new();
    private readonly Button _btnModify = new();
    private readonly Button _btnSplit = new();
    private readonly Button _btnSaveInline = new();

    private Pesaje _pesaje;
    private Pesaje _workingTicket;
    private readonly bool _isEnProceso;
    private bool _suppressLiveSync;

    private readonly CheckBox _chkPatente = new() { Text = "Patente / Acopl." };
    private readonly CheckBox _chkCliente = new() { Text = "Cliente" };
    private readonly CheckBox _chkProducto = new() { Text = "Producto" };
    private readonly CheckBox _chkOrigen = new() { Text = "Origen / Prov." };
    private readonly CheckBox _chkDestino = new() { Text = "Destino" };
    private readonly CheckBox _chkTransp = new() { Text = "Transportista" };
    private readonly CheckBox _chkChofer = new() { Text = "Chofer" };
    private readonly CheckBox _chkGuia = new() { Text = "Guía Despacho" };
    private readonly CheckBox _chkTiempos = new() { Text = "Fechas / Oper." };
    private readonly CheckBox _chkObs = new() { Text = "Observaciones" };

    private readonly TextBox _txtEmpresa = new();
    private readonly ComboBox _cmbTipo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtPatente = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 8 };
    private readonly TextBox _txtAcoplado = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 8 };
    private readonly TextBox _txtCliente = new();
    private readonly TextBox _txtProducto = new();
    private readonly TextBox _txtOrigen = new();
    private readonly TextBox _txtDestino = new();
    private readonly TextBox _txtTransp = new();
    private readonly TextBox _txtChofer = new();
    private readonly TextBox _txtGuia = new() { MaxLength = 50, CharacterCasing = CharacterCasing.Upper };
    private readonly NumericUpDown _numBruto = new() { Maximum = 200_000, ThousandsSeparator = true };
    private readonly NumericUpDown _numTara = new() { Maximum = 200_000, ThousandsSeparator = true };
    private readonly NumericUpDown _numNeto = new() { Maximum = 200_000, ThousandsSeparator = true };
    private readonly TextBox _txtObs1 = new() { MaxLength = 120 };
    private readonly TextBox _txtObs2 = new() { MaxLength = 120 };

    public TicketViewerDialog(AppServices services, Pesaje pesaje, bool isEnProceso = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(pesaje);
        _services = services;
        _pesaje = pesaje;
        _workingTicket = CloneTicket(pesaje);
        _isEnProceso = isEnProceso;

        Text = $"Visor y Editor de Ticket — Folio #{pesaje.FolioPesaje}";
        AppTheme.ConfigureResponsiveDialog(this, 1180, 770, 960, 660, resizable: true);
        BuildUi();
        BindInitialState();
    }

    public bool Modified { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tt.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 50 };
        header.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, header.ClientRectangle);
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 16, 0),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = AppTheme.FontTitle,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = $"Visor y Personalizador de Ticket Comercial — Folio #{_pesaje.FolioPesaje:D6}"
        });

        var actionsBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            BackColor = AppTheme.SurfaceCard,
            Padding = new Padding(10, 8, 10, 8)
        };
        var actionsRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var lblCopies = new Label { Text = "Copias:", AutoSize = true, Font = AppTheme.FontBodyBold, Margin = new Padding(0, 9, 4, 0) };
        _numCopias.Width = 50;
        _numCopias.Minimum = 1;
        _numCopias.Maximum = 20;
        _numCopias.Value = Math.Clamp(_services.ConfigActual.NroCopiasTicket, 1, 20);
        _numCopias.Margin = new Padding(0, 5, 8, 0);

        var btnPrint = new Button { Size = new Size(138, 34), Margin = new Padding(0, 0, 6, 0) };
        AppTheme.StylePrimaryButton(btnPrint, AppIcon.Print, "Imprimir / Vista", "Vista previa e impresión con campos marcados", _tt);
        btnPrint.Enabled = PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ImprimirTicket);
        btnPrint.Click += (_, _) => PrintPreviewTicket();

        var btnPdf = new Button { Size = new Size(124, 34), Margin = new Padding(0, 0, 6, 0) };
        AppTheme.StyleSecondaryButton(btnPdf, AppIcon.Pdf, "Exportar PDF", "Guardar ticket personalizado en PDF", AppTheme.AccentDanger, _tt);
        btnPdf.Click += (_, _) => ExportTicketPdf();

        _btnSplit.Size = new Size(124, 34);
        _btnSplit.Margin = new Padding(0, 0, 6, 0);
        AppTheme.StyleSecondaryButton(_btnSplit, AppIcon.Split, "Dividir en 2", "Dividir este ticket en 2 folios asignando folio libre al nuevo", AppTheme.AccentEmerald, _tt);
        _btnSplit.Click += async (_, _) => await SplitCurrentTicketAsync();

        _btnModify.Size = new Size(140, 34);
        _btnModify.Margin = new Padding(0, 0, 6, 0);
        AppTheme.StyleSecondaryButton(_btnModify, AppIcon.EditTicket, "Editor Completo", "Abrir formulario completo con catálogos", AppTheme.NavyPrimary, _tt);
        _btnModify.Enabled = PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ModificarRegistro);
        _btnModify.Click += async (_, _) => await OpenFullEditorAsync();

        var btnCopyRaw = new Button { Size = new Size(118, 34), Margin = new Padding(0, 0, 6, 0) };
        AppTheme.StyleSecondaryButton(btnCopyRaw, AppIcon.Copy, "Copiar Texto", "Copiar texto con campos visibles al portapapeles", null, _tt);
        btnCopyRaw.Click += (_, _) =>
        {
            string raw = TicketPrinterService.BuildRawTicketText(_workingTicket, BuildDisplayBascula(), _services.ConfigActual, _services.TicketFields, _isEnProceso);
            Clipboard.SetText(raw);
            MessageBox.Show(this, "Detalle del ticket copiado al portapapeles.", "Copiar", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        var btnClose = new Button { Size = new Size(96, 34), Margin = Padding.Empty };
        AppTheme.StyleSecondaryButton(btnClose, AppIcon.Cancel, "Cerrar", "Cerrar visualizador", null, _tt);
        btnClose.Click += (_, _) =>
        {
            DialogResult = Modified ? DialogResult.OK : DialogResult.Cancel;
            Close();
        };

        actionsRow.Controls.AddRange([lblCopies, _numCopias, btnPrint, btnPdf, _btnSplit, _btnModify, btnCopyRaw, btnClose]);
        actionsBar.Controls.Add(actionsRow);

        // Split: Vista previa izquierda | Panel personalización & edición en vivo derecha
        var split = new SplitContainer
        {
            Size = new Size(1160, 620),
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
            Panel1MinSize = 460,
            Panel2MinSize = 350,
            SplitterDistance = 760,
            BackColor = AppTheme.BorderSubtle
        };

        var scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16, 12, 16, 12), BackColor = AppTheme.BackgroundLight };
        _ticketCanvas.Size = new Size(712, 578);
        _ticketCanvas.Dock = DockStyle.Top;
        _ticketCanvas.BackColor = Color.White;
        _ticketCanvas.BorderStyle = BorderStyle.FixedSingle;
        _ticketCanvas.Paint += PaintVisualTicket;
        _ticketCanvas.Resize += (_, _) => _ticketCanvas.Invalidate();
        scrollHost.Controls.Add(_ticketCanvas);
        split.Panel1.Controls.Add(scrollHost);

        var rightScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = AppTheme.SurfaceCard, Padding = new Padding(10) };

        // 1. Checkboxes de visibilidad de campos
        var grpVis = new GroupBox
        {
            Text = "Campos Visibles del Ticket",
            Dock = DockStyle.Top,
            Height = 178,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.NavyDark,
            Padding = new Padding(8, 4, 8, 4)
        };
        var chkGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6 };
        chkGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        chkGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        for (int r = 0; r < 5; r++) chkGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        chkGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        CheckBox[] chks = [_chkPatente, _chkCliente, _chkProducto, _chkOrigen, _chkDestino, _chkTransp, _chkChofer, _chkGuia, _chkTiempos, _chkObs];
        for (int i = 0; i < chks.Length; i++)
        {
            CheckBox c = chks[i];
            c.AutoSize = true;
            c.Font = AppTheme.FontBody;
            c.Margin = Padding.Empty;
            c.CheckedChanged += (_, _) => OnVisibilityChanged();
            chkGrid.Controls.Add(c, i % 2, i / 2);
        }

        var presetVisFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var btnVisAll = new Button { Size = new Size(104, 30), Margin = new Padding(0, 2, 6, 0) };
        AppTheme.StyleSecondaryButton(btnVisAll, AppIcon.Check, "Marcar Todos", "Mostrar todos los campos en ticket/PDF", AppTheme.NavyPrimary, _tt);
        btnVisAll.Click += (_, _) => SetAllFieldsVisible(true);

        var btnVisBasic = new Button { Size = new Size(104, 30), Margin = new Padding(0, 2, 0, 0) };
        AppTheme.StyleSecondaryButton(btnVisBasic, AppIcon.Filter, "Solo Básicos", "Mostrar solo Patente, Cliente, Producto y Guía", null, _tt);
        btnVisBasic.Click += (_, _) => SetBasicFieldsVisible();

        presetVisFlow.Controls.AddRange([btnVisAll, btnVisBasic]);
        chkGrid.Controls.Add(presetVisFlow, 0, 5);
        chkGrid.SetColumnSpan(presetVisFlow, 2);
        grpVis.Controls.Add(chkGrid);

        // 2. Editor en vivo de contenido / información del ticket
        var grpContent = new GroupBox
        {
            Text = "Editar Contenido del Ticket (En Vivo)",
            Dock = DockStyle.Top,
            Height = 456,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.NavyDark,
            Padding = new Padding(8, 4, 8, 6),
            Margin = new Padding(0, 6, 0, 0)
        };
        var editGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 17 };
        editGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102f));
        editGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (int r = 0; r < 16; r++) editGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 23f));
        editGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _cmbTipo.Items.AddRange(["1 - Recepción", "2 - Despacho", "3 - Mov. Interno"]);
        AddLiveEditRow(editGrid, 0, "Empresa:", _txtEmpresa);
        AddLiveEditRow(editGrid, 1, "Tipo Pesaje:", _cmbTipo);
        AddLiveEditRow(editGrid, 2, "Patente:", _txtPatente);
        AddLiveEditRow(editGrid, 3, "Acoplado:", _txtAcoplado);
        AddLiveEditRow(editGrid, 4, "Cliente:", _txtCliente);
        AddLiveEditRow(editGrid, 5, "Producto:", _txtProducto);
        AddLiveEditRow(editGrid, 6, "Origen:", _txtOrigen);
        AddLiveEditRow(editGrid, 7, "Destino:", _txtDestino);
        AddLiveEditRow(editGrid, 8, "Transportista:", _txtTransp);
        AddLiveEditRow(editGrid, 9, "Chofer:", _txtChofer);
        AddLiveEditRow(editGrid, 10, "Guía Desp.:", _txtGuia);
        AddLiveEditRow(editGrid, 11, "Bruto (kg):", _numBruto);
        AddLiveEditRow(editGrid, 12, "Tara (kg):", _numTara);
        AddLiveEditRow(editGrid, 13, "Neto (kg):", _numNeto);
        AddLiveEditRow(editGrid, 14, "Obs. 1:", _txtObs1);
        AddLiveEditRow(editGrid, 15, "Obs. 2:", _txtObs2);

        foreach (TextBox tb in new[] { _txtEmpresa, _txtPatente, _txtAcoplado, _txtCliente, _txtProducto, _txtOrigen, _txtDestino, _txtTransp, _txtChofer, _txtGuia, _txtObs1, _txtObs2 })
        {
            tb.TextChanged += (_, _) => SyncWorkingTicketFromInputs();
        }
        _cmbTipo.SelectedIndexChanged += (_, _) => SyncWorkingTicketFromInputs();
        _numBruto.ValueChanged += (_, _) => SyncWorkingTicketFromInputs();
        _numTara.ValueChanged += (_, _) => SyncWorkingTicketFromInputs();
        _numNeto.ValueChanged += (_, _) => SyncWorkingTicketFromInputs();

        _btnSaveInline.Dock = DockStyle.Fill;
        _btnSaveInline.Margin = new Padding(0, 5, 0, 1);
        AppTheme.StylePrimaryButton(_btnSaveInline, AppIcon.Save, "Guardar Cambios en BD", "Guardar información/pesos editados en la base de datos", _tt);
        _btnSaveInline.Enabled = PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ModificarRegistro);
        _btnSaveInline.Click += async (_, _) => await SaveInlineToDbAsync();
        editGrid.Controls.Add(_btnSaveInline, 0, 16);
        editGrid.SetColumnSpan(_btnSaveInline, 2);

        grpContent.Controls.Add(editGrid);

        rightScroll.Controls.Add(grpContent);
        rightScroll.Controls.Add(grpVis);
        split.Panel2.Controls.Add(rightScroll);

        Controls.AddRange([split, actionsBar, header]);
    }

    private static void AddLiveEditRow(TableLayoutPanel tbl, int row, string label, Control ctl)
    {
        tbl.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
            ForeColor = AppTheme.TextPrimary
        }, 0, row);
        ctl.Dock = DockStyle.Fill;
        ctl.Margin = new Padding(0, 1, 0, 1);
        ctl.Font = new Font("Segoe UI", 8.5f);
        tbl.Controls.Add(ctl, 1, row);
    }

    private void BindInitialState()
    {
        _suppressLiveSync = true;
        try
        {
            TicketFieldVisibility v = _services.TicketFields;
            _chkPatente.Checked = v.MostrarPatente;
            _chkCliente.Checked = v.MostrarCliente;
            _chkProducto.Checked = v.MostrarProducto;
            _chkOrigen.Checked = v.MostrarOrigen;
            _chkDestino.Checked = v.MostrarDestino;
            _chkTransp.Checked = v.MostrarTransportista;
            _chkChofer.Checked = v.MostrarChofer;
            _chkGuia.Checked = v.MostrarGuiaDespacho;
            _chkTiempos.Checked = v.MostrarTiemposOperador;
            _chkObs.Checked = v.MostrarObservaciones;

            _txtEmpresa.Text = string.IsNullOrWhiteSpace(_services.BasculaActual.NombreEmpresa) ? "PESAJE COMERCIAL" : _services.BasculaActual.NombreEmpresa;
            _cmbTipo.SelectedIndex = Math.Clamp((int)_workingTicket.TipoPesaje - 1, 0, 2);
            _txtPatente.Text = _workingTicket.Patente;
            _txtAcoplado.Text = _workingTicket.Acoplado;
            _txtCliente.Text = string.IsNullOrWhiteSpace(_workingTicket.NombreCliente) ? _workingTicket.Cliente : _workingTicket.NombreCliente;
            _txtProducto.Text = string.IsNullOrWhiteSpace(_workingTicket.NombreProducto) ? _workingTicket.Producto : _workingTicket.NombreProducto;
            _txtOrigen.Text = string.IsNullOrWhiteSpace(_workingTicket.NombreOrigen) ? _workingTicket.Origen : _workingTicket.NombreOrigen;
            _txtDestino.Text = string.IsNullOrWhiteSpace(_workingTicket.NombreDestino) ? _workingTicket.Destino : _workingTicket.NombreDestino;
            _txtTransp.Text = string.IsNullOrWhiteSpace(_workingTicket.NombreTransportista) ? _workingTicket.Transportista : _workingTicket.NombreTransportista;
            _txtChofer.Text = string.IsNullOrWhiteSpace(_workingTicket.NombreChofer) ? _workingTicket.Chofer : _workingTicket.NombreChofer;
            _txtGuia.Text = _workingTicket.GuiaDespacho;
            _numBruto.Value = Math.Clamp(_workingTicket.Bruto, 0, 200_000);
            _numTara.Value = Math.Clamp(_workingTicket.Tara, 0, 200_000);
            _numNeto.Value = _isEnProceso ? 0 : Math.Clamp(_workingTicket.Neto, 0, 200_000);
            _txtObs1.Text = _workingTicket.Observacion1;
            _txtObs2.Text = _workingTicket.Observacion2;

            bool canSplit = !_isEnProceso && !_pesaje.Anulado &&
                            (_pesaje.Neto > 1 || Math.Abs(_pesaje.Bruto - _pesaje.Tara) > 1) &&
                            PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AgregarRegistro) &&
                            PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ModificarRegistro);
            _btnSplit.Enabled = canSplit;
        }
        finally
        {
            _suppressLiveSync = false;
        }
        RefreshCanvasLayout();
    }

    private void OnVisibilityChanged()
    {
        if (_suppressLiveSync) return;
        TicketFieldVisibility v = _services.TicketFields;
        v.MostrarPatente = _chkPatente.Checked;
        v.MostrarCliente = _chkCliente.Checked;
        v.MostrarProducto = _chkProducto.Checked;
        v.MostrarOrigen = _chkOrigen.Checked;
        v.MostrarDestino = _chkDestino.Checked;
        v.MostrarTransportista = _chkTransp.Checked;
        v.MostrarChofer = _chkChofer.Checked;
        v.MostrarGuiaDespacho = _chkGuia.Checked;
        v.MostrarTiemposOperador = _chkTiempos.Checked;
        v.MostrarObservaciones = _chkObs.Checked;
        try
        {
            AppConfigStore.SaveTicketFields(v);
        }
        catch
        {
            // No bloquear visor visual si el directorio es de solo lectura
        }
        RefreshCanvasLayout();
    }

    private void SetAllFieldsVisible(bool visible)
    {
        _suppressLiveSync = true;
        foreach (CheckBox c in new[] { _chkPatente, _chkCliente, _chkProducto, _chkOrigen, _chkDestino, _chkTransp, _chkChofer, _chkGuia, _chkTiempos, _chkObs })
        {
            c.Checked = visible;
        }
        _suppressLiveSync = false;
        OnVisibilityChanged();
    }

    private void SetBasicFieldsVisible()
    {
        _suppressLiveSync = true;
        _chkPatente.Checked = true;
        _chkCliente.Checked = true;
        _chkProducto.Checked = true;
        _chkOrigen.Checked = false;
        _chkDestino.Checked = false;
        _chkTransp.Checked = false;
        _chkChofer.Checked = false;
        _chkGuia.Checked = true;
        _chkTiempos.Checked = true;
        _chkObs.Checked = false;
        _suppressLiveSync = false;
        OnVisibilityChanged();
    }

    private void SyncWorkingTicketFromInputs()
    {
        if (_suppressLiveSync) return;
        _workingTicket.TipoPesaje = (TipoPesaje)Math.Clamp(_cmbTipo.SelectedIndex + 1, 1, 3);
        _workingTicket.Patente = _txtPatente.Text.Trim();
        _workingTicket.Acoplado = _txtAcoplado.Text.Trim();
        _workingTicket.NombreCliente = _txtCliente.Text.Trim();
        _workingTicket.NombreProducto = _txtProducto.Text.Trim();
        _workingTicket.NombreOrigen = _txtOrigen.Text.Trim();
        _workingTicket.NombreDestino = _txtDestino.Text.Trim();
        _workingTicket.NombreTransportista = _txtTransp.Text.Trim();
        _workingTicket.NombreChofer = _txtChofer.Text.Trim();
        _workingTicket.GuiaDespacho = _txtGuia.Text.Trim();
        _workingTicket.Bruto = (int)_numBruto.Value;
        _workingTicket.Tara = (int)_numTara.Value;
        _workingTicket.Neto = _isEnProceso ? 0 : (int)_numNeto.Value;
        _workingTicket.Observacion1 = _txtObs1.Text.Trim();
        _workingTicket.Observacion2 = _txtObs2.Text.Trim();
        _ticketCanvas.Invalidate();
    }

    private void RefreshCanvasLayout()
    {
        int fieldCount = TicketPrinterService.BuildVisibleFieldRows(_workingTicket, _services.TicketFields).Count;
        int footerH = (_services.TicketFields.MostrarTiemposOperador ? 82 : 0) + (_services.TicketFields.MostrarObservaciones ? 28 : 0);
        int targetH = 148 + fieldCount * 22 + 106 + (footerH > 0 ? footerH + 14 : 8);
        _ticketCanvas.Height = Math.Max(390, targetH);
        _ticketCanvas.Invalidate();
    }

    private BasculaInfo BuildDisplayBascula()
    {
        string emp = _txtEmpresa.Text.Trim();
        if (string.IsNullOrWhiteSpace(emp))
        {
            return _services.BasculaActual;
        }
        return new BasculaInfo
        {
            Bascula = _services.BasculaActual.Bascula,
            Descripcion = _services.BasculaActual.Descripcion,
            NroInstalacion = _services.BasculaActual.NroInstalacion,
            Rut = _services.BasculaActual.Rut,
            Direccion = _services.BasculaActual.Direccion,
            NombreEmpresa = emp
        };
    }

    private void PaintVisualTicket(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        Rectangle rect = _ticketCanvas.ClientRectangle;
        int w = rect.Width;
        int pad = 22;

        AppTheme.PaintTealGradient(g, new Rectangle(0, 0, w, 78), includeGoldBottomAccent: true);
        Image? logo = IconCatalog.GetCorporateLogo();
        if (logo is not null)
        {
            using var logoBg = new SolidBrush(Color.White);
            g.FillRectangle(logoBg, pad, 10, 114, 56);
            g.DrawImage(logo, new Rectangle(pad + 6, 13, 102, 50));
        }

        string company = string.IsNullOrWhiteSpace(_txtEmpresa.Text) ? "PESAJE COMERCIAL" : _txtEmpresa.Text.Trim();
        string dir = string.IsNullOrWhiteSpace(_services.BasculaActual.Rut)
            ? _services.BasculaActual.Direccion
            : $"{_services.BasculaActual.Direccion}  |  RUT: {_services.BasculaActual.Rut}";
        using var fTitle = new Font("Segoe UI", 13f, FontStyle.Bold);
        using var fSub = new Font("Segoe UI", 8.75f, FontStyle.Regular);
        using var fSec = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var fBadge = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        using var fVal = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        using var fBig = new Font("Segoe UI", 14.5f, FontStyle.Bold);
        using var fLblW = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var goldBrush = new SolidBrush(AppTheme.GoldAccent);
        using var mutedBrush = new SolidBrush(AppTheme.TextMuted);
        using var darkBrush = new SolidBrush(AppTheme.NavyDark);
        using var textBrush = new SolidBrush(AppTheme.TextPrimary);
        using var linePen = new Pen(AppTheme.BorderSubtle, 1f);

        int textLeft = logo is not null ? pad + 128 : pad;
        g.DrawString("TICKET DE PESAJE COMERCIAL", fTitle, Brushes.White, textLeft, 12);
        g.DrawString(company, fSec, goldBrush, textLeft, 36);
        g.DrawString(dir, fSub, Brushes.LightSteelBlue, textLeft, 54);

        // Badge superior derecho: TIPO DE PESAJE destacado (en vez de COMPLETADO)
        string badgeText = TicketCustomizationHelper.FormatTipoPesajeBadge(_workingTicket.TipoPesaje, _workingTicket.Anulado, _isEnProceso);
        Color badgeColor = _workingTicket.Anulado
            ? AppTheme.AccentDanger
            : (_isEnProceso
                ? AppTheme.AccentAmber
                : (_workingTicket.TipoPesaje == TipoPesaje.Despacho
                    ? Color.FromArgb(0, 118, 173)
                    : (_workingTicket.TipoPesaje == TipoPesaje.MovimientoInterno ? Color.FromArgb(14, 124, 123) : AppTheme.AccentEmerald)));
        int badgeW = Math.Clamp((int)g.MeasureString(badgeText, fBadge).Width + 24, 150, 224);
        using var badgeBrush = new SolidBrush(badgeColor);
        var badgeRect = new Rectangle(w - pad - badgeW, 18, badgeW, 34);
        g.FillRectangle(badgeBrush, badgeRect);
        using var badgeBorder = new Pen(AppTheme.GoldAccent, 1.2f);
        g.DrawRectangle(badgeBorder, badgeRect);
        using var centerFmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(badgeText, fBadge, Brushes.White, badgeRect, centerFmt);

        int y = 94;
        g.DrawString($"FOLIO N° {_workingTicket.FolioPesaje:D6}", new Font("Segoe UI", 14f, FontStyle.Bold), darkBrush, pad, y);
        string estadoStr = TicketCustomizationHelper.FormatEstadoTicket(_workingTicket.Anulado, _isEnProceso);
        g.DrawString($"ESTADO: {estadoStr}", fSec, _workingTicket.Anulado ? Brushes.Firebrick : mutedBrush, w - pad - 200, y + 4);
        y += 32;
        g.DrawLine(linePen, pad, y, w - pad, y);
        y += 10;

        IReadOnlyList<(string Lbl, string Val)> fields = TicketPrinterService.BuildVisibleFieldRows(_workingTicket, _services.TicketFields);
        foreach ((string lbl, string val) in fields)
        {
            g.DrawString(lbl + ":", fSec, mutedBrush, pad, y);
            g.DrawString(string.IsNullOrWhiteSpace(val) ? "-" : val, fVal, textBrush, pad + 170, y);
            y += 22;
            g.DrawLine(linePen, pad, y - 3, w - pad, y - 3);
        }

        y += 8;
        var wBox = new Rectangle(pad, y, w - 2 * pad, 84);
        using var navyBrush = new SolidBrush(AppTheme.NavyDark);
        using var goldPen = new Pen(AppTheme.GoldAccent, 1.8f);
        g.FillRectangle(navyBrush, wBox);
        g.DrawRectangle(goldPen, wBox);

        int colW = wBox.Width / 3;
        DrawWeightCell(g, new Rectangle(wBox.X, wBox.Y, colW, wBox.Height), "PESO BRUTO", $"{_workingTicket.Bruto:N0} kg", Brushes.White, fLblW, fBig, goldBrush);
        DrawWeightCell(g, new Rectangle(wBox.X + colW, wBox.Y, colW, wBox.Height), _workingTicket.TaraRegistrada ? "PESO TARA (REG.)" : "PESO TARA", $"{_workingTicket.Tara:N0} kg", Brushes.White, fLblW, fBig, goldBrush);
        using var neonBrush = new SolidBrush(AppTheme.LcdDigitOn);
        DrawWeightCell(g, new Rectangle(wBox.X + colW * 2, wBox.Y, colW, wBox.Height), "PESO NETO", _isEnProceso ? "PENDIENTE" : $"{_workingTicket.Neto:N0} kg", neonBrush, fLblW, fBig, goldBrush);
        y += 96;

        bool showTimes = _services.TicketFields.MostrarTiemposOperador;
        bool showObs = _services.TicketFields.MostrarObservaciones;
        if (showTimes || showObs)
        {
            int boxH = (showTimes ? 68 : 8) + (showObs ? 28 : 4);
            using var softBrush = new SolidBrush(AppTheme.RowAlternate);
            var timeBox = new Rectangle(pad, y, w - 2 * pad, boxH);
            g.FillRectangle(softBrush, timeBox);
            g.DrawRectangle(linePen, timeBox);

            int ty = y + 8;
            if (showTimes)
            {
                int mid = w / 2;
                g.DrawString("ENTRADA A PLANTA", fSec, darkBrush, pad + 12, ty);
                g.DrawString($"Fecha/Hora: {_workingTicket.FechaIngreso:dd/MM/yyyy} {_workingTicket.HoraIngreso}", fVal, textBrush, pad + 12, ty + 20);
                g.DrawString($"Operador: {_workingTicket.NombreUsuarioIngreso}", fVal, mutedBrush, pad + 12, ty + 38);

                g.DrawString("SALIDA DE PLANTA", fSec, darkBrush, mid + 8, ty);
                string salText = _isEnProceso ? "En tránsito (sin salida)" : $"{_workingTicket.FechaSalida:dd/MM/yyyy} {_workingTicket.HoraSalida}";
                g.DrawString($"Fecha/Hora: {salText}", fVal, textBrush, mid + 8, ty + 20);
                g.DrawString($"Operador: {(_isEnProceso ? "-" : _workingTicket.NombreUsuarioSalida)}", fVal, mutedBrush, mid + 8, ty + 38);
                ty += 62;
            }
            if (showObs)
            {
                string obs = $"{_workingTicket.Observacion1} {_workingTicket.Observacion2}".Trim();
                g.DrawString($"Observaciones: {(string.IsNullOrWhiteSpace(obs) ? "-" : obs)}", fSub, textBrush, pad + 12, ty);
            }
        }
    }

    private static void DrawWeightCell(Graphics g, Rectangle cell, string title, string val, Brush valBrush, Font fLbl, Font fVal, Brush lblBrush)
    {
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(title, fLbl, lblBrush, new Rectangle(cell.X, cell.Y + 8, cell.Width, 20), fmt);
        g.DrawString(val, fVal, valBrush, new Rectangle(cell.X, cell.Y + 30, cell.Width, 42), fmt);
    }

    private void PrintPreviewTicket()
    {
        short copies = (short)_numCopias.Value;
        using PrintDocument doc = TicketPrinterService.CreateCommercialTicketDocument(
            _workingTicket, BuildDisplayBascula(), _services.ConfigActual, copies, IconCatalog.GetCorporateLogo(), _services.TicketFields, _isEnProceso);
        using var prev = new PrintPreviewDialog { Document = doc, Width = 860, Height = 660 };
        prev.ShowDialog(this);
    }

    private void ExportTicketPdf()
    {
        using var sfd = new SaveFileDialog
        {
            Filter = "Documento PDF|*.pdf",
            FileName = $"Ticket_Folio_{_workingTicket.FolioPesaje}_{_workingTicket.Patente}.pdf"
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        TicketPrinterService.ExportCommercialTicketToPdf(
            sfd.FileName, _workingTicket, BuildDisplayBascula(), _services.ConfigActual, _isEnProceso, _services.TicketFields);
        MessageBox.Show(this, $"Ticket exportado a PDF:\r\n{sfd.FileName}", "Exportar PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task SaveInlineToDbAsync()
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ModificarRegistro))
        {
            MessageBox.Show(this, "No posee permiso para modificar registros en base de datos.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string pat = ChileanValidators.NormalizarPatente(_txtPatente.Text);
        if (!ChileanValidators.EsPatenteValida(pat))
        {
            MessageBox.Show(this, "Ingrese una patente válida (4 a 8 caracteres).", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        short actorId = _services.UsuarioActual?.Id ?? 0;
        try
        {
            _workingTicket.Patente = pat;
            _workingTicket.Acoplado = ChileanValidators.NormalizarPatente(_txtAcoplado.Text);
            if (_isEnProceso)
            {
                await _services.Weighing.ModificarTransitoAsync(new RegistroTransito
                {
                    FolioPesaje = _workingTicket.FolioPesaje,
                    Patente = _workingTicket.Patente,
                    Acoplado = _workingTicket.Acoplado,
                    Producto = _workingTicket.Producto,
                    Cliente = _workingTicket.Cliente,
                    Origen = _workingTicket.Origen,
                    Destino = _workingTicket.Destino,
                    GuiaDespacho = _workingTicket.GuiaDespacho,
                    NetoInformado = _workingTicket.NetoInformado,
                    FechaIngreso = _workingTicket.FechaIngreso,
                    HoraIngreso = _workingTicket.HoraIngreso,
                    Bruto = _workingTicket.Bruto,
                    Tara = _workingTicket.Tara,
                    Chofer = _workingTicket.Chofer,
                    Observacion1 = _workingTicket.Observacion1,
                    Transportista = _workingTicket.Transportista,
                    TipoPesaje = _workingTicket.TipoPesaje,
                    UsuarioIngreso = _workingTicket.UsuarioIngreso
                }, actorId);
            }
            else
            {
                await _services.Weighing.ModificarPesajeAsync(_workingTicket, actorId, preserveManualNeto: true);
            }

            _pesaje = CloneTicket(_workingTicket);
            Modified = true;
            _ticketCanvas.Invalidate();
            MessageBox.Show(this, $"Cambios del Folio #{_pesaje.FolioPesaje} guardados correctamente.", "Guardar Ticket", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo guardar en BD:\r\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SplitCurrentTicketAsync()
    {
        if (_isEnProceso || _pesaje.Anulado)
        {
            MessageBox.Show(this, "Solo los tickets completados y activos pueden dividirse.", "Dividir Ticket", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dlg = new SplitTicketDialog(_services, _pesaje);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result is not null)
        {
            Modified = true;
            Pesaje? updated = await _services.Weighing.GetPesajePorFolioAsync(_pesaje.FolioPesaje);
            if (updated is not null)
            {
                _pesaje = updated;
                _workingTicket = CloneTicket(updated);
                BindInitialState();
            }
        }
    }

    private async Task OpenFullEditorAsync()
    {
        TicketEditorMode mode = _isEnProceso ? TicketEditorMode.EditInProcess : TicketEditorMode.EditHistorical;
        using var dlg = new TicketEditorDialog(_services, mode, _pesaje);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            Modified = true;
            if (!_isEnProceso)
            {
                Pesaje? updated = await _services.Weighing.GetPesajePorFolioAsync(_pesaje.FolioPesaje);
                if (updated is not null)
                {
                    _pesaje = updated;
                    _workingTicket = CloneTicket(updated);
                }
            }
            else
            {
                List<RegistroTransito> trs = await _services.Weighing.GetTransitoAsync();
                RegistroTransito? match = trs.FirstOrDefault(x => x.FolioPesaje == _pesaje.FolioPesaje);
                if (match is not null)
                {
                    _pesaje = TicketEditorDialog.ToPesajeView(match);
                    _workingTicket = CloneTicket(_pesaje);
                }
            }
            BindInitialState();
        }
    }

    private static Pesaje CloneTicket(Pesaje p) => new()
    {
        FolioPesaje = p.FolioPesaje,
        Patente = p.Patente,
        Acoplado = p.Acoplado,
        FolioPesajeMop = p.FolioPesajeMop,
        Producto = p.Producto,
        NombreProducto = p.NombreProducto,
        Cliente = p.Cliente,
        NombreCliente = p.NombreCliente,
        Origen = p.Origen,
        NombreOrigen = p.NombreOrigen,
        Destino = p.Destino,
        NombreDestino = p.NombreDestino,
        GuiaDespacho = p.GuiaDespacho,
        NetoInformado = p.NetoInformado,
        FechaIngreso = p.FechaIngreso,
        HoraIngreso = p.HoraIngreso,
        FechaSalida = p.FechaSalida,
        HoraSalida = p.HoraSalida,
        FechaModificacion = p.FechaModificacion,
        HoraModificacion = p.HoraModificacion,
        Bruto = p.Bruto,
        Tara = p.Tara,
        Neto = p.Neto,
        TipoMop = p.TipoMop,
        Largo = p.Largo,
        MaxBruto = p.MaxBruto,
        EstadoMop = p.EstadoMop,
        Chofer = p.Chofer,
        NombreChofer = p.NombreChofer,
        Traspaso = p.Traspaso,
        Observacion1 = p.Observacion1,
        Observacion2 = p.Observacion2,
        Transportista = p.Transportista,
        NombreTransportista = p.NombreTransportista,
        TaraRegistrada = p.TaraRegistrada,
        TipoPesaje = p.TipoPesaje,
        SavedToServer = p.SavedToServer,
        UsuarioIngreso = p.UsuarioIngreso,
        NombreUsuarioIngreso = p.NombreUsuarioIngreso,
        UsuarioSalida = p.UsuarioSalida,
        NombreUsuarioSalida = p.NombreUsuarioSalida,
        UsuarioModificacion = p.UsuarioModificacion,
        NombreUsuarioModificacion = p.NombreUsuarioModificacion,
        UsuarioEliminacion = p.UsuarioEliminacion,
        NombreUsuarioEliminacion = p.NombreUsuarioEliminacion,
        Anulado = p.Anulado
    };
}
