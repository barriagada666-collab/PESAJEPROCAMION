using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Controls;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Management;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Domain.Diagnostics;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;
using PesajeProCamion.Domain.Services;
using PesajeProCamion.Domain.Validation;
using PesajeProCamion.Hardware.Printing;
using PesajeProCamion.Hardware.Scale;

namespace PesajeProCamion.App.UI.Weighing;

/// <summary>
/// Espacio de trabajo principal de Pesaje Comercial 100% responsivo con identidad Petrol Teal + Logo Cambiaso,
/// visor RS-232 en vivo, captura Pesar Camión / Pesar Carro / Tara Registrada / Grabar,
/// autoresolución de catálogos y grilla En Tránsito con menú contextual clic derecho.
/// </summary>
public sealed class PesajeWorkspaceControl : UserControl
{
    private readonly AppServices _services;
    private readonly ToolTip _tt = new();
    private readonly DigitalWeightDisplay _lcd = new();
    private readonly Label _lblEmp = new();
    private readonly Label _lblModoOperacion = new();
    private readonly Label _lblEstadoBalanza = new();

    private readonly TextBox _txtFolio = new() { ReadOnly = true };
    private readonly ComboBox _cmbTipoPesaje = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtPatente = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 8 };
    private readonly TextBox _txtAcoplado = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 8 };
    private readonly TextBox _txtClienteRut = new() { MaxLength = 10 };
    private readonly TextBox _txtClienteNombre = new() { ReadOnly = true };
    private readonly TextBox _txtProductoCod = new() { MaxLength = 12, CharacterCasing = CharacterCasing.Upper };
    private readonly TextBox _txtProductoNombre = new() { ReadOnly = true };
    private readonly TextBox _txtOrigenCod = new() { MaxLength = 10, CharacterCasing = CharacterCasing.Upper };
    private readonly TextBox _txtOrigenNombre = new() { ReadOnly = true };
    private readonly TextBox _txtDestinoCod = new() { MaxLength = 10, CharacterCasing = CharacterCasing.Upper };
    private readonly TextBox _txtDestinoNombre = new() { ReadOnly = true };
    private readonly TextBox _txtTranspCod = new() { MaxLength = 10 };
    private readonly TextBox _txtTranspNombre = new() { ReadOnly = true };
    private readonly TextBox _txtChoferCod = new() { MaxLength = 35 };
    private readonly TextBox _txtChoferNombre = new() { ReadOnly = true };
    private readonly TextBox _txtGuia = new() { MaxLength = 50, CharacterCasing = CharacterCasing.Upper };
    private readonly NumericUpDown _numNetoInf = new() { Maximum = 200_000, ThousandsSeparator = true };
    private readonly NumericUpDown _numFardos = new() { Minimum = 0, Maximum = 999_999, ThousandsSeparator = true };
    private readonly CheckBox _chkMostrarFardos = new();
    private readonly Label _lblFardosField = new();
    private readonly NumericUpDown _numTipoMop = new() { Maximum = 9999 };
    private readonly NumericUpDown _numLargo = new() { Maximum = 90 };
    private readonly TextBox _txtObs = new() { MaxLength = 120 };
    private readonly CheckBox _chkUsarTaraReg = new();

    private readonly NumericUpDown _numBruto = new() { Maximum = 200_000, ThousandsSeparator = true, ReadOnly = true };
    private readonly NumericUpDown _numTara = new() { Maximum = 200_000, ThousandsSeparator = true, ReadOnly = true };
    private readonly NumericUpDown _numNeto = new() { Maximum = 200_000, ThousandsSeparator = true, ReadOnly = true };
    private readonly Label _lblTaraRef = new();
    private readonly Label _lblIngresoInfo = new();
    private readonly Label _lblMsg = new();
    private readonly DataGridView _gridTransito = new();
    private DataGridViewTextBoxColumn? _colTransitoFardos;

    private readonly Button _btnPesarCamion = new();
    private readonly Button _btnPesarCarro = new();
    private readonly Button _btnTaraRegPanel = new();
    private readonly Button _btnGrabarPanel = new();

    private RegistroTransito? _transitoActivo;
    private Camion? _camionActual;
    private int _folioMopAsociado;
    private EstadoNormaMop _estadoMopActual = EstadoNormaMop.NoAplica;
    private int _maxBrutoMop;
    private int _currentWeightKg;
    private bool _currentStable = true;

    public PesajeWorkspaceControl(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        Dock = DockStyle.Fill;
        BackColor = AppTheme.BackgroundLight;
        BuildUi();
        _currentWeightKg = _services.Scale.CurrentWeightKg;
        _lcd.UpdateReading(_currentWeightKg, true, _currentWeightKg == 0, false, false, false, _services.Scale.IsSimulationActive);
        _services.Scale.ReadingReceived += OnReadingReceived;
    }

    public event EventHandler? TransitUpdated;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _services.Scale.ReadingReceived -= OnReadingReceived;
            _tt.Dispose();
        }
        base.Dispose(disposing);
    }

    public async Task RefreshTransitoAsync()
    {
        _lblEmp.Text = string.IsNullOrWhiteSpace(_services.BasculaActual.NombreEmpresa)
            ? "BÁSCULA COMERCIAL"
            : _services.BasculaActual.NombreEmpresa;

        List<RegistroTransito> rows = await _services.Weighing.GetTransitoAsync();
        _gridTransito.Rows.Clear();
        foreach (RegistroTransito t in rows)
        {
            int idx = _gridTransito.Rows.Add(
                t.FolioPesaje.ToString("D6", CultureInfo.InvariantCulture),
                TicketCustomizationHelper.FormatTipoPesajeNombre(t.TipoPesaje),
                t.Patente,
                t.FechaIngreso.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                t.HoraIngreso,
                t.Fardos,
                $"{t.Bruto:N0}",
                $"{t.Tara:N0}",
                t.NombreCliente,
                t.NombreProducto);
            _gridTransito.Rows[idx].Tag = t;
            _gridTransito.Rows[idx].Cells[0].Style.Font = AppTheme.FontBodyBold;
            _gridTransito.Rows[idx].Cells[6].Style.Font = AppTheme.FontBodyBold;
            _gridTransito.Rows[idx].Cells[7].Style.Font = AppTheme.FontBodyBold;
        }
        ApplyFardosVisibility(_services.TicketFields.MostrarFardos);
        TransitUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void BuildUi()
    {
        // Header corporativo 100% responsivo con TableLayoutPanel (cero offsets negativos)
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 104,
            Padding = new Padding(12, 10, 12, 10),
            ColumnCount = 3,
            RowCount = 1
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 356f));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        header.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, header.ClientRectangle);

        var logoCard = new Panel { Dock = DockStyle.Fill, Margin = new Padding(2, 4, 12, 4), BackColor = Color.White };
        logoCard.Controls.Add(new PictureBox
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(4),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = IconCatalog.GetCorporateLogo() ?? IconCatalog.RenderTruckBadge(110, 66)
        });

        var headerInfo = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(4, 0, 10, 0) };
        _lblEmp.Dock = DockStyle.Top;
        _lblEmp.Height = 28;
        _lblEmp.ForeColor = Color.White;
        _lblEmp.BackColor = Color.Transparent;
        _lblEmp.AutoEllipsis = true;
        _lblEmp.Font = new Font("Segoe UI", 12.5f, FontStyle.Bold);
        _lblEmp.Text = string.IsNullOrWhiteSpace(_services.BasculaActual.NombreEmpresa) ? "BÁSCULA COMERCIAL" : _services.BasculaActual.NombreEmpresa;

        _lblModoOperacion.Dock = DockStyle.Top;
        _lblModoOperacion.Height = 26;
        _lblModoOperacion.ForeColor = AppTheme.LcdDigitOn;
        _lblModoOperacion.BackColor = Color.Transparent;
        _lblModoOperacion.AutoEllipsis = true;
        _lblModoOperacion.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        _lblModoOperacion.Text = "MODO: PRIMER PESAJE / NUEVO INGRESO";

        _lblEstadoBalanza.Dock = DockStyle.Fill;
        _lblEstadoBalanza.ForeColor = AppTheme.GoldAccent;
        _lblEstadoBalanza.BackColor = Color.Transparent;
        _lblEstadoBalanza.AutoEllipsis = true;
        _lblEstadoBalanza.Font = AppTheme.FontBodyBold;
        _lblEstadoBalanza.Text = _services.Scale.StatusDescription;
        headerInfo.Controls.AddRange([_lblEstadoBalanza, _lblModoOperacion, _lblEmp]);

        _lcd.Dock = DockStyle.Fill;
        _lcd.Margin = new Padding(4, 1, 2, 1);

        header.Controls.Add(logoCard, 0, 0);
        header.Controls.Add(headerInfo, 1, 0);
        header.Controls.Add(_lcd, 2, 0);

        // Barra de herramientas uniforme y con soporte Overflow
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = AppTheme.SurfaceCard, Padding = new Padding(8, 4, 8, 4), CanOverflow = true };
        ToolStripButton btnCancelar = AppTheme.CreateToolButton(AppIcon.Cancel, "Cancelar Pesaje", "Limpiar formulario actual", AppTheme.AccentDanger);
        ToolStripButton btnNuevoTicket = AppTheme.CreateToolButton(AppIcon.Add, "Crear Ticket Manual", "Crear ticket completo o en proceso sin conflicto de folio", AppTheme.AccentEmerald);
        ToolStripButton btnBuscarFolio = AppTheme.CreateToolButton(AppIcon.ViewTicket, "Visualizar / Reimprimir Folio", "Buscar folio para visualizar, exportar PDF o imprimir", AppTheme.NavyPrimary);
        ToolStripButton btnVisor = AppTheme.CreateToolButton(AppIcon.SerialPort, "Visor / COM", "Configurar puerto COM, trama RS-232 o simulador", AppTheme.NavyPrimary);
        ToolStripButton btnManual = AppTheme.CreateToolButton(AppIcon.ManualWeight, "Ingreso Manual", "Digitar peso de contingencia/prueba en el visor", AppTheme.AccentAmber);
        ToolStripButton btnMop = AppTheme.CreateToolButton(AppIcon.AxleMop, "Pesaje Ejes MOP", "Abrir pesaje por ejes estático/dinámico", AppTheme.NavyPrimary);
        ToolStripButton btnAnularTr = AppTheme.CreateToolButton(AppIcon.Delete, "Anular Tránsito", "Eliminar camión seleccionado de planta", AppTheme.AccentDanger);

        btnCancelar.Click += (_, _) => ClearForm();
        btnNuevoTicket.Click += async (_, _) => await CreateManualTicketAsync();
        btnBuscarFolio.Click += async (_, _) => await ReimprimirPorFolioAsync();
        btnVisor.Click += (_, _) => OpenVisorDialog();
        btnManual.Click += (_, _) => CaptureManualWeight();
        btnMop.Click += async (_, _) => await OpenMopWeighingAsync();
        btnAnularTr.Click += async (_, _) => await DeleteSelectedTransitAsync();

        toolbar.Items.AddRange([
            btnCancelar, btnNuevoTicket, btnBuscarFolio, new ToolStripSeparator(),
            btnVisor, btnManual, new ToolStripSeparator(),
            btnMop, new ToolStripSeparator(),
            btnAnularTr
        ]);

        // Contenedor principal dividido horizontalmente: Tarjetas responsivas + Grilla En Tránsito
        var split = new SplitContainer
        {
            Size = new Size(1240, 660),
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 278,
            Panel2MinSize = 90,
            SplitterDistance = 304,
            BackColor = AppTheme.BorderSubtle
        };
        var topContainer = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BackgroundLight, Padding = new Padding(6, 4, 6, 4), AutoScroll = true };

        var cardsTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 292,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        cardsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61f));
        cardsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39f));
        cardsTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // Tarjeta Izquierda con TableLayoutPanel responsivo (cero desbordes ni saltos de línea)
        var leftCard = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(8, 4, 8, 4),
            BackColor = AppTheme.SurfaceCard,
            BorderStyle = BorderStyle.FixedSingle
        };

        var leftGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 9 };
        leftGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
        leftGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        leftGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88f));
        leftGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        for (int r = 0; r < 7; r++) leftGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 27f));
        leftGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 21f));
        leftGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _cmbTipoPesaje.Items.AddRange(["1 - Recepción", "2 - Despacho", "3 - Mov. Interno"]);
        _cmbTipoPesaje.SelectedIndex = 0;
        _txtPatente.Leave += async (_, _) => await OnPatenteLeaveAsync();

        // Row 0: Patente + Acoplado | Tipo Pesaje + Folio
        AddCellLabel(leftGrid, "Patente *", 0, 0);
        leftGrid.Controls.Add(BuildTripleHost(_txtPatente, "Buscar camión", LookupCamionAsync, new Label { Text = "Acopl.", AutoSize = true, Font = AppTheme.FontBody, Margin = new Padding(3, 5, 2, 0) }, _txtAcoplado, secondWidth: 56), 1, 0);
        AddCellLabel(leftGrid, "Tipo / Folio", 2, 0);
        leftGrid.Controls.Add(BuildTripleHost(_cmbTipoPesaje, null, null, null, _txtFolio, secondWidth: 46), 3, 0);

        // Row 1: Cliente | Producto
        AddCellLabel(leftGrid, "Cliente *", 0, 1);
        leftGrid.Controls.Add(BuildLookupPair(_txtClienteRut, _txtClienteNombre, "Buscar cliente", LookupClienteAsync), 1, 1);
        AddCellLabel(leftGrid, "Producto *", 2, 1);
        leftGrid.Controls.Add(BuildLookupPair(_txtProductoCod, _txtProductoNombre, "Buscar producto", LookupProductoAsync), 3, 1);

        // Row 2: Origen | Destino
        AddCellLabel(leftGrid, "Origen", 0, 2);
        leftGrid.Controls.Add(BuildLookupPair(_txtOrigenCod, _txtOrigenNombre, "Buscar origen", LookupOrigenAsync), 1, 2);
        AddCellLabel(leftGrid, "Destino", 2, 2);
        leftGrid.Controls.Add(BuildLookupPair(_txtDestinoCod, _txtDestinoNombre, "Buscar destino", LookupDestinoAsync), 3, 2);

        // Row 3: Transportista | Chofer
        AddCellLabel(leftGrid, "Transport.", 0, 3);
        leftGrid.Controls.Add(BuildLookupPair(_txtTranspCod, _txtTranspNombre, "Buscar transportista", LookupTranspAsync), 1, 3);
        AddCellLabel(leftGrid, "Chofer", 2, 3);
        leftGrid.Controls.Add(BuildLookupPair(_txtChoferCod, _txtChoferNombre, "Buscar chofer", LookupChoferAsync), 3, 3);

        // Row 4: Guía Despacho & Neto Inf | MOP & Largo & Tara Reg
        AddCellLabel(leftGrid, "Guía Desp.", 0, 4);
        leftGrid.Controls.Add(BuildTripleHost(_txtGuia, null, null, new Label { Text = "Neto Inf:", AutoSize = true, Margin = new Padding(3, 5, 2, 0) }, _numNetoInf, secondWidth: 64), 1, 4);
        AddCellLabel(leftGrid, "MOP / Largo", 2, 4);
        var mopRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        mopRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52f));
        mopRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44f));
        mopRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        mopRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _numTipoMop.Dock = DockStyle.Fill; _numTipoMop.Margin = new Padding(0, 3, 4, 0);
        _numLargo.Dock = DockStyle.Fill; _numLargo.Margin = new Padding(0, 3, 4, 0);
        _chkUsarTaraReg.Text = "Tara Reg."; _chkUsarTaraReg.Dock = DockStyle.Fill; _chkUsarTaraReg.Margin = new Padding(2, 2, 0, 0);
        mopRow.Controls.Add(_numTipoMop, 0, 0);
        mopRow.Controls.Add(_numLargo, 1, 0);
        mopRow.Controls.Add(_chkUsarTaraReg, 2, 0);
        leftGrid.Controls.Add(mopRow, 3, 4);

        // Row 5: Observación (span 3 cols)
        AddCellLabel(leftGrid, "Observación", 0, 5);
        _txtObs.Dock = DockStyle.Fill;
        _txtObs.Margin = new Padding(0, 2, 2, 2);
        leftGrid.Controls.Add(_txtObs, 1, 5);
        leftGrid.SetColumnSpan(_txtObs, 3);

        // Row 6: Cantidad de Fardos + Checkbox Mostrar Fardos
        _lblFardosField.Text = "Cant. Fardos";
        _lblFardosField.Dock = DockStyle.Fill;
        _lblFardosField.TextAlign = ContentAlignment.MiddleLeft;
        _lblFardosField.Font = AppTheme.FontBodyBold;
        _lblFardosField.ForeColor = AppTheme.TextPrimary;
        leftGrid.Controls.Add(_lblFardosField, 0, 6);

        var fardosRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 1, 4, 1) };
        fardosRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118f));
        fardosRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        fardosRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _numFardos.Dock = DockStyle.Fill;
        _numFardos.Margin = new Padding(0, 1, 8, 1);
        _chkMostrarFardos.Text = "Mostrar Fardos";
        _chkMostrarFardos.Dock = DockStyle.Fill;
        _chkMostrarFardos.Font = AppTheme.FontBodyBold;
        _chkMostrarFardos.Checked = _services.TicketFields.MostrarFardos;
        _tt.SetToolTip(_chkMostrarFardos, "Muestra u oculta Cantidad de Fardos en formularios y tickets sin alterar el valor en BD.");
        _chkMostrarFardos.CheckedChanged += (_, _) =>
        {
            _services.TicketFields.MostrarFardos = _chkMostrarFardos.Checked;
            AppConfigStore.SaveTicketFields(_services.TicketFields);
            ApplyFardosVisibility(_chkMostrarFardos.Checked);
        };
        fardosRow.Controls.Add(_numFardos, 0, 0);
        fardosRow.Controls.Add(_chkMostrarFardos, 1, 0);
        leftGrid.Controls.Add(fardosRow, 1, 6);
        leftGrid.SetColumnSpan(fardosRow, 3);

        // Row 7 & 8: Estado ingreso + mensajes
        _lblIngresoInfo.Dock = DockStyle.Fill;
        _lblIngresoInfo.Font = AppTheme.FontBodyBold;
        _lblIngresoInfo.ForeColor = AppTheme.NavyPrimary;
        _lblIngresoInfo.AutoEllipsis = true;
        _lblIngresoInfo.Text = "Ingreso: Pendiente de primer pesaje.";
        leftGrid.Controls.Add(_lblIngresoInfo, 0, 7);
        leftGrid.SetColumnSpan(_lblIngresoInfo, 4);

        _lblMsg.Dock = DockStyle.Fill;
        _lblMsg.Font = AppTheme.FontBodyBold;
        _lblMsg.ForeColor = AppTheme.NavyDark;
        _lblMsg.AutoEllipsis = true;
        _lblMsg.Text = "Formulario listo. Ingrese patente o seleccione un camión en tránsito.";
        leftGrid.Controls.Add(_lblMsg, 0, 8);
        leftGrid.SetColumnSpan(_lblMsg, 4);

        // Autoresolución al salir de códigos digitados manualmente
        _txtClienteRut.Leave += async (_, _) => await ResolveClienteTextAsync();
        _txtProductoCod.Leave += async (_, _) => await ResolveProductoTextAsync();
        _txtOrigenCod.Leave += async (_, _) => await ResolveOrigenTextAsync();
        _txtDestinoCod.Leave += async (_, _) => await ResolveDestinoTextAsync();
        _txtTranspCod.Leave += async (_, _) => await ResolveTranspTextAsync();
        _txtChoferCod.Leave += async (_, _) => await ResolveChoferTextAsync();

        leftCard.Controls.Add(leftGrid);

        // Tarjeta Derecha responsiva: Pesos Bruto/Tara/Neto + Tara Histórica + 4 Botones 100% visibles sin scroll
        var rightCard = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 4, 6, 4),
            BackColor = AppTheme.SurfaceCard,
            BorderStyle = BorderStyle.FixedSingle
        };

        var rightLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
        rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
        rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130f));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var weightsBand = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.NavyDark,
            Padding = new Padding(8, 5, 8, 5),
            Margin = new Padding(0, 0, 6, 6),
            ColumnCount = 2,
            RowCount = 3
        };
        weightsBand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68f));
        weightsBand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        weightsBand.RowStyles.Add(new RowStyle(SizeType.Percent, 30f));
        weightsBand.RowStyles.Add(new RowStyle(SizeType.Percent, 30f));
        weightsBand.RowStyles.Add(new RowStyle(SizeType.Percent, 40f));
        AddWeightRow(weightsBand, 0, "BRUTO", _numBruto, isNeto: false);
        AddWeightRow(weightsBand, 1, "TARA", _numTara, isNeto: false);
        AddWeightRow(weightsBand, 2, "NETO", _numNeto, isNeto: true);

        _lblTaraRef.Dock = DockStyle.Fill;
        _lblTaraRef.Margin = new Padding(0, 0, 0, 6);
        _lblTaraRef.BackColor = AppTheme.BackgroundLight;
        _lblTaraRef.BorderStyle = BorderStyle.FixedSingle;
        _lblTaraRef.Padding = new Padding(6);
        _lblTaraRef.Font = AppTheme.FontBody;
        _lblTaraRef.ForeColor = AppTheme.TextPrimary;
        _lblTaraRef.Text = "Tara Histórica:\r\n  -\r\nFecha:\r\n  -\r\nHora:\r\n  -";

        rightLayout.Controls.Add(weightsBand, 0, 0);
        rightLayout.Controls.Add(_lblTaraRef, 1, 0);

        var captureRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        captureRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        captureRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        captureRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _btnPesarCamion.Dock = DockStyle.Fill;
        _btnPesarCamion.Margin = new Padding(0, 2, 4, 4);
        AppTheme.StylePrimaryButton(_btnPesarCamion, AppIcon.Truck, "PESAR CAMIÓN", "Capturar peso del camión desde el visor actual", _tt);
        _btnPesarCamion.Click += (_, _) => CaptureCurrentScaleWeight(isTrailer: false);

        _btnPesarCarro.Dock = DockStyle.Fill;
        _btnPesarCarro.Margin = new Padding(4, 2, 0, 4);
        AppTheme.StyleSecondaryButton(_btnPesarCarro, AppIcon.Scale, "PESAR CARRO / 2°", "Capturar segundo peso o peso de acoplado", AppTheme.NavyPrimary, _tt);
        _btnPesarCarro.Click += (_, _) => CaptureCurrentScaleWeight(isTrailer: true);
        captureRow.Controls.Add(_btnPesarCamion, 0, 0);
        captureRow.Controls.Add(_btnPesarCarro, 1, 0);

        rightLayout.Controls.Add(captureRow, 0, 1);
        rightLayout.SetColumnSpan(captureRow, 2);

        _btnTaraRegPanel.Dock = DockStyle.Fill;
        _btnTaraRegPanel.Margin = new Padding(0, 2, 0, 4);
        AppTheme.StyleSecondaryButton(_btnTaraRegPanel, AppIcon.Tare, "APLICAR TARA REGISTRADA", "Validar vigencia y aplicar tara registrada del camión", AppTheme.AccentAmber, _tt);
        _btnTaraRegPanel.Click += (_, _) => ApplyRegisteredTare();
        rightLayout.Controls.Add(_btnTaraRegPanel, 0, 2);
        rightLayout.SetColumnSpan(_btnTaraRegPanel, 2);

        _btnGrabarPanel.Dock = DockStyle.Fill;
        _btnGrabarPanel.Margin = new Padding(0, 2, 0, 0);
        AppTheme.StylePrimaryButton(_btnGrabarPanel, AppIcon.Save, "GRABAR PESAJE", "Guardar primer pesaje en tránsito o completar salida definitiva", _tt);
        _btnGrabarPanel.BackColor = AppTheme.AccentEmerald;
        _btnGrabarPanel.Click += async (_, _) => await SaveWeighingAsync();
        rightLayout.Controls.Add(_btnGrabarPanel, 0, 3);
        rightLayout.SetColumnSpan(_btnGrabarPanel, 2);

        rightCard.Controls.Add(rightLayout);

        cardsTable.Controls.Add(leftCard, 0, 0);
        cardsTable.Controls.Add(rightCard, 1, 0);
        topContainer.Controls.Add(cardsTable);

        // Grilla Inferior En Tránsito + Clic Derecho
        var transitWrapper = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.SurfaceCard };
        var transitTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "  CAMIONES PENDIENTES EN PLANTA (EN TRÁNSITO / EN PROCESO) — Doble clic para 2° pesaje | Clic derecho para opciones",
            BackColor = AppTheme.SlateHeader,
            ForeColor = Color.White,
            Font = AppTheme.FontBodyBold,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        AppTheme.StyleDataGrid(_gridTransito);
        _gridTransito.Dock = DockStyle.Fill;
        _colTransitoFardos = new DataGridViewTextBoxColumn { Name = "colTransitoFardos", HeaderText = "Fardos", FillWeight = 56, Visible = _services.TicketFields.MostrarFardos };
        _gridTransito.Columns.AddRange([
            new DataGridViewTextBoxColumn { HeaderText = "N° Folio", FillWeight = 64 },
            new DataGridViewTextBoxColumn { HeaderText = "Tipo de Pesaje", FillWeight = 112 },
            new DataGridViewTextBoxColumn { HeaderText = "Patente", FillWeight = 72 },
            new DataGridViewTextBoxColumn { HeaderText = "Fecha Ing.", FillWeight = 78 },
            new DataGridViewTextBoxColumn { HeaderText = "Hora Ing.", FillWeight = 62 },
            _colTransitoFardos,
            new DataGridViewTextBoxColumn { HeaderText = "Bruto (kg)", FillWeight = 78 },
            new DataGridViewTextBoxColumn { HeaderText = "Tara (kg)", FillWeight = 78 },
            new DataGridViewTextBoxColumn { HeaderText = "Cliente", FillWeight = 150 },
            new DataGridViewTextBoxColumn { HeaderText = "Producto", FillWeight = 135 }
        ]);
        _gridTransito.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _gridTransito.Rows[e.RowIndex].Tag is RegistroTransito tr)
            {
                LoadFromTransito(tr);
            }
        };
        AttachWorkspaceContextMenus(leftCard, rightCard);
        transitWrapper.Controls.AddRange([_gridTransito, transitTitle]);

        split.Panel1.Controls.Add(topContainer);
        split.Panel2.Controls.Add(transitWrapper);

        Controls.AddRange([split, toolbar, header]);
        ApplyFardosVisibility(_services.TicketFields.MostrarFardos);
    }

    private void ApplyFardosVisibility(bool visible)
    {
        _lblFardosField.Visible = visible;
        _numFardos.Visible = visible;
        if (_colTransitoFardos is not null)
        {
            _colTransitoFardos.Visible = visible;
        }
    }

    private void AttachWorkspaceContextMenus(Control leftCard, Control rightCard)
    {
        UniversalContextMenuService.AttachToGrid(
            _gridTransito,
            () => _services.UsuarioActual,
            customActions:
            [
                new ContextActionSpec
                {
                    Text = "Cargar en Báscula (2° Pesaje)",
                    Icon = AppIcon.Scale,
                    IconTint = AppTheme.AccentEmerald,
                    Group = 10,
                    RequiresRow = true,
                    IsVisible = sel => sel is RegistroTransito,
                    ExecuteAsync = sel => { if (sel is RegistroTransito tr) LoadFromTransito(tr); return Task.CompletedTask; }
                },
                new ContextActionSpec
                {
                    Text = "Visualizar Ticket En Proceso...",
                    Icon = AppIcon.ViewTicket,
                    IconTint = AppTheme.NavyPrimary,
                    Group = 10,
                    RequiresRow = true,
                    IsVisible = sel => sel is RegistroTransito,
                    ExecuteAsync = async sel =>
                    {
                        if (sel is RegistroTransito tr)
                        {
                            using var dlg = new TicketViewerDialog(_services, TicketEditorDialog.ToPesajeView(tr), isEnProceso: true);
                            if (dlg.ShowDialog(this) == DialogResult.OK || dlg.Modified) await RefreshTransitoAsync();
                        }
                    }
                },
                new ContextActionSpec
                {
                    Text = "Modificar Datos En Proceso...",
                    Icon = AppIcon.EditTicket,
                    IconTint = AppTheme.NavyDark,
                    Group = 10,
                    RequiresRow = true,
                    RequiredPermission = AccionAutorizacion.ModificarRegistro,
                    IsVisible = sel => sel is RegistroTransito,
                    ExecuteAsync = async sel =>
                    {
                        if (sel is RegistroTransito tr)
                        {
                            using var dlg = new TicketEditorDialog(_services, TicketEditorMode.EditInProcess, TicketEditorDialog.ToPesajeView(tr));
                            if (dlg.ShowDialog(this) == DialogResult.OK) await RefreshTransitoAsync();
                        }
                    }
                },
                new ContextActionSpec
                {
                    Text = "Anular / Eliminar de Tránsito...",
                    Icon = AppIcon.Delete,
                    IconTint = AppTheme.AccentDanger,
                    Group = 20,
                    RequiresRow = true,
                    RequiredPermission = AccionAutorizacion.EliminarRegistro,
                    IsVisible = sel => sel is RegistroTransito,
                    ExecuteAsync = async _ => await DeleteSelectedTransitAsync()
                },
                new ContextActionSpec
                {
                    Text = "Copiar Patente",
                    Icon = AppIcon.Copy,
                    IconTint = AppTheme.TextPrimary,
                    Group = 30,
                    RequiresRow = true,
                    IsVisible = sel => sel is RegistroTransito tr && !string.IsNullOrWhiteSpace(tr.Patente),
                    ExecuteAsync = sel => { if (sel is RegistroTransito tr) Clipboard.SetText(tr.Patente); return Task.CompletedTask; }
                }
            ],
            exportTitle: "CAMIONES PENDIENTES EN PLANTA (EN TRÁNSITO)",
            exportSubtitleProvider: () => $"Báscula: {_services.BasculaActual.NombreEmpresa} | En tránsito: {_gridTransito.Rows.Count}",
            sheetName: "Transito_Planta",
            refreshAsync: RefreshTransitoAsync);

        ContextActionSpec[] workspaceActions =
        [
            new ContextActionSpec
            {
                Text = "Grabar Pesaje Actual",
                Icon = AppIcon.Save,
                IconTint = AppTheme.AccentEmerald,
                Group = 10,
                RequiredPermission = AccionAutorizacion.AgregarRegistro,
                ExecuteAsync = _ => SaveWeighingAsync()
            },
            new ContextActionSpec
            {
                Text = "Aplicar Tara Registrada del Camión",
                Icon = AppIcon.Tare,
                IconTint = AppTheme.AccentAmber,
                Group = 10,
                IsVisible = _ => _camionActual is not null,
                ExecuteAsync = _ => { ApplyRegisteredTare(); return Task.CompletedTask; }
            },
            new ContextActionSpec
            {
                Text = "Crear Ticket Manual...",
                Icon = AppIcon.Add,
                IconTint = AppTheme.AccentEmerald,
                Group = 20,
                RequiredPermission = AccionAutorizacion.AgregarRegistro,
                ExecuteAsync = _ => CreateManualTicketAsync()
            },
            new ContextActionSpec
            {
                Text = "Visualizar / Reimprimir Folio...",
                Icon = AppIcon.ViewTicket,
                IconTint = AppTheme.NavyPrimary,
                Group = 20,
                ExecuteAsync = _ => ReimprimirPorFolioAsync()
            },
            new ContextActionSpec
            {
                Text = "Configurar Visor RS-232...",
                Icon = AppIcon.SerialPort,
                IconTint = AppTheme.NavyPrimary,
                Group = 30,
                ExecuteAsync = _ => { OpenVisorDialog(); return Task.CompletedTask; }
            },
            new ContextActionSpec
            {
                Text = "Limpiar / Cancelar Formulario",
                Icon = AppIcon.Cancel,
                IconTint = AppTheme.AccentDanger,
                Group = 40,
                ExecuteAsync = _ => { ClearForm(); return Task.CompletedTask; }
            },
            new ContextActionSpec
            {
                Text = "Actualizar Tránsito",
                Icon = AppIcon.Refresh,
                IconTint = AppTheme.NavyPrimary,
                Group = 50,
                ExecuteAsync = _ => RefreshTransitoAsync()
            }
        ];
        UniversalContextMenuService.AttachToControl(leftCard, () => _services.UsuarioActual, workspaceActions);
        UniversalContextMenuService.AttachToControl(rightCard, () => _services.UsuarioActual, workspaceActions);
    }

    private static void AddCellLabel(TableLayoutPanel grid, string text, int col, int row)
    {
        grid.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.TextPrimary
        }, col, row);
    }

    private Control BuildLookupPair(TextBox codeBox, TextBox nameBox, string tip, Func<Task> onSearch)
    {
        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 2, 4, 2) };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        codeBox.Dock = DockStyle.Fill; codeBox.Margin = new Padding(0, 2, 3, 0);
        var btn = new Button { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 3, 1) };
        AppTheme.StyleSecondaryButton(btn, AppIcon.Search, string.Empty, tip, null, _tt);
        btn.Click += async (_, _) => await onSearch();
        nameBox.Dock = DockStyle.Fill; nameBox.Margin = new Padding(0, 2, 0, 0);

        tbl.Controls.Add(codeBox, 0, 0);
        tbl.Controls.Add(btn, 1, 0);
        tbl.Controls.Add(nameBox, 2, 0);
        return tbl;
    }

    private Control BuildTripleHost(Control c1, string? searchTip, Func<Task>? onSearch, Label? midLbl, Control c2, int secondWidth = 80)
    {
        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 4, 2), RowCount = 1 };
        int cols = 2 + (onSearch is not null ? 1 : 0) + (midLbl is not null ? 1 : 0);
        tbl.ColumnCount = cols;
        int col = 0;
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        c1.Dock = DockStyle.Fill; c1.Margin = new Padding(0, 2, 3, 0);
        tbl.Controls.Add(c1, col++, 0);

        if (onSearch is not null && searchTip is not null)
        {
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32f));
            var b = new Button { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 3, 1) };
            AppTheme.StyleSecondaryButton(b, AppIcon.Search, string.Empty, searchTip, null, _tt);
            b.Click += async (_, _) => await onSearch();
            tbl.Controls.Add(b, col++, 0);
        }
        if (midLbl is not null)
        {
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tbl.Controls.Add(midLbl, col++, 0);
        }
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, secondWidth));
        c2.Dock = DockStyle.Fill; c2.Margin = new Padding(0, 2, 0, 0);
        tbl.Controls.Add(c2, col, 0);
        return tbl;
    }

    private static void AddWeightRow(TableLayoutPanel tbl, int row, string title, NumericUpDown num, bool isNeto)
    {
        tbl.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = isNeto ? AppTheme.GoldSoft : AppTheme.GoldAccent,
            Font = new Font("Segoe UI", isNeto ? 11f : 9.75f, FontStyle.Bold)
        }, 0, row);
        num.Dock = DockStyle.Fill;
        num.Margin = new Padding(0, 2, 0, 2);
        num.BackColor = AppTheme.LcdBackground;
        num.ForeColor = isNeto ? Color.FromArgb(120, 255, 175) : AppTheme.LcdDigitOn;
        num.Font = new Font("Consolas", isNeto ? 17.5f : 14f, FontStyle.Bold);
        num.TextAlign = HorizontalAlignment.Right;
        tbl.Controls.Add(num, 1, row);
    }

    private void OnReadingReceived(object? sender, ScaleReadingEventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            _currentWeightKg = e.WeightKg;
            _currentStable = e.Stable;
            _lcd.UpdateReading(e.WeightKg, e.Stable, e.IsZero, e.IsDynamic, e.CommError, e.DataError, _services.Scale.IsSimulationActive);
            _lblEstadoBalanza.Text = _services.Scale.StatusDescription;
        });
    }

    private void OpenVisorDialog()
    {
        using var dlg = new VisorSetupForm(_services);
        dlg.ShowDialog(this);
        _lblEstadoBalanza.Text = _services.Scale.StatusDescription;
    }

    private void CaptureCurrentScaleWeight(bool isTrailer)
    {
        if (!_currentStable && !_services.TestMode)
        {
            _lblMsg.Text = "El peso está en movimiento. Espere estabilidad para capturar.";
            _lblMsg.ForeColor = AppTheme.AccentDanger;
            return;
        }
        AssignCapturedWeight(_currentWeightKg, isTrailer);
    }

    private void CaptureManualWeight()
    {
        using var dlg = new ManualWeightDialog(_currentWeightKg);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _services.Scale.EmitSimulatedWeight(dlg.WeightKg, stable: true);
            AssignCapturedWeight(dlg.WeightKg, isTrailer: _transitoActivo is not null || _numBruto.Value > 0);
        }
    }

    private void AssignCapturedWeight(int weightKg, bool isTrailer)
    {
        if (weightKg <= 0)
        {
            _lblMsg.Text = "El peso capturado debe ser mayor a cero kg (use 'Visor / COM' o 'Ingreso Manual').";
            _lblMsg.ForeColor = AppTheme.AccentDanger;
            return;
        }

        if (_transitoActivo is null && !isTrailer)
        {
            if (_cmbTipoPesaje.SelectedIndex == 0)
            {
                _numBruto.Value = weightKg;
                if (_chkUsarTaraReg.Checked && _camionActual is not null) _numTara.Value = _camionActual.Tara;
            }
            else
            {
                if (_numBruto.Value == 0) _numBruto.Value = weightKg;
                else _numTara.Value = weightKg;
            }
        }
        else if (_transitoActivo is not null)
        {
            if (_transitoActivo.Bruto > 0 && _numTara.Value == 0) _numTara.Value = weightKg;
            else if (_transitoActivo.Tara > 0 && _numBruto.Value == 0) _numBruto.Value = weightKg;
            else _numTara.Value = weightKg;
        }
        else
        {
            if (_numBruto.Value == 0) _numBruto.Value = weightKg;
            else _numTara.Value = weightKg;
        }

        RecalculateNet();
        _lblMsg.Text = $"Peso capturado correctamente: {weightKg:N0} kg.";
        _lblMsg.ForeColor = AppTheme.AccentEmerald;
    }

    private void RecalculateNet()
    {
        int b = (int)_numBruto.Value;
        int t = (int)_numTara.Value;
        _numNeto.Value = (b > 0 && t > 0) ? WeighingCalculator.CalcularNeto(b, t) : 0;
    }

    private async Task OnPatenteLeaveAsync()
    {
        string pat = ChileanValidators.NormalizarPatente(_txtPatente.Text);
        if (string.IsNullOrEmpty(pat)) return;
        _txtPatente.Text = pat;

        RegistroTransito? enTransito = await _services.Weighing.GetTransitoPorPatenteAsync(pat);
        if (enTransito is not null)
        {
            LoadFromTransito(enTransito);
            return;
        }

        _transitoActivo = null;
        _lblModoOperacion.Text = "MODO: PRIMER PESAJE / NUEVO INGRESO";
        _lblIngresoInfo.Text = $"Ingreso proyectado: {DateTime.Today:dd/MM/yyyy} {DateTime.Now:HH:mm} | Op: {_services.UsuarioActual?.Nombre}";
        _camionActual = await _services.Catalogs.GetCamionAsync(pat);
        if (_camionActual is not null)
        {
            _txtAcoplado.Text = _camionActual.Acoplado;
            _txtTranspCod.Text = _camionActual.Transportista;
            _txtChoferCod.Text = _camionActual.Chofer;
            _numTipoMop.Value = Math.Clamp((int)_camionActual.TipoMop, 0, 9999);
            _numLargo.Value = Math.Clamp((int)_camionActual.Largo, 0, 90);
            _lblTaraRef.Text = $"Tara Histórica:\r\n  {_camionActual.Tara:N0} kg\r\nFecha:\r\n  {_camionActual.FechaTara?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "-"}\r\nHora:\r\n  {_camionActual.HoraTara}";
            _lblMsg.Text = _camionActual.Tara <= 0
                ? "Advertencia: la tara histórica del camión está en 0."
                : $"Camión encontrado. Última tara registrada: {_camionActual.Tara:N0} kg.";
            _lblMsg.ForeColor = AppTheme.NavyPrimary;
            await ResolveTranspTextAsync();
            await ResolveChoferTextAsync();
        }
        else
        {
            _lblTaraRef.Text = "Tara Histórica:\r\n  (Nuevo)\r\nFecha:\r\n  -\r\nHora:\r\n  -";
        }
    }

    private void LoadFromTransito(RegistroTransito tr)
    {
        _transitoActivo = tr;
        _txtFolio.Text = tr.FolioPesaje.ToString(CultureInfo.InvariantCulture);
        _txtPatente.Text = tr.Patente;
        _txtAcoplado.Text = tr.Acoplado;
        _cmbTipoPesaje.SelectedIndex = Math.Clamp((int)tr.TipoPesaje - 1, 0, 2);
        _txtClienteRut.Text = tr.Cliente;
        _txtClienteNombre.Text = tr.NombreCliente;
        _txtProductoCod.Text = tr.Producto;
        _txtProductoNombre.Text = tr.NombreProducto;
        _txtOrigenCod.Text = tr.Origen;
        _txtDestinoCod.Text = tr.Destino;
        _txtTranspCod.Text = tr.Transportista;
        _txtChoferCod.Text = tr.Chofer;
        _txtGuia.Text = tr.GuiaDespacho;
        _numNetoInf.Value = Math.Clamp(tr.NetoInformado, 0, 200_000);
        _numFardos.Value = Math.Clamp(tr.Fardos, 0, 999_999);
        _numTipoMop.Value = Math.Clamp((int)tr.TipoMop, 0, 9999);
        _numLargo.Value = Math.Clamp((int)tr.Largo, 0, 90);
        _txtObs.Text = tr.Observacion1;
        _numBruto.Value = Math.Clamp(tr.Bruto, 0, 200_000);
        _numTara.Value = Math.Clamp(tr.Tara, 0, 200_000);
        RecalculateNet();

        _lblIngresoInfo.Text = $"Ingreso Tránsito: {tr.FechaIngreso:dd/MM/yyyy} {tr.HoraIngreso} | Op. ID {tr.UsuarioIngreso}";
        _lblModoOperacion.Text = $"MODO: SEGUNDO PESAJE ({TicketCustomizationHelper.FormatFolioHeader(tr.FolioPesaje)})";
        _lblMsg.Text = $"Vehículo {tr.Patente} cargado desde tránsito. Capture el segundo peso y presione GRABAR PESAJE.";
        _lblMsg.ForeColor = AppTheme.AccentEmerald;
    }

    private void ApplyRegisteredTare()
    {
        ResultadoValidacionTara val = WeighingCalculator.EvaluarTaraRegistrada(_camionActual, _services.ConfigActual, DateTime.Today, (int)_numBruto.Value > 0 ? (int)_numBruto.Value : null);
        if (!val.Permitida || _camionActual is null)
        {
            MessageBox.Show(this, $"No se puede usar TARA registrada del camión:\r\n{val.Motivo}", "Tara Registrada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _chkUsarTaraReg.Checked = true;
        _numTara.Value = _camionActual.Tara;
        RecalculateNet();
        _lblMsg.Text = $"Tara registrada aplicada: {_camionActual.Tara:N0} kg.";
        _lblMsg.ForeColor = AppTheme.AccentEmerald;
    }

    private async Task CreateManualTicketAsync()
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AgregarRegistro))
        {
            MessageBox.Show(this, "No está autorizado para crear tickets.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dlg = new TicketEditorDialog(_services, TicketEditorMode.CreateNew);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            await RefreshTransitoAsync();
            Pesaje? created = await _services.Weighing.GetPesajePorFolioAsync(dlg.SavedFolio);
            if (created is not null)
            {
                using var viewer = new TicketViewerDialog(_services, created);
                viewer.ShowDialog(this);
            }
        }
    }

    private async Task SaveWeighingAsync()
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AgregarRegistro))
        {
            MessageBox.Show(this, "No está autorizado para registrar pesajes.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string pat = ChileanValidators.NormalizarPatente(_txtPatente.Text);
        if (!ChileanValidators.EsPatenteValida(pat))
        {
            MessageBox.Show(this, "Debe ingresar una patente válida.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int bruto = (int)_numBruto.Value;
        int tara = (int)_numTara.Value;
        if (bruto <= 0 && tara <= 0)
        {
            MessageBox.Show(this, "Debe capturar al menos un peso (Bruto o Tara) antes de guardar.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            DateTime now = DateTime.Now;
            var tipo = (TipoPesaje)(_cmbTipoPesaje.SelectedIndex + 1);
            short userId = _services.UsuarioActual?.Id ?? 0;

            if (_transitoActivo is null && (bruto == 0 || tara == 0))
            {
                var tr = new RegistroTransito
                {
                    Patente = pat,
                    Acoplado = ChileanValidators.NormalizarPatente(_txtAcoplado.Text),
                    Producto = _txtProductoCod.Text.Trim(),
                    Cliente = _txtClienteRut.Text.Trim(),
                    Origen = _txtOrigenCod.Text.Trim(),
                    Destino = _txtDestinoCod.Text.Trim(),
                    GuiaDespacho = _txtGuia.Text.Trim(),
                    NetoInformado = (int)_numNetoInf.Value,
                    FechaIngreso = now.Date,
                    HoraIngreso = now.ToString("HH:mm", CultureInfo.InvariantCulture),
                    Bruto = bruto,
                    Tara = tara,
                    TipoMop = (short)_numTipoMop.Value,
                    Largo = (short)_numLargo.Value,
                    MaxBruto = _maxBrutoMop,
                    Chofer = _txtChoferCod.Text.Trim(),
                    Observacion1 = _txtObs.Text.Trim(),
                    Fardos = (int)_numFardos.Value,
                    Transportista = _txtTranspCod.Text.Trim(),
                    TipoPesaje = tipo,
                    UsuarioIngreso = userId
                };
                int folioTr = await _services.Weighing.RegistrarIngresoTransitoAsync(tr);
                await RefreshTransitoAsync();
                MessageBox.Show(this, $"Ingreso a planta registrado en Tránsito con {TicketCustomizationHelper.FormatFolioHeader(folioTr)}.", "Primer Pesaje", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ClearForm();
                return;
            }

            if (bruto <= 0 || tara <= 0)
            {
                MessageBox.Show(this, "Para cerrar el pesaje se requieren ambos pesos (Bruto y Tara > 0).", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var p = new Pesaje
            {
                FolioPesaje = _transitoActivo?.FolioPesaje ?? 0,
                Patente = pat,
                Acoplado = ChileanValidators.NormalizarPatente(_txtAcoplado.Text),
                FolioPesajeMop = _folioMopAsociado,
                Producto = _txtProductoCod.Text.Trim(),
                NombreProducto = _txtProductoNombre.Text.Trim(),
                Cliente = _txtClienteRut.Text.Trim(),
                NombreCliente = _txtClienteNombre.Text.Trim(),
                Origen = _txtOrigenCod.Text.Trim(),
                NombreOrigen = _txtOrigenNombre.Text.Trim(),
                Destino = _txtDestinoCod.Text.Trim(),
                NombreDestino = _txtDestinoNombre.Text.Trim(),
                GuiaDespacho = _txtGuia.Text.Trim(),
                NetoInformado = (int)_numNetoInf.Value,
                FechaIngreso = _transitoActivo?.FechaIngreso ?? now.Date,
                HoraIngreso = _transitoActivo?.HoraIngreso ?? now.ToString("HH:mm", CultureInfo.InvariantCulture),
                FechaSalida = now.Date,
                HoraSalida = now.ToString("HH:mm", CultureInfo.InvariantCulture),
                Bruto = bruto,
                Tara = tara,
                Neto = WeighingCalculator.CalcularNeto(bruto, tara),
                TipoMop = (short)_numTipoMop.Value,
                Largo = (short)_numLargo.Value,
                MaxBruto = _maxBrutoMop,
                EstadoMop = _estadoMopActual,
                Chofer = _txtChoferCod.Text.Trim(),
                NombreChofer = _txtChoferNombre.Text.Trim(),
                Observacion1 = _txtObs.Text.Trim(),
                Fardos = (int)_numFardos.Value,
                Transportista = _txtTranspCod.Text.Trim(),
                NombreTransportista = _txtTranspNombre.Text.Trim(),
                TaraRegistrada = _chkUsarTaraReg.Checked,
                TipoPesaje = tipo,
                UsuarioIngreso = _transitoActivo?.UsuarioIngreso ?? userId,
                NombreUsuarioIngreso = _services.UsuarioActual?.Nombre ?? string.Empty,
                UsuarioSalida = userId,
                NombreUsuarioSalida = _services.UsuarioActual?.Nombre ?? string.Empty
            };

            int folioFinal = await _services.Weighing.CompletarPesajeAsync(p);
            p.FolioPesaje = folioFinal;
            await RefreshTransitoAsync();

            if (PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ImprimirTicket))
            {
                bool showTicket = !_services.ConfigActual.ConfirmarImpresionTicket ||
                                  MessageBox.Show(this, $"Pesaje {TicketCustomizationHelper.FormatFolioHeader(folioFinal)} guardado (Neto: {p.Neto:N0} kg).\r\n¿Desea visualizar / imprimir el ticket?", "Pesaje Completado", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                if (showTicket)
                {
                    using var viewer = new TicketViewerDialog(_services, p, isEnProceso: false);
                    viewer.ShowDialog(this);
                }
            }
            else
            {
                MessageBox.Show(this, $"Pesaje {TicketCustomizationHelper.FormatFolioHeader(folioFinal)} completado. Peso Neto: {p.Neto:N0} kg.", "Pesaje Completado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            ClearForm();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Pesaje", "Error guardando pesaje comercial.", ex);
            MessageBox.Show(this, $"Error guardando pesaje:\r\n{ex.Message}", "Error BD", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DeleteSelectedTransitAsync()
    {
        if (_gridTransito.CurrentRow?.Tag is not RegistroTransito tr)
        {
            MessageBox.Show(this, "Seleccione un camión en tránsito de la tabla inferior.", "En Tránsito", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.EliminarRegistro))
        {
            MessageBox.Show(this, "No está autorizado para eliminar registros de tránsito.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(this, $"¿Eliminar de tránsito el folio #{tr.FolioPesaje} ({tr.Patente})?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        await _services.Weighing.EliminarTransitoAsync(tr.FolioPesaje, _services.UsuarioActual?.Id ?? 0, "Anulado desde pantalla de pesaje");
        await RefreshTransitoAsync();
        if (_transitoActivo?.FolioPesaje == tr.FolioPesaje) ClearForm();
    }

    private async Task OpenMopWeighingAsync()
    {
        short tipo = (short)_numTipoMop.Value;
        if (tipo <= 0) tipo = 510;
        short largo = (short)Math.Max(4, (int)_numLargo.Value);
        using var mop = new PesajeMopForm(_services, _txtPatente.Text, tipo, largo, _transitoActivo?.FolioPesaje);
        if (mop.ShowDialog(this) == DialogResult.OK && mop.ResultadoGuardado is not null)
        {
            _folioMopAsociado = mop.ResultadoGuardado.FolioMop;
            _estadoMopActual = mop.ResultadoGuardado.Estado;
            _maxBrutoMop = mop.ResultadoGuardado.Maximo;
            _numTipoMop.Value = mop.ResultadoGuardado.TipoMop;
            _numLargo.Value = mop.ResultadoGuardado.Largo;
            _numBruto.Value = mop.ResultadoGuardado.Bruto;
            RecalculateNet();
        }
    }

    private async Task ReimprimirPorFolioAsync()
    {
        using var dlg = new FolioTicketDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        Pesaje? p = await _services.Weighing.GetPesajePorFolioAsync(dlg.Folio);
        if (p is not null)
        {
            using var viewer = new TicketViewerDialog(_services, p, isEnProceso: false);
            viewer.ShowDialog(this);
            return;
        }

        List<RegistroTransito> trs = await _services.Weighing.GetTransitoAsync();
        RegistroTransito? tr = trs.FirstOrDefault(x => x.FolioPesaje == dlg.Folio);
        if (tr is not null)
        {
            using var viewer = new TicketViewerDialog(_services, TicketEditorDialog.ToPesajeView(tr), isEnProceso: true);
            viewer.ShowDialog(this);
            return;
        }

        MessageBox.Show(this, $"No se encontró el folio #{dlg.Folio} en histórico ni en tránsito.", "Búsqueda", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public void ClearForm()
    {
        _transitoActivo = null;
        _camionActual = null;
        _folioMopAsociado = 0;
        _estadoMopActual = EstadoNormaMop.NoAplica;
        _maxBrutoMop = 0;
        _txtFolio.Text = string.Empty;
        _txtPatente.Text = _txtAcoplado.Text = _txtClienteRut.Text = _txtClienteNombre.Text = string.Empty;
        _txtProductoCod.Text = _txtProductoNombre.Text = _txtOrigenCod.Text = _txtOrigenNombre.Text = string.Empty;
        _txtDestinoCod.Text = _txtDestinoNombre.Text = _txtTranspCod.Text = _txtTranspNombre.Text = string.Empty;
        _txtChoferCod.Text = _txtChoferNombre.Text = _txtGuia.Text = _txtObs.Text = string.Empty;
        _numNetoInf.Value = _numFardos.Value = _numTipoMop.Value = _numLargo.Value = _numBruto.Value = _numTara.Value = _numNeto.Value = 0;
        _chkUsarTaraReg.Checked = false;
        _lblTaraRef.Text = "Tara Histórica:\r\n  -\r\nFecha:\r\n  -\r\nHora:\r\n  -";
        _lblIngresoInfo.Text = "Ingreso: Pendiente de primer pesaje.";
        _lblModoOperacion.Text = "MODO: PRIMER PESAJE / NUEVO INGRESO";
        _lblMsg.Text = "Formulario listo para nuevo pesaje.";
        _lblMsg.ForeColor = AppTheme.NavyPrimary;
    }

    private async Task ResolveClienteTextAsync()
    {
        string rut = _txtClienteRut.Text.Trim();
        if (string.IsNullOrEmpty(rut)) { _txtClienteNombre.Text = string.Empty; return; }
        Cliente? c = await _services.Catalogs.GetClienteAsync(rut);
        if (c is not null) _txtClienteNombre.Text = c.Nombre;
    }

    private async Task ResolveProductoTextAsync()
    {
        string cod = _txtProductoCod.Text.Trim();
        if (string.IsNullOrEmpty(cod)) { _txtProductoNombre.Text = string.Empty; return; }
        Producto? p = (await _services.Catalogs.GetProductosAsync()).FirstOrDefault(x => string.Equals(x.Codigo, cod, StringComparison.OrdinalIgnoreCase));
        if (p is not null) _txtProductoNombre.Text = p.Descripcion;
    }

    private async Task ResolveOrigenTextAsync()
    {
        string cod = _txtOrigenCod.Text.Trim();
        if (string.IsNullOrEmpty(cod)) { _txtOrigenNombre.Text = string.Empty; return; }
        Origen? o = (await _services.Catalogs.GetOrigenesAsync()).FirstOrDefault(x => string.Equals(x.Codigo, cod, StringComparison.OrdinalIgnoreCase));
        if (o is not null) _txtOrigenNombre.Text = o.Descripcion;
    }

    private async Task ResolveDestinoTextAsync()
    {
        string cod = _txtDestinoCod.Text.Trim();
        if (string.IsNullOrEmpty(cod)) { _txtDestinoNombre.Text = string.Empty; return; }
        Destino? d = (await _services.Catalogs.GetDestinosAsync()).FirstOrDefault(x => string.Equals(x.Codigo, cod, StringComparison.OrdinalIgnoreCase));
        if (d is not null) _txtDestinoNombre.Text = d.Descripcion;
    }

    private async Task ResolveTranspTextAsync()
    {
        string cod = _txtTranspCod.Text.Trim();
        if (string.IsNullOrEmpty(cod)) { _txtTranspNombre.Text = string.Empty; return; }
        Transportista? t = (await _services.Catalogs.GetTransportistasAsync()).FirstOrDefault(x => string.Equals(x.Rut, cod, StringComparison.OrdinalIgnoreCase));
        if (t is not null) _txtTranspNombre.Text = t.Nombre;
    }

    private async Task ResolveChoferTextAsync()
    {
        string cod = _txtChoferCod.Text.Trim();
        if (string.IsNullOrEmpty(cod)) { _txtChoferNombre.Text = string.Empty; return; }
        Chofer? ch = (await _services.Catalogs.GetChoferesAsync()).FirstOrDefault(x => string.Equals(x.Rut, cod, StringComparison.OrdinalIgnoreCase));
        if (ch is not null) _txtChoferNombre.Text = ch.Nombre;
    }

    private async Task LookupCamionAsync()
    {
        List<Camion> list = await _services.Catalogs.GetCamionesAsync();
        using var dlg = new CatalogLookupDialog<Camion>("Seleccionar Camión", list, c => $"{c.Patente} {c.Acoplado}", ("Patente", c => c.Patente, 90), ("Acoplado", c => c.Acoplado, 80), ("Tara", c => $"{c.Tara:N0}", 85));
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null)
        {
            _txtPatente.Text = dlg.SelectedItem.Patente;
            await OnPatenteLeaveAsync();
        }
    }

    private async Task LookupClienteAsync()
    {
        List<Cliente> list = await _services.Catalogs.GetClientesAsync();
        using var dlg = new CatalogLookupDialog<Cliente>("Seleccionar Cliente", list, c => $"{c.Rut} {c.Nombre}", ("RUT", c => $"{c.Rut}-{c.Digito}", 90), ("Nombre", c => c.Nombre, 220), ("Ciudad", c => c.Ciudad, 110));
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null)
        {
            _txtClienteRut.Text = dlg.SelectedItem.Rut;
            _txtClienteNombre.Text = dlg.SelectedItem.Nombre;
        }
    }

    private async Task LookupProductoAsync()
    {
        List<Producto> list = await _services.Catalogs.GetProductosAsync();
        using var dlg = new CatalogLookupDialog<Producto>("Seleccionar Producto", list, p => $"{p.Codigo} {p.Descripcion}", ("Código", p => p.Codigo, 100), ("Descripción", p => p.Descripcion, 260));
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null)
        {
            _txtProductoCod.Text = dlg.SelectedItem.Codigo;
            _txtProductoNombre.Text = dlg.SelectedItem.Descripcion;
        }
    }

    private async Task LookupOrigenAsync()
    {
        List<Origen> list = await _services.Catalogs.GetOrigenesAsync();
        using var dlg = new CatalogLookupDialog<Origen>("Seleccionar Origen", list, o => $"{o.Codigo} {o.Descripcion}", ("Código", o => o.Codigo, 80), ("Descripción", o => o.Descripcion, 260));
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null)
        {
            _txtOrigenCod.Text = dlg.SelectedItem.Codigo;
            _txtOrigenNombre.Text = dlg.SelectedItem.Descripcion;
        }
    }

    private async Task LookupDestinoAsync()
    {
        List<Destino> list = await _services.Catalogs.GetDestinosAsync();
        using var dlg = new CatalogLookupDialog<Destino>("Seleccionar Destino", list, d => $"{d.Codigo} {d.Descripcion}", ("Código", d => d.Codigo, 80), ("Descripción", d => d.Descripcion, 260));
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null)
        {
            _txtDestinoCod.Text = dlg.SelectedItem.Codigo;
            _txtDestinoNombre.Text = dlg.SelectedItem.Descripcion;
        }
    }

    private async Task LookupTranspAsync()
    {
        List<Transportista> list = await _services.Catalogs.GetTransportistasAsync();
        using var dlg = new CatalogLookupDialog<Transportista>("Seleccionar Transportista", list, t => $"{t.Rut} {t.Nombre}", ("RUT", t => t.Rut, 95), ("Nombre", t => t.Nombre, 240));
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null)
        {
            _txtTranspCod.Text = dlg.SelectedItem.Rut;
            _txtTranspNombre.Text = dlg.SelectedItem.Nombre;
        }
    }

    private async Task LookupChoferAsync()
    {
        List<Chofer> list = await _services.Catalogs.GetChoferesAsync();
        using var dlg = new CatalogLookupDialog<Chofer>("Seleccionar Chofer", list, c => $"{c.Rut} {c.Nombre}", ("RUT", c => c.Rut, 95), ("Nombre", c => c.Nombre, 240));
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null)
        {
            _txtChoferCod.Text = dlg.SelectedItem.Rut;
            _txtChoferNombre.Text = dlg.SelectedItem.Nombre;
        }
    }
}
