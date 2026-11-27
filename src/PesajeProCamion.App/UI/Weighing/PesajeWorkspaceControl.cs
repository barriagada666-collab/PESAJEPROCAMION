using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Controls;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;
using PesajeProCamion.Domain.Services;
using PesajeProCamion.Domain.Validation;
using PesajeProCamion.Hardware.Printing;
using PesajeProCamion.Hardware.Scale;

namespace PesajeProCamion.App.UI.Weighing;

/// <summary>Espacio de trabajo principal de Pesaje Comercial (Unificado con búsqueda, captura serial/manual, tara registrada y tránsito).</summary>
public sealed class PesajeWorkspaceControl : UserControl
{
    private readonly AppServices _services;
    private readonly ToolTip _tt = new();
    private readonly DigitalWeightDisplay _lcd = new();
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
    private readonly TextBox _txtGuia = new() { MaxLength = 12, CharacterCasing = CharacterCasing.Upper };
    private readonly NumericUpDown _numNetoInf = new() { Maximum = 200_000, ThousandsSeparator = true };
    private readonly NumericUpDown _numTipoMop = new() { Maximum = 9999 };
    private readonly NumericUpDown _numLargo = new() { Maximum = 90 };
    private readonly TextBox _txtObs = new() { MaxLength = 120 };
    private readonly CheckBox _chkUsarTaraReg = new();

    private readonly NumericUpDown _numBruto = new() { Maximum = 200_000, ThousandsSeparator = true, ReadOnly = true };
    private readonly NumericUpDown _numTara = new() { Maximum = 200_000, ThousandsSeparator = true, ReadOnly = true };
    private readonly NumericUpDown _numNeto = new() { Maximum = 200_000, ThousandsSeparator = true, ReadOnly = true };
    private readonly Label _lblMsg = new();
    private readonly DataGridView _gridTransito = new();

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
        List<RegistroTransito> rows = await _services.Weighing.GetTransitoAsync();
        _gridTransito.Rows.Clear();
        foreach (RegistroTransito t in rows)
        {
            int idx = _gridTransito.Rows.Add(
                t.FolioPesaje,
                t.TipoPesaje.ToString(),
                t.Patente,
                t.FechaIngreso.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                t.HoraIngreso,
                $"{t.Bruto:N0}",
                $"{t.Tara:N0}",
                t.NombreCliente,
                t.NombreProducto);
            _gridTransito.Rows[idx].Tag = t;
        }
        TransitUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void BuildUi()
    {
        // Header with Truck visual + Status + Digital 7-Segment Display
        var header = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = AppTheme.NavyDark, Padding = new Padding(14, 10, 14, 10) };
        var picTruck = new PictureBox { Location = new Point(14, 12), Size = new Size(105, 78), SizeMode = PictureBoxSizeMode.Zoom, Image = IconCatalog.RenderTruckBadge(105, 78) };
        var lblEmp = new Label
        {
            Location = new Point(132, 14),
            Size = new Size(420, 24),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Text = string.IsNullOrWhiteSpace(_services.BasculaActual.NombreEmpresa) ? "BÁSCULA COMERCIAL" : _services.BasculaActual.NombreEmpresa
        };
        _lblModoOperacion.Location = new Point(132, 42);
        _lblModoOperacion.Size = new Size(420, 24);
        _lblModoOperacion.ForeColor = AppTheme.LcdDigitOn;
        _lblModoOperacion.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        _lblModoOperacion.Text = "MODO: PRIMER PESAJE / NUEVO INGRESO";

        _lblEstadoBalanza.Location = new Point(132, 68);
        _lblEstadoBalanza.Size = new Size(420, 20);
        _lblEstadoBalanza.ForeColor = Color.LightSteelBlue;
        _lblEstadoBalanza.Font = new Font("Segoe UI", 9f);
        _lblEstadoBalanza.Text = _services.Scale.StatusDescription;

        _lcd.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _lcd.Location = new Point(header.Width - 365, 11);
        _lcd.Size = new Size(348, 82);
        header.Controls.AddRange([picTruck, lblEmp, _lblModoOperacion, _lblEstadoBalanza, _lcd]);

        // Consistent Action Toolbar
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = AppTheme.SurfaceCard, Padding = new Padding(8, 4, 8, 4) };
        ToolStripButton btnCancelar = AppTheme.CreateToolButton(AppIcon.Cancel, "Cancelar Pesaje", "Limpiar formulario actual", AppTheme.AccentDanger);
        ToolStripButton btnBuscarFolio = AppTheme.CreateToolButton(AppIcon.Search, "Reimprimir Folio", "Buscar ticket por folio", AppTheme.NavyPrimary);
        ToolStripButton btnCapturar = AppTheme.CreateToolButton(AppIcon.Scale, "Capturar Peso", "Capturar peso actual desde indicador (F5)", AppTheme.AccentEmerald);
        ToolStripButton btnManual = AppTheme.CreateToolButton(AppIcon.ManualWeight, "Ingreso Manual", "Digitar peso manualmente si está habilitado", AppTheme.AccentAmber);
        ToolStripButton btnTaraReg = AppTheme.CreateToolButton(AppIcon.Tare, "Aplicar Tara Reg.", "Aplicar tara registrada del camión", AppTheme.NavyPrimary);
        ToolStripButton btnGuardar = AppTheme.CreateToolButton(AppIcon.Save, "Guardar Pesaje", "Guardar tránsito o completar salida", AppTheme.NavyDark);
        ToolStripButton btnMop = AppTheme.CreateToolButton(AppIcon.AxleMop, "Pesaje Ejes MOP", "Abrir pesaje por ejes estático/dinámico", AppTheme.NavyPrimary);
        ToolStripButton btnAnularTr = AppTheme.CreateToolButton(AppIcon.Delete, "Anular Tránsito", "Eliminar camión seleccionado en tránsito", AppTheme.AccentDanger);

        btnCancelar.Click += (_, _) => ClearForm();
        btnBuscarFolio.Click += async (_, _) => await ReimprimirPorFolioAsync();
        btnCapturar.Click += (_, _) => CaptureCurrentScaleWeight();
        btnManual.Click += (_, _) => CaptureManualWeight();
        btnTaraReg.Click += (_, _) => ApplyRegisteredTare();
        btnGuardar.Click += async (_, _) => await SaveWeighingAsync();
        btnMop.Click += async (_, _) => await OpenMopWeighingAsync();
        btnAnularTr.Click += async (_, _) => await DeleteSelectedTransitAsync();

        toolbar.Items.AddRange([
            btnCancelar, btnBuscarFolio, new ToolStripSeparator(),
            btnCapturar, btnManual, btnTaraReg, new ToolStripSeparator(),
            btnGuardar, btnMop, new ToolStripSeparator(),
            btnAnularTr
        ]);

        // Form Card + Transit Grid SplitContainer
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 325, BackColor = AppTheme.BorderSubtle };
        var formCard = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.SurfaceCard, AutoScroll = true, Padding = new Padding(14) };

        _cmbTipoPesaje.Items.AddRange(["1 - Recepción de Carga", "2 - Despacho de Carga", "3 - Movimiento Interno"]);
        _cmbTipoPesaje.SelectedIndex = 0;

        int c1L = 16, c1X = 115, c2L = 435, c2X = 535, y = 12;
        AddLbl(formCard, "Patente *", c1L, y); _txtPatente.SetBounds(c1X, y - 2, 105, 24);
        Button bPat = AddSearchBtn(formCard, c1X + 110, y - 3, "Buscar camión registrado (F2)", async () => await LookupCamionAsync());
        AddLbl(formCard, "Acoplado", 275, y); _txtAcoplado.SetBounds(342, y - 2, 82, 24);
        AddLbl(formCard, "Tipo Pesaje", c2L, y); _cmbTipoPesaje.SetBounds(c2X, y - 2, 210, 24);
        AddLbl(formCard, "Folio", 760, y); _txtFolio.SetBounds(808, y - 2, 95, 24);

        _txtPatente.Leave += async (_, _) => await OnPatenteLeaveAsync();

        y += 34;
        AddLbl(formCard, "Cliente *", c1L, y); _txtClienteRut.SetBounds(c1X, y - 2, 95, 24);
        AddSearchBtn(formCard, c1X + 98, y - 3, "Buscar cliente (F2)", async () => await LookupClienteAsync());
        _txtClienteNombre.SetBounds(c1X + 132, y - 2, 180, 24);
        AddLbl(formCard, "Producto *", c2L, y); _txtProductoCod.SetBounds(c2X, y - 2, 95, 24);
        AddSearchBtn(formCard, c2X + 98, y - 3, "Buscar producto (F2)", async () => await LookupProductoAsync());
        _txtProductoNombre.SetBounds(c2X + 132, y - 2, 236, 24);

        y += 34;
        AddLbl(formCard, "Origen", c1L, y); _txtOrigenCod.SetBounds(c1X, y - 2, 75, 24);
        AddSearchBtn(formCard, c1X + 78, y - 3, "Buscar origen (F2)", async () => await LookupOrigenAsync());
        _txtOrigenNombre.SetBounds(c1X + 112, y - 2, 200, 24);
        AddLbl(formCard, "Destino", c2L, y); _txtDestinoCod.SetBounds(c2X, y - 2, 75, 24);
        AddSearchBtn(formCard, c2X + 78, y - 3, "Buscar destino (F2)", async () => await LookupDestinoAsync());
        _txtDestinoNombre.SetBounds(c2X + 112, y - 2, 256, 24);

        y += 34;
        AddLbl(formCard, "Transportista", c1L, y); _txtTranspCod.SetBounds(c1X, y - 2, 95, 24);
        AddSearchBtn(formCard, c1X + 98, y - 3, "Buscar transportista (F2)", async () => await LookupTranspAsync());
        _txtTranspNombre.SetBounds(c1X + 132, y - 2, 180, 24);
        AddLbl(formCard, "Chofer", c2L, y); _txtChoferCod.SetBounds(c2X, y - 2, 95, 24);
        AddSearchBtn(formCard, c2X + 98, y - 3, "Buscar chofer (F2)", async () => await LookupChoferAsync());
        _txtChoferNombre.SetBounds(c2X + 132, y - 2, 236, 24);

        y += 34;
        AddLbl(formCard, "Guía Desp.", c1L, y); _txtGuia.SetBounds(c1X, y - 2, 110, 24);
        AddLbl(formCard, "Neto Inf.", 240, y); _numNetoInf.SetBounds(308, y - 2, 110, 24);
        AddLbl(formCard, "Tipo MOP", c2L, y); _numTipoMop.SetBounds(c2X, y - 2, 76, 24);
        AddLbl(formCard, "Largo (m)", 625, y); _numLargo.SetBounds(692, y - 2, 65, 24);
        _chkUsarTaraReg.Text = "Usar Tara Reg."; _chkUsarTaraReg.SetBounds(775, y, 130, 22);

        y += 34;
        AddLbl(formCard, "Observación", c1L, y); _txtObs.SetBounds(c1X, y - 2, 788, 24);

        // Weight Summary Boxes
        y += 38;
        var weightStrip = new Panel { Location = new Point(c1L, y), Size = new Size(887, 68), BackColor = AppTheme.NavyDark };
        AddWeightBox(weightStrip, "PESO BRUTO (kg)", _numBruto, 16);
        AddWeightBox(weightStrip, "PESO TARA (kg)", _numTara, 305);
        AddWeightBox(weightStrip, "PESO NETO (kg)", _numNeto, 594);

        _lblMsg.Location = new Point(c1L, y + 74);
        _lblMsg.Size = new Size(887, 22);
        _lblMsg.Font = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        _lblMsg.ForeColor = AppTheme.NavyPrimary;

        formCard.Controls.AddRange([
            _txtPatente, bPat, _txtAcoplado, _cmbTipoPesaje, _txtFolio,
            _txtClienteRut, _txtClienteNombre, _txtProductoCod, _txtProductoNombre,
            _txtOrigenCod, _txtOrigenNombre, _txtDestinoCod, _txtDestinoNombre,
            _txtTranspCod, _txtTranspNombre, _txtChoferCod, _txtChoferNombre,
            _txtGuia, _numNetoInf, _numTipoMop, _numLargo, _chkUsarTaraReg,
            _txtObs, weightStrip, _lblMsg
        ]);

        // Transit Grid with Title Bar
        var transitWrapper = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.SurfaceCard };
        var transitTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "  CAMIONES PENDIENTES EN PLANTA (EN TRÁNSITO) — Doble clic para completar segundo pesaje",
            BackColor = AppTheme.SlateHeader,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        AppTheme.StyleDataGrid(_gridTransito);
        _gridTransito.Dock = DockStyle.Fill;
        _gridTransito.Columns.AddRange([
            new DataGridViewTextBoxColumn { HeaderText = "Folio", FillWeight = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "Tipo", FillWeight = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "Patente", FillWeight = 75 },
            new DataGridViewTextBoxColumn { HeaderText = "Fecha Ing.", FillWeight = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "Hora Ing.", FillWeight = 65 },
            new DataGridViewTextBoxColumn { HeaderText = "Bruto (kg)", FillWeight = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "Tara (kg)", FillWeight = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "Cliente", FillWeight = 160 },
            new DataGridViewTextBoxColumn { HeaderText = "Producto", FillWeight = 140 }
        ]);
        _gridTransito.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _gridTransito.Rows[e.RowIndex].Tag is RegistroTransito tr)
            {
                LoadFromTransito(tr);
            }
        };
        transitWrapper.Controls.AddRange([_gridTransito, transitTitle]);

        split.Panel1.Controls.Add(formCard);
        split.Panel2.Controls.Add(transitWrapper);

        Controls.AddRange([split, toolbar, header]);
    }

    private static void AddLbl(Panel parent, string text, int x, int y)
        => parent.Controls.Add(new Label { Text = text, Location = new Point(x, y + 2), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = AppTheme.TextPrimary });

    private Button AddSearchBtn(Panel parent, int x, int y, string tip, Func<Task> onClick)
    {
        var btn = new Button { Location = new Point(x, y), Size = new Size(28, 26) };
        AppTheme.StyleSecondaryButton(btn, AppIcon.Search, string.Empty, tip, null, _tt);
        btn.Click += async (_, _) => await onClick();
        parent.Controls.Add(btn);
        return btn;
    }

    private static void AddWeightBox(Panel parent, string title, NumericUpDown num, int x)
    {
        parent.Controls.Add(new Label { Text = title, Location = new Point(x, 6), AutoSize = true, ForeColor = Color.LightSteelBlue, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) });
        num.Location = new Point(x, 26);
        num.Size = new Size(268, 34);
        num.BackColor = AppTheme.LcdBackground;
        num.ForeColor = AppTheme.LcdDigitOn;
        num.Font = new Font("Consolas", 15f, FontStyle.Bold);
        num.TextAlign = HorizontalAlignment.Right;
        parent.Controls.Add(num);
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

    private void CaptureCurrentScaleWeight()
    {
        if (!_currentStable && !_services.TestMode)
        {
            _lblMsg.Text = "El peso está en movimiento. Espere estabilidad para capturar.";
            _lblMsg.ForeColor = AppTheme.AccentDanger;
            return;
        }
        AssignCapturedWeight(_currentWeightKg);
    }

    private void CaptureManualWeight()
    {
        using var dlg = new ManualWeightDialog(_currentWeightKg);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _services.Scale.EmitSimulatedWeight(dlg.WeightKg, stable: true);
            AssignCapturedWeight(dlg.WeightKg);
        }
    }

    private void AssignCapturedWeight(int weightKg)
    {
        if (weightKg <= 0)
        {
            _lblMsg.Text = "El peso capturado debe ser mayor a cero kg.";
            _lblMsg.ForeColor = AppTheme.AccentDanger;
            return;
        }

        if (_transitoActivo is null)
        {
            if (_cmbTipoPesaje.SelectedIndex == 0) // Recepción entra cargado -> Bruto
            {
                _numBruto.Value = weightKg;
                if (_chkUsarTaraReg.Checked && _camionActual is not null)
                {
                    _numTara.Value = _camionActual.Tara;
                }
            }
            else // Despacho/Mov entra por lo general vacío -> o primer peso en Bruto/Tara según convención
            {
                if (_numBruto.Value == 0) _numBruto.Value = weightKg;
                else _numTara.Value = weightKg;
            }
        }
        else
        {
            if (_transitoActivo.Bruto > 0 && _numTara.Value == 0)
            {
                _numTara.Value = weightKg;
            }
            else if (_transitoActivo.Tara > 0 && _numBruto.Value == 0)
            {
                _numBruto.Value = weightKg;
            }
            else
            {
                _numTara.Value = weightKg;
            }
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
        _camionActual = await _services.Catalogs.GetCamionAsync(pat);
        if (_camionActual is not null)
        {
            _txtAcoplado.Text = _camionActual.Acoplado;
            _txtTranspCod.Text = _camionActual.Transportista;
            _txtChoferCod.Text = _camionActual.Chofer;
            _numTipoMop.Value = Math.Clamp((int)_camionActual.TipoMop, 0, 9999);
            _numLargo.Value = Math.Clamp((int)_camionActual.Largo, 0, 90);
            _lblMsg.Text = _camionActual.Tara <= 0
                ? "Advertencia: la tara histórica del camión está en 0."
                : $"Camión encontrado. Última tara registrada: {_camionActual.Tara:N0} kg.";
            _lblMsg.ForeColor = AppTheme.NavyPrimary;
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
        _numTipoMop.Value = Math.Clamp((int)tr.TipoMop, 0, 9999);
        _numLargo.Value = Math.Clamp((int)tr.Largo, 0, 90);
        _txtObs.Text = tr.Observacion1;
        _numBruto.Value = Math.Clamp(tr.Bruto, 0, 200_000);
        _numTara.Value = Math.Clamp(tr.Tara, 0, 200_000);
        RecalculateNet();

        _lblModoOperacion.Text = $"MODO: SEGUNDO PESAJE (Folio Tránsito #{tr.FolioPesaje})";
        _lblMsg.Text = $"Vehículo {tr.Patente} cargado desde tránsito. Capture el segundo peso para finalizar.";
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

        DateTime now = DateTime.Now;
        var tipo = (TipoPesaje)(_cmbTipoPesaje.SelectedIndex + 1);
        short userId = _services.UsuarioActual?.Id ?? 0;

        // Si falta uno de los pesos y no usa tara registrada -> queda en DBTRANSITO (Primer Pesaje)
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
                Transportista = _txtTranspCod.Text.Trim(),
                TipoPesaje = tipo,
                UsuarioIngreso = userId
            };
            int folioTr = await _services.Weighing.RegistrarIngresoTransitoAsync(tr);
            await RefreshTransitoAsync();
            MessageBox.Show(this, $"Ingreso a planta registrado en Tránsito con Folio #{folioTr}.", "Primer Pesaje", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ClearForm();
            return;
        }

        if (bruto <= 0 || tara <= 0)
        {
            MessageBox.Show(this, "Para cerrar el pesaje se requieren ambos pesos (Bruto y Tara > 0).", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Segundo Pesaje o Pesaje Único con Tara Registrada -> Guardar en DBPESAJES
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

        if (PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ImprimirTicket) &&
            (!_services.ConfigActual.ConfirmarImpresionTicket ||
             MessageBox.Show(this, $"Pesaje #{folioFinal} guardado (Neto: {p.Neto:N0} kg).\r\n¿Desea imprimir/previsualizar el ticket?", "Pesaje Completado", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes))
        {
            using PrintDocument doc = TicketPrinterService.CreateCommercialTicketDocument(p, _services.BasculaActual, _services.ConfigActual);
            using var prev = new PrintPreviewDialog { Document = doc, Width = 780, Height = 580 };
            prev.ShowDialog(this);
        }
        else
        {
            MessageBox.Show(this, $"Pesaje #{folioFinal} completado. Peso Neto: {p.Neto:N0} kg.", "Pesaje Completado", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        ClearForm();
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
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ImprimirTicket))
        {
            MessageBox.Show(this, "No tiene permisos de impresión.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dlg = new FolioTicketDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        Pesaje? p = await _services.Weighing.GetPesajePorFolioAsync(dlg.Folio);
        if (p is null)
        {
            MessageBox.Show(this, $"No se encontró el folio #{dlg.Folio}.", "Búsqueda", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using PrintDocument doc = TicketPrinterService.CreateCommercialTicketDocument(p, _services.BasculaActual, _services.ConfigActual);
        using var prev = new PrintPreviewDialog { Document = doc, Width = 780, Height = 580 };
        prev.ShowDialog(this);
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
        _numNetoInf.Value = _numTipoMop.Value = _numLargo.Value = _numBruto.Value = _numTara.Value = _numNeto.Value = 0;
        _chkUsarTaraReg.Checked = false;
        _lblModoOperacion.Text = "MODO: PRIMER PESAJE / NUEVO INGRESO";
        _lblMsg.Text = "Formulario listo para nuevo pesaje.";
        _lblMsg.ForeColor = AppTheme.NavyPrimary;
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
