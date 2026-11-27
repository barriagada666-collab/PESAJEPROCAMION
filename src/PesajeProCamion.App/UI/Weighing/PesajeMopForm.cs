using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Controls;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Services;
using PesajeProCamion.Domain.Validation;
using PesajeProCamion.Hardware.Printing;
using PesajeProCamion.Hardware.Scale;

namespace PesajeProCamion.App.UI.Weighing;

/// <summary>Módulo de Pesaje por Ejes MOP (estático y dinámico) con evaluación Decreto 158/80 y Ley 19.171/92.</summary>
public sealed class PesajeMopForm : Form
{
    private readonly AppServices _services;
    private readonly DigitalWeightDisplay _lcd = new();
    private readonly DynamicAxleProcessor _dynamicProcessor = new();

    private readonly TextBox _txtFolio = new() { ReadOnly = true };
    private readonly TextBox _txtPatente = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 8 };
    private readonly TextBox _txtAcoplado = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 8 };
    private readonly ComboBox _cmbTipoMop = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _numLargo = new() { Minimum = 4, Maximum = 90, Value = 18 };
    private readonly TextBox _txtTransp = new();
    private readonly TextBox _txtChofer = new();
    private readonly TextBox _txtGuia = new();
    private readonly ComboBox _cmbTipoCarga = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly DataGridView _gridEjes = new();
    private readonly DataGridView _gridTandems = new();
    private readonly Label _lblBruto = new();
    private readonly Label _lblMaximo = new();
    private readonly Label _lblExceso = new();
    private readonly Label _lblEstado = new();
    private readonly Panel _semaforo = new();

    private readonly Button _btnModo = new();
    private readonly Button _btnIniciar = new();
    private readonly Button _btnRegEje = new();
    private readonly Button _btnDemo = new();
    private readonly Button _btnTerminar = new();
    private readonly Button _btnVerNorma = new();
    private readonly Button _btnCancelar = new();

    private List<MopNormaCamion> _normaCamiones = [];
    private Dictionary<short, MopNormaTandem> _normaTandems = [];
    private List<MopNormaEje> _normaEjes = [];
    private List<MopNormaLargo> _normaLargos = [];

    private readonly List<int> _pesosCapturados = [];
    private int _expectedAxles;
    private bool _activo;
    private bool _modoDinamico;
    private int _currentWeightKg;

    public PesajeMopForm(AppServices services, string? patenteInicial = null, short tipoMopInicial = 510, short largoInicial = 18, int? folioPesajeAsociado = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        FolioPesajeAsociado = folioPesajeAsociado ?? 0;

        Text = "Pesaje por Ejes - Normativa M.O.P.";
        ClientSize = new Size(980, 620);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.BackgroundLight;

        BuildUi();
        Load += async (_, _) => await InitDataAsync(patenteInicial, tipoMopInicial, largoInicial);
        FormClosing += (_, _) =>
        {
            _services.Scale.ReadingReceived -= OnReadingReceived;
            _services.Scale.StopDemo();
        };
    }

    public int FolioPesajeAsociado { get; }
    public PesajeMop? ResultadoGuardado { get; private set; }

    private void BuildUi()
    {
        var topPanel = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = AppTheme.NavyDark, Padding = new Padding(14, 10, 14, 10) };
        _lcd.Location = new Point(16, 11);
        _lcd.Size = new Size(340, 82);

        _semaforo.Location = new Point(372, 11);
        _semaforo.Size = new Size(270, 82);
        _semaforo.BackColor = AppTheme.AccentEmerald;
        _lblEstado.Dock = DockStyle.Fill;
        _lblEstado.TextAlign = ContentAlignment.MiddleCenter;
        _lblEstado.ForeColor = Color.White;
        _lblEstado.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
        _lblEstado.Text = "LISTO PARA INICIAR";
        _semaforo.Controls.Add(_lblEstado);

        var statsPanel = new Panel { Location = new Point(656, 11), Size = new Size(308, 82), BackColor = Color.FromArgb(24, 48, 82) };
        _lblBruto.SetBounds(12, 8, 285, 22); _lblBruto.ForeColor = Color.White; _lblBruto.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        _lblMaximo.SetBounds(12, 31, 285, 22); _lblMaximo.ForeColor = Color.LightSteelBlue; _lblMaximo.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _lblExceso.SetBounds(12, 54, 285, 22); _lblExceso.ForeColor = Color.Gold; _lblExceso.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _lblBruto.Text = "BRUTO: 0 kg"; _lblMaximo.Text = "MÁXIMO: 0 kg"; _lblExceso.Text = "SOBREPESO: 0 kg";
        statsPanel.Controls.AddRange([_lblBruto, _lblMaximo, _lblExceso]);
        topPanel.Controls.AddRange([_lcd, _semaforo, statsPanel]);

        var metaPanel = new Panel { Dock = DockStyle.Top, Height = 108, BackColor = AppTheme.SurfaceCard, Padding = new Padding(14, 8, 14, 8) };
        metaPanel.Controls.Add(new Label { Text = "Folio MOP:", Location = new Point(16, 14), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        _txtFolio.SetBounds(95, 11, 80, 24);
        metaPanel.Controls.Add(new Label { Text = "Patente:", Location = new Point(190, 14), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        _txtPatente.SetBounds(252, 11, 95, 24);
        metaPanel.Controls.Add(new Label { Text = "Acoplado:", Location = new Point(360, 14), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        _txtAcoplado.SetBounds(432, 11, 90, 24);
        metaPanel.Controls.Add(new Label { Text = "Tipo MOP:", Location = new Point(536, 14), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        _cmbTipoMop.SetBounds(612, 11, 140, 24);
        metaPanel.Controls.Add(new Label { Text = "Largo (m):", Location = new Point(766, 14), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        _numLargo.SetBounds(838, 11, 64, 24);

        metaPanel.Controls.Add(new Label { Text = "Transportista:", Location = new Point(16, 48), AutoSize = true });
        _txtTransp.SetBounds(108, 45, 160, 24);
        metaPanel.Controls.Add(new Label { Text = "Chofer:", Location = new Point(282, 48), AutoSize = true });
        _txtChofer.SetBounds(336, 45, 186, 24);
        metaPanel.Controls.Add(new Label { Text = "Guía:", Location = new Point(536, 48), AutoSize = true });
        _txtGuia.SetBounds(578, 45, 100, 24);
        metaPanel.Controls.Add(new Label { Text = "Carga:", Location = new Point(692, 48), AutoSize = true });
        _cmbTipoCarga.SetBounds(742, 45, 218, 24);

        metaPanel.Controls.AddRange([_txtFolio, _txtPatente, _txtAcoplado, _cmbTipoMop, _numLargo, _txtTransp, _txtChofer, _txtGuia, _cmbTipoCarga]);

        var actionsBar = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = AppTheme.BackgroundLight };
        _btnModo.SetBounds(16, 8, 128, 34);
        AppTheme.StyleSecondaryButton(_btnModo, AppIcon.Settings, "Modo: Estático", "Alternar entre captura estática o dinámica");
        _btnModo.Click += (_, _) =>
        {
            _modoDinamico = !_modoDinamico;
            _btnModo.Text = _modoDinamico ? "Modo: Dinámico" : "Modo: Estático";
            _btnRegEje.Enabled = _activo && !_modoDinamico;
        };

        _btnIniciar.SetBounds(152, 8, 128, 34);
        AppTheme.StylePrimaryButton(_btnIniciar, AppIcon.AxleMop, "Iniciar Pesaje", "Comenzar captura por ejes");
        _btnIniciar.Click += (_, _) => StartWeighing();

        _btnRegEje.SetBounds(288, 8, 136, 34);
        AppTheme.StylePrimaryButton(_btnRegEje, AppIcon.Add, "Registrar Eje", "Capturar peso del eje actual");
        _btnRegEje.Enabled = false;
        _btnRegEje.Click += (_, _) => RegisterStaticAxle();

        _btnDemo.SetBounds(432, 8, 134, 34);
        AppTheme.StyleSecondaryButton(_btnDemo, AppIcon.Refresh, "Reproducir Demo", "Simular pasada con curva DEMO1 de la base de datos", AppTheme.AccentAmber);
        _btnDemo.Click += async (_, _) => await RunDemoAsync();

        _btnTerminar.SetBounds(574, 8, 160, 34);
        AppTheme.StylePrimaryButton(_btnTerminar, AppIcon.Save, "Terminar e Imprimir", "Guardar pesaje MOP e imprimir boleto");
        _btnTerminar.Enabled = false;
        _btnTerminar.Click += async (_, _) => await FinishAndSaveAsync();

        _btnVerNorma.SetBounds(742, 8, 118, 34);
        AppTheme.StyleSecondaryButton(_btnVerNorma, AppIcon.List, "Tipos MOP", "Ver normativa oficial Decreto 158/80");
        _btnVerNorma.Click += (_, _) => ShowNormativaModal();

        _btnCancelar.SetBounds(868, 8, 96, 34);
        AppTheme.StyleDangerButton(_btnCancelar, AppIcon.Cancel, "Cerrar", "Cancelar / Cerrar");
        _btnCancelar.Click += (_, _) => Close();

        actionsBar.Controls.AddRange([_btnModo, _btnIniciar, _btnRegEje, _btnDemo, _btnTerminar, _btnVerNorma, _btnCancelar]);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 215, BackColor = AppTheme.BorderSubtle };
        AppTheme.StyleDataGrid(_gridEjes);
        _gridEjes.Dock = DockStyle.Fill;
        _gridEjes.Columns.AddRange([
            new DataGridViewTextBoxColumn { HeaderText = "Eje N°", FillWeight = 55 },
            new DataGridViewTextBoxColumn { HeaderText = "Tipo Eje", FillWeight = 70 },
            new DataGridViewTextBoxColumn { HeaderText = "Peso (kg)", FillWeight = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "Límite (kg)", FillWeight = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "Tol. (kg)", FillWeight = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "Exceso (kg)", FillWeight = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "Estado", FillWeight = 120 }
        ]);
        split.Panel1.Controls.Add(_gridEjes);

        AppTheme.StyleDataGrid(_gridTandems);
        _gridTandems.Dock = DockStyle.Fill;
        _gridTandems.Columns.AddRange([
            new DataGridViewTextBoxColumn { HeaderText = "Conjunto N°", FillWeight = 75 },
            new DataGridViewTextBoxColumn { HeaderText = "Configuración", FillWeight = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "Peso Conjunto (kg)", FillWeight = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "Límite Conjunto", FillWeight = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "Tol.", FillWeight = 75 },
            new DataGridViewTextBoxColumn { HeaderText = "Exceso (kg)", FillWeight = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "Estado", FillWeight = 120 }
        ]);
        split.Panel2.Controls.Add(_gridTandems);

        Controls.AddRange([split, actionsBar, metaPanel, topPanel]);
        _services.Scale.ReadingReceived += OnReadingReceived;
    }

    private async Task InitDataAsync(string? patente, short tipoMop, short largo)
    {
        (_normaCamiones, _normaTandems, _normaEjes, _normaLargos) = await _services.Mop.GetNormativaCompletaAsync();
        List<TipoCarga> cargas = await _services.Catalogs.GetTiposCargaAsync();
        BasculaInfo basc = await _services.Catalogs.GetBasculaAsync(1);

        _txtFolio.Text = basc.FolioPesajeMop.ToString(CultureInfo.InvariantCulture);
        _cmbTipoMop.DataSource = _normaCamiones;
        _cmbTipoMop.DisplayMember = nameof(MopNormaCamion.Tipo);
        MopNormaCamion? match = _normaCamiones.FirstOrDefault(c => c.Tipo == tipoMop) ?? _normaCamiones.FirstOrDefault();
        if (match is not null) _cmbTipoMop.SelectedItem = match;

        _cmbTipoCarga.DataSource = cargas;
        _cmbTipoCarga.DisplayMember = nameof(TipoCarga.Descripcion);
        TipoCarga? normCarga = cargas.FirstOrDefault(c => c.Normal == 1) ?? cargas.FirstOrDefault();
        if (normCarga is not null) _cmbTipoCarga.SelectedItem = normCarga;

        _txtPatente.Text = ChileanValidators.NormalizarPatente(patente);
        _numLargo.Value = Math.Clamp((int)largo, 4, 90);
        if (!string.IsNullOrWhiteSpace(_txtPatente.Text))
        {
            Camion? cam = await _services.Catalogs.GetCamionAsync(_txtPatente.Text);
            if (cam is not null)
            {
                _txtAcoplado.Text = cam.Acoplado;
                _txtTransp.Text = cam.Transportista;
                _txtChofer.Text = cam.Chofer;
                if (cam.Largo >= 4) _numLargo.Value = Math.Clamp((int)cam.Largo, 4, 90);
            }
        }
    }

    private void OnReadingReceived(object? sender, ScaleReadingEventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            _currentWeightKg = e.WeightKg;
            _lcd.UpdateReading(e.WeightKg, e.Stable, e.IsZero, _modoDinamico, e.CommError, e.DataError, _services.Scale.IsSimulationActive);

            if (_activo && _modoDinamico)
            {
                int? ejeKg = _dynamicProcessor.FeedSample(e.WeightKg, _services.ConfigActual);
                if (ejeKg.HasValue && _pesosCapturados.Count < _expectedAxles)
                {
                    _pesosCapturados.Add(ejeKg.Value);
                    EvaluateAndRenderProgress();
                }
            }
        });
    }

    private void StartWeighing()
    {
        if (_cmbTipoMop.SelectedItem is not MopNormaCamion norma)
        {
            MessageBox.Show(this, "Seleccione un Tipo MOP válido.", "Pesaje MOP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _pesosCapturados.Clear();
        _dynamicProcessor.Reset();
        _expectedAxles = norma.Tandems.Sum(t => _normaEjes.Count(e => e.Tandem == t));
        _activo = true;
        _btnIniciar.Enabled = false;
        _btnRegEje.Enabled = !_modoDinamico;
        _btnTerminar.Enabled = true;
        EvaluateAndRenderProgress();
    }

    private void RegisterStaticAxle()
    {
        if (!_activo) return;
        int peso = _currentWeightKg > 0 ? _currentWeightKg : PromptSimulatedAxleWeight();
        if (peso <= 0) return;

        if (_pesosCapturados.Count < _expectedAxles)
        {
            _pesosCapturados.Add(peso);
            EvaluateAndRenderProgress();
        }
    }

    private int PromptSimulatedAxleWeight()
    {
        using var dlg = new ManualWeightDialog(6500);
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.WeightKg : 0;
    }

    private async Task RunDemoAsync()
    {
        List<DemoMuestra> samples = await _services.Mop.GetDemoSamplesAsync();
        if (samples.Count == 0)
        {
            MessageBox.Show(this, "No hay datos en la tabla DEMO1 para reproducir.", "Demo Dinámico", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _modoDinamico = true;
        _btnModo.Text = "Modo: Dinámico";
        MopNormaCamion? t510 = _normaCamiones.FirstOrDefault(c => c.Tipo == 510);
        if (t510 is not null) _cmbTipoMop.SelectedItem = t510;
        if (string.IsNullOrWhiteSpace(_txtPatente.Text)) _txtPatente.Text = "DEMO510";
        StartWeighing();
        await _services.Scale.PlayDemoCurveAsync(samples, intervalMs: 12);
    }

    private void EvaluateAndRenderProgress()
    {
        if (_cmbTipoMop.SelectedItem is not MopNormaCamion norma) return;
        MopEvaluacionResultado res = WeighingCalculator.EvaluarPesajeMop(
            norma,
            (short)_numLargo.Value,
            _normaTandems,
            _normaEjes,
            _normaLargos,
            _pesosCapturados);

        _gridEjes.Rows.Clear();
        foreach (MopEjeResultado ej in res.Ejes)
        {
            int idx = _gridEjes.Rows.Add(ej.EjeNumero, ej.NombreEje, $"{ej.PesoKg:N0}", $"{ej.LimiteKg:N0}", $"{ej.ToleranciaKg:N0}", $"{ej.ExcesoKg:N0}", ej.FueraDeNorma ? "FUERA DE NORMA" : "DENTRO DE NORMA");
            _gridEjes.Rows[idx].DefaultCellStyle.ForeColor = ej.FueraDeNorma ? AppTheme.AccentDanger : AppTheme.TextPrimary;
        }

        _gridTandems.Rows.Clear();
        foreach (MopTandemResultado td in res.Tandems)
        {
            int idx = _gridTandems.Rows.Add(td.TandemNumero, td.NombreTandem, $"{td.PesoTotalKg:N0}", $"{td.LimiteKg:N0}", $"{td.ToleranciaKg:N0}", $"{td.ExcesoKg:N0}", td.FueraDeNorma ? "FUERA DE NORMA" : "DENTRO DE NORMA");
            _gridTandems.Rows[idx].DefaultCellStyle.ForeColor = td.FueraDeNorma ? AppTheme.AccentDanger : AppTheme.TextPrimary;
        }

        _lblBruto.Text = $"BRUTO ({_pesosCapturados.Count}/{_expectedAxles} ejes): {res.PesoBrutoTotalKg:N0} kg";
        _lblMaximo.Text = $"MÁXIMO LEGAL: {res.LimiteMaximoCamionKg:N0} kg";
        _lblExceso.Text = $"SOBREPESO: {res.ExcesoBrutoTotalKg:N0} kg";

        bool fuera = res.EstadoGeneral == EstadoNormaMop.FueraDeNorma;
        _semaforo.BackColor = fuera ? AppTheme.AccentDanger : AppTheme.AccentEmerald;
        _lblEstado.Text = _pesosCapturados.Count < _expectedAxles
            ? $"INGRESE EJE {_pesosCapturados.Count + 1} DE {_expectedAxles}"
            : (fuera ? "FUERA DE NORMA" : "DENTRO DE NORMA");
    }

    private async Task FinishAndSaveAsync()
    {
        if (_cmbTipoMop.SelectedItem is not MopNormaCamion norma) return;
        string pat = ChileanValidators.NormalizarPatente(_txtPatente.Text);
        if (string.IsNullOrWhiteSpace(pat))
        {
            MessageBox.Show(this, "Debe ingresar la patente del camión antes de guardar.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MopEvaluacionResultado res = WeighingCalculator.EvaluarPesajeMop(norma, (short)_numLargo.Value, _normaTandems, _normaEjes, _normaLargos, _pesosCapturados);
        DateTime now = DateTime.Now;

        var entity = new PesajeMop
        {
            Bascula = 1,
            FolioPesaje = FolioPesajeAsociado,
            Patente = pat,
            Acoplado = ChileanValidators.NormalizarPatente(_txtAcoplado.Text),
            Fecha = now.Date,
            Hora = now.ToString("HH:mm", CultureInfo.InvariantCulture),
            TipoCarga = (_cmbTipoCarga.SelectedItem as TipoCarga)?.Codigo ?? 7,
            Transportista = _txtTransp.Text.Trim(),
            TipoMop = norma.Tipo,
            Largo = (short)_numLargo.Value,
            Bruto = res.PesoBrutoTotalKg,
            Maximo = res.LimiteMaximoCamionKg,
            Sobrepeso = res.ExcesoBrutoTotalKg,
            GuiaDespacho = _txtGuia.Text.Trim(),
            Chofer = _txtChofer.Text.Trim(),
            Estado = res.EstadoGeneral,
            UsuarioId = _services.UsuarioActual?.Id ?? 0
        };

        foreach (MopEjeResultado ej in res.Ejes)
        {
            MopTandemResultado? td = res.Tandems.FirstOrDefault(t => t.TandemNumero == ej.TandemNumero);
            entity.Detalles.Add(new PesajeMopDetalle
            {
                Bascula = 1,
                EjeNumero = ej.EjeNumero,
                EjeTipo = ej.TipoEje,
                EjePeso = ej.PesoKg,
                EjeLimite = ej.LimiteKg,
                TandemNumero = ej.TandemNumero,
                TandemTipo = td?.TandemTipo ?? 0,
                TandemPeso = td?.PesoTotalKg ?? 0,
                TandemLimite = td?.LimiteKg ?? 0
            });
        }

        await _services.Mop.SavePesajeMopAsync(entity);
        ResultadoGuardado = entity;
        _activo = false;
        _services.Scale.StopDemo();

        if (MessageBox.Show(this, $"Pesaje MOP #{entity.FolioMop} guardado ({entity.Estado}).\r\n¿Desea previsualizar el Boleto Oficial?", "Boleto MOP", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            using PrintDocument doc = TicketPrinterService.CreateMopBoletoDocument(entity, _services.BasculaActual, entity.Transportista, entity.Chofer);
            using var prev = new PrintPreviewDialog { Document = doc, Width = 780, Height = 580 };
            prev.ShowDialog(this);
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void ShowNormativaModal()
    {
        using var dlg = new CatalogLookupDialog<MopNormaCamion>(
            "Normativa Oficial MOP - Configuraciones de Vehículos",
            _normaCamiones,
            c => $"{c.Tipo} {c.Tandem01} {c.Tandem02}",
            ("Tipo MOP", c => c.Tipo, 75),
            ("Conjuntos", c => string.Join(" + ", c.Tandems.Select(id => _normaTandems.TryGetValue(id, out MopNormaTandem? t) ? t.Nombre : id.ToString(CultureInfo.InvariantCulture))), 240),
            ("Máximo Especial (kg)", c => c.Maximo.HasValue ? $"{c.Maximo:N0}" : "Según Largo (39-45 t)", 140));
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null)
        {
            _cmbTipoMop.SelectedItem = dlg.SelectedItem;
        }
    }
}
