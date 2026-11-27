using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Controls;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;
using PesajeProCamion.Hardware.Printing;
using PesajeProCamion.Hardware.Scale;

namespace PesajeProCamion.App.UI.Dialogs;

/// <summary>Configuración del Visor Serial RS-232 (ortografía corregida + tester en vivo + modo simulación).</summary>
public sealed class VisorSetupForm : Form
{
    private readonly AppServices _services;
    private readonly DigitalWeightDisplay _previewLcd = new();
    private readonly ComboBox _cmbCom = new();
    private readonly ComboBox _cmbBaud = new();
    private readonly ComboBox _cmbDataBits = new();
    private readonly ComboBox _cmbParity = new();
    private readonly NumericUpDown _numStx = new();
    private readonly NumericUpDown _numLen = new();
    private readonly NumericUpDown _numPos = new();
    private readonly NumericUpDown _numLimit = new();
    private readonly NumericUpDown _numDelta = new();
    private readonly TextBox _txtRaw = new();
    private readonly TextBox _txtParsed = new();

    public VisorSetupForm(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        Text = "Configuración de Visor de Peso RS-232";
        AppTheme.ConfigureResponsiveDialog(this, 710, 520, 660, 480, resizable: true);
        BuildUi();
        Load += async (_, _) => await AppTheme.SafeLoadAsync(this, "Visor RS-232", LoadCurrentAsync);
        FormClosing += (_, _) => _services.Scale.ReadingReceived -= OnReading;
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 44 };
        header.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, header.ClientRectangle);
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 0, 14, 0),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = AppTheme.FontBodyBold,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Visor de Pesaje RS-232 — Lectura Directa, Trama ASCII y Simulación"
        });

        var footerBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 8, 16, 8),
            BackColor = AppTheme.SurfaceCard
        };
        var btnClose = new Button { Size = new Size(124, 36) };
        AppTheme.StyleSecondaryButton(btnClose, AppIcon.Cancel, "Cerrar", "Cerrar ventana");
        btnClose.Click += (_, _) => Close();
        var btnSave = new Button { Size = new Size(184, 36) };
        AppTheme.StylePrimaryButton(btnSave, AppIcon.Save, "Guardar y Aplicar", "Guardar y aplicar configuración de visor");
        btnSave.Click += async (_, _) => await SaveAsync();
        footerBar.Controls.AddRange([btnClose, btnSave]);

        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        _previewLcd.Location = new Point(18, 12);
        _previewLcd.Size = new Size(344, 82);
        body.Controls.Add(_previewLcd);

        _txtParsed.Location = new Point(376, 12);
        _txtParsed.Width = 312;
        _txtParsed.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _txtParsed.BackColor = AppTheme.LcdBackground;
        _txtParsed.ForeColor = AppTheme.LcdDigitOn;
        _txtParsed.Font = new Font("Consolas", 11f, FontStyle.Bold);
        _txtParsed.ReadOnly = true;

        _txtRaw.Location = new Point(376, 44);
        _txtRaw.Size = new Size(312, 182);
        _txtRaw.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _txtRaw.Multiline = true;
        _txtRaw.ScrollBars = ScrollBars.Vertical;
        _txtRaw.BackColor = AppTheme.LcdBackground;
        _txtRaw.ForeColor = Color.LightGreen;
        _txtRaw.Font = new Font("Consolas", 8.75f);
        _txtRaw.ReadOnly = true;
        body.Controls.AddRange([_txtParsed, _txtRaw]);

        var grpSerial = new GroupBox { Text = "Configuración Puerto Serial", Location = new Point(18, 106), Size = new Size(344, 294), Font = AppTheme.FontBodyBold, ForeColor = AppTheme.NavyDark };
        _cmbCom.DropDownStyle = ComboBoxStyle.DropDownList;
        var ports = new List<string>(Enumerable.Range(1, 16).Select(i => $"COM{i}"));
        foreach (string detected in ScaleReaderService.GetAvailablePorts())
        {
            if (!ports.Contains(detected, StringComparer.OrdinalIgnoreCase))
            {
                ports.Add(detected);
            }
        }
        _cmbCom.Items.AddRange([.. ports]);
        _cmbBaud.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbBaud.Items.AddRange(["9600", "4800", "2400", "1200", "19200", "38400", "115200"]);
        _cmbDataBits.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDataBits.Items.AddRange(["7", "8"]);
        _cmbParity.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbParity.Items.AddRange(["None", "Even", "Odd", "Space"]);

        AddPair(grpSerial, "Puerto Serial", _cmbCom, 28);
        AddPair(grpSerial, "Velocidad", _cmbBaud, 60);
        AddPair(grpSerial, "Largo Bits", _cmbDataBits, 92);
        AddPair(grpSerial, "Paridad", _cmbParity, 124);
        _cmbCom.SelectedIndex = 0; _cmbBaud.SelectedIndex = 0; _cmbDataBits.SelectedIndex = 1; _cmbParity.SelectedIndex = 0;

        var btnFlow = new FlowLayoutPanel { Location = new Point(10, 158), Size = new Size(324, 124) };
        var btnOpen = new Button { Size = new Size(102, 34) };
        AppTheme.StylePrimaryButton(btnOpen, AppIcon.SerialPort, "Abrir", "Abrir puerto serial físico del visor");
        btnOpen.Click += async (_, _) => await TestOpenSerialAsync();

        var btnCloseCom = new Button { Size = new Size(102, 34) };
        AppTheme.StyleSecondaryButton(btnCloseCom, AppIcon.Cancel, "Cerrar COM", "Cerrar puerto serial activo", AppTheme.AccentDanger);
        btnCloseCom.Click += (_, _) =>
        {
            _services.Scale.Close();
            _txtRaw.AppendText("[COM] Puerto serial cerrado." + Environment.NewLine);
        };

        var btnSim = new Button { Size = new Size(106, 34) };
        AppTheme.StyleSecondaryButton(btnSim, AppIcon.Refresh, "Simular", "Emitir lectura simulada al visor", AppTheme.AccentEmerald);
        btnSim.Click += (_, _) => _services.Scale.EmitSimulatedWeight(18450, true);

        var btnTestFrame = new Button { Size = new Size(166, 34) };
        AppTheme.StyleSecondaryButton(btnTestFrame, AppIcon.Scale, "Probar Trama STX", "Inyectar trama ASCII de prueba según configuración STX/Pos/Len", AppTheme.NavyPrimary);
        btnTestFrame.Click += (_, _) =>
        {
            _services.Scale.UpdateConfig(BuildTempConfig());
            string sample = "\x02+ 18450 kg\r\n";
            _services.Scale.FeedRawAsciiTestFrame(sample);
        };

        var btnDefaults = new Button { Size = new Size(134, 34) };
        AppTheme.StyleSecondaryButton(btnDefaults, AppIcon.Settings, "Por Defecto", "Restaurar valores por defecto");
        btnDefaults.Click += (_, _) =>
        {
            _cmbCom.SelectedIndex = 0; _cmbBaud.SelectedIndex = 0; _cmbDataBits.SelectedIndex = 1; _cmbParity.SelectedIndex = 0;
            _numStx.Value = 2; _numLen.Value = 5; _numPos.Value = 3; _numLimit.Value = 1; _numDelta.Value = 10;
        };
        btnFlow.Controls.AddRange([btnOpen, btnCloseCom, btnSim, btnTestFrame, btnDefaults]);
        grpSerial.Controls.Add(btnFlow);
        body.Controls.Add(grpSerial);

        var grpDatos = new GroupBox { Text = "Trama de Datos (0 = línea CR/LF sin STX)", Location = new Point(376, 234), Size = new Size(312, 166), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Font = AppTheme.FontBodyBold, ForeColor = AppTheme.NavyDark };
        AddPairNum(grpDatos, "STX (ASCII)", _numStx, 0, 255, 28);
        AddPairNum(grpDatos, "Posición Dato", _numPos, 1, 50, 58);
        AddPairNum(grpDatos, "Largo Dato", _numLen, 1, 15, 88);
        AddPairNum(grpDatos, "Muestras Estab.", _numLimit, 1, 50, 118);
        _numStx.Value = 2; _numPos.Value = 3; _numLen.Value = 5; _numLimit.Value = 1;
        body.Controls.Add(grpDatos);

        Controls.AddRange([body, footerBar, header]);
        _previewLcd.UpdateReading(_services.Scale.CurrentWeightKg, true, _services.Scale.CurrentWeightKg == 0, false, false, false, _services.Scale.IsSimulationActive);
        _txtParsed.Text = $"Peso actual: {_services.Scale.CurrentWeightKg:N0} kg";
        _services.Scale.ReadingReceived += OnReading;
    }

    private ConfiguracionSistema BuildTempConfig()
    {
        ConfiguracionSistema cfg = _services.ConfigActual;
        cfg.VisorStx = (int)_numStx.Value;
        cfg.VisorPosData = (int)_numPos.Value;
        cfg.VisorLenData = (int)_numLen.Value;
        cfg.VLimit = (int)_numLimit.Value;
        cfg.VDelta = (int)_numDelta.Value;
        return cfg;
    }

    private static void AddPair(GroupBox grp, string label, ComboBox cmb, int y)
    {
        grp.Controls.Add(new Label { Text = label, Location = new Point(14, y + 3), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Regular) });
        cmb.Location = new Point(135, y);
        cmb.Width = 185;
        grp.Controls.Add(cmb);
    }

    private static void AddPairNum(GroupBox grp, string label, NumericUpDown num, int min, int max, int y)
    {
        grp.Controls.Add(new Label { Text = label, Location = new Point(12, y + 3), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Regular) });
        num.Location = new Point(145, y);
        num.Width = 96;
        num.Minimum = min;
        num.Maximum = max;
        grp.Controls.Add(num);
    }

    private void OnReading(object? sender, ScaleReadingEventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            _previewLcd.UpdateReading(e.WeightKg, e.Stable, e.IsZero, e.IsDynamic, e.CommError, e.DataError, _services.Scale.IsSimulationActive);
            _txtParsed.Text = $"Peso: {e.WeightKg:N0} kg ({(e.Stable ? "ESTABLE" : "MOV")})";
            if (!string.IsNullOrEmpty(e.RawData))
            {
                _txtRaw.AppendText(e.RawData + Environment.NewLine);
            }
        });
    }

    private async Task LoadCurrentAsync()
    {
        HardwareSetup hw = await _services.Catalogs.GetHardwareSetupAsync("BASCULA");
        ConfiguracionSistema cfg = await _services.Catalogs.GetConfigAsync();
        _cmbCom.SelectedIndex = Math.Clamp(hw.ComNumber - 1, 0, _cmbCom.Items.Count - 1);
        _cmbBaud.SelectedIndex = Math.Clamp(hw.BaudRateIndex, 0, _cmbBaud.Items.Count - 1);
        _cmbDataBits.SelectedIndex = Math.Clamp(hw.DataBitsIndex, 0, _cmbDataBits.Items.Count - 1);
        _cmbParity.SelectedIndex = Math.Clamp(hw.ParityIndex, 0, _cmbParity.Items.Count - 1);
        _numStx.Value = Math.Clamp(cfg.VisorStx, 0, 255);
        _numPos.Value = Math.Clamp(cfg.VisorPosData, 1, 50);
        _numLen.Value = Math.Clamp(cfg.VisorLenData, 1, 15);
        _numLimit.Value = Math.Clamp(cfg.VLimit, 1, 50);
        _numDelta.Value = Math.Clamp(cfg.VDelta, 0, 1000);
    }

    private Task TestOpenSerialAsync()
    {
        var hw = new HardwareSetup
        {
            Device = "BASCULA",
            ComNumber = (short)(_cmbCom.SelectedIndex + 1),
            BaudRateIndex = _cmbBaud.SelectedIndex,
            DataBitsIndex = _cmbDataBits.SelectedIndex,
            ParityIndex = _cmbParity.SelectedIndex
        };
        if (!_services.Scale.TryOpenSerial(hw, _services.ConfigActual, out string? err))
        {
            MessageBox.Show(this, $"No se pudo abrir COM{hw.ComNumber}: {err}\r\nPuede operar en Modo Simulación.", "Puerto Serial", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ConfigurarVisorBascula))
        {
            MessageBox.Show(this, "No está autorizado para modificar la configuración del visor serial.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var hw = new HardwareSetup
        {
            Device = "BASCULA",
            ComNumber = (short)(_cmbCom.SelectedIndex + 1),
            BaudRateIndex = _cmbBaud.SelectedIndex,
            DataBitsIndex = _cmbDataBits.SelectedIndex,
            ParityIndex = _cmbParity.SelectedIndex
        };
        await _services.Catalogs.SaveHardwareSetupAsync(hw);

        ConfiguracionSistema cfg = await _services.Catalogs.GetConfigAsync();
        cfg.VisorStx = (int)_numStx.Value;
        cfg.VisorPosData = (int)_numPos.Value;
        cfg.VisorLenData = (int)_numLen.Value;
        cfg.VLimit = (int)_numLimit.Value;
        cfg.VDelta = (int)_numDelta.Value;
        await _services.Catalogs.SaveConfigAsync(cfg);
        await _services.RefreshMetadataAsync();
        MessageBox.Show(this, "Configuración de visor guardada correctamente.", "Visor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Close();
    }
}

/// <summary>Configuración de filtros, límites y calibración por eje del pesaje dinámico MOP.</summary>
public sealed class DinamicoSetupForm : Form
{
    private readonly AppServices _services;
    private readonly NumericUpDown _numCutLeft = new();
    private readonly NumericUpDown _numCutRight = new();
    private readonly NumericUpDown _numFiltros = new();
    private readonly NumericUpDown _numUp = new();
    private readonly NumericUpDown _numDown = new();
    private readonly NumericUpDown _numUnder = new();
    private readonly NumericUpDown _numOver = new();
    private readonly NumericUpDown[] _offsets = new NumericUpDown[7];
    private readonly NumericUpDown[] _calibs = new NumericUpDown[7];

    public DinamicoSetupForm(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        Text = "Configuración Pesaje Dinámico MOP";
        AppTheme.ConfigureResponsiveDialog(this, 680, 468, 640, 440, resizable: true);
        BuildUi();
        Load += async (_, _) => await AppTheme.SafeLoadAsync(this, "Pesaje Dinámico MOP", LoadAsync);
    }

    private void BuildUi()
    {
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = AppTheme.SurfaceCard,
            Padding = new Padding(12, 8, 16, 8)
        };
        var btnClose = new Button { Size = new Size(122, 36) };
        AppTheme.StyleSecondaryButton(btnClose, AppIcon.Cancel, "Cerrar", "Cerrar");
        btnClose.Click += (_, _) => Close();

        var btnSave = new Button { Size = new Size(134, 36) };
        AppTheme.StylePrimaryButton(btnSave, AppIcon.Save, "Guardar", "Guardar parámetros dinámicos");
        btnSave.Click += async (_, _) => await SaveAsync();

        var btnReset = new Button { Size = new Size(164, 36) };
        AppTheme.StyleSecondaryButton(btnReset, AppIcon.Refresh, "Limpiar Offsets", "Reiniciar calibraciones y offsets a 0");
        btnReset.Click += (_, _) =>
        {
            for (int i = 0; i < 7; i++) { _calibs[i].Value = 0; _offsets[i].Value = 0; }
        };
        footer.Controls.AddRange([btnClose, btnSave, btnReset]);

        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(14) };

        var g1 = new GroupBox { Text = "Cortes (%)", Location = new Point(16, 12), Size = new Size(202, 118), Font = AppTheme.FontBodyBold };
        AddN(g1, "Inicial (%)", _numCutLeft, 0, 45, 28);
        AddN(g1, "Final (%)", _numCutRight, 0, 45, 64);

        var g2 = new GroupBox { Text = "Filtros", Location = new Point(232, 12), Size = new Size(212, 118), Font = AppTheme.FontBodyBold };
        AddN(g2, "Nro Filtros", _numFiltros, 1, 500, 24);
        AddN(g2, "Hi Factor", _numUp, 0, 10, 52, decimals: 2);
        AddN(g2, "Low Factor", _numDown, 0, 10, 80, decimals: 2);

        var g3 = new GroupBox { Text = "Límites Disparo (kg)", Location = new Point(458, 12), Size = new Size(202, 118), Font = AppTheme.FontBodyBold };
        AddN(g3, "Inicial (Under)", _numUnder, 10, 5000, 28);
        AddN(g3, "Final (Over)", _numOver, 10, 5000, 64);

        var g4 = new GroupBox { Text = "Calibración y Offsets por Eje (Ejes 1 a 7)", Location = new Point(16, 142), Size = new Size(644, 242), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Font = AppTheme.FontBodyBold };
        for (int i = 0; i < 7; i++)
        {
            int y = 26 + i * 29;
            g4.Controls.Add(new Label { Text = $"Eje {i + 1}", Location = new Point(18, y + 3), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            g4.Controls.Add(new Label { Text = "Calib:", Location = new Point(98, y + 3), AutoSize = true, Font = AppTheme.FontBody });
            _calibs[i] = new NumericUpDown { Location = new Point(150, y), Width = 130, Minimum = -10000, Maximum = 10000, Font = AppTheme.FontBody };
            g4.Controls.Add(new Label { Text = "Offset:", Location = new Point(320, y + 3), AutoSize = true, Font = AppTheme.FontBody });
            _offsets[i] = new NumericUpDown { Location = new Point(382, y), Width = 130, Minimum = -10000, Maximum = 10000, Font = AppTheme.FontBody };
            g4.Controls.AddRange([_calibs[i], _offsets[i]]);
        }

        body.Controls.AddRange([g1, g2, g3, g4]);
        Controls.AddRange([body, footer]);
    }

    private static void AddN(GroupBox grp, string lbl, NumericUpDown num, int min, int max, int y, int decimals = 0)
    {
        grp.Controls.Add(new Label { Text = lbl, Location = new Point(10, y + 3), AutoSize = true });
        num.Location = new Point(108, y);
        num.Width = 76;
        num.Minimum = min;
        num.Maximum = max;
        num.DecimalPlaces = decimals;
        if (decimals > 0) num.Increment = 0.1m;
        grp.Controls.Add(num);
    }

    private async Task LoadAsync()
    {
        ConfiguracionSistema c = await _services.Catalogs.GetConfigAsync();
        _numCutLeft.Value = Math.Clamp(c.VmopCutLeft, 0, 45);
        _numCutRight.Value = Math.Clamp(c.VmopCutRight, 0, 45);
        _numFiltros.Value = Math.Clamp(c.NroFiltros, 1, 500);
        _numUp.Value = (decimal)Math.Clamp(c.DstUpFactor, 0, 10);
        _numDown.Value = (decimal)Math.Clamp(c.DstDownFactor, 0, 10);
        _numUnder.Value = Math.Clamp(c.VmopUnder, 10, 5000);
        _numOver.Value = Math.Clamp(c.VmopOver, 10, 5000);
        for (int i = 0; i < 7; i++)
        {
            _calibs[i].Value = Math.Clamp(c.Calibraciones[i], -10000, 10000);
            _offsets[i].Value = Math.Clamp(c.Offsets[i], -10000, 10000);
        }
    }

    private async Task SaveAsync()
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ConfigurarPesajeDinamico))
        {
            MessageBox.Show(this, "No está autorizado para modificar el pesaje dinámico MOP.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        ConfiguracionSistema c = await _services.Catalogs.GetConfigAsync();
        c.VmopCutLeft = (int)_numCutLeft.Value;
        c.VmopCutRight = (int)_numCutRight.Value;
        c.NroFiltros = (int)_numFiltros.Value;
        c.DstUpFactor = (double)_numUp.Value;
        c.DstDownFactor = (double)_numDown.Value;
        c.VmopUnder = (int)_numUnder.Value;
        c.VmopOver = (int)_numOver.Value;
        for (int i = 0; i < 7; i++)
        {
            c.Calibraciones[i] = (int)_calibs[i].Value;
            c.Offsets[i] = (int)_offsets[i].Value;
        }
        await _services.Catalogs.SaveConfigAsync(c);
        await _services.RefreshMetadataAsync();
        MessageBox.Show(this, "Parámetros dinámicos guardados correctamente.", "Pesaje Dinámico", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Close();
    }
}

/// <summary>Parámetros generales de la instalación, folios, impresora y báscula.</summary>
public sealed class InstalacionForm : Form
{
    private readonly AppServices _services;
    private readonly TextBox _txtEmpresa = new();
    private readonly TextBox _txtRut = new();
    private readonly TextBox _txtDir = new();
    private readonly TextBox _txtDescripBasc = new();
    private readonly TextBox _txtNroInst = new();
    private readonly NumericUpDown _numFolio = new();
    private readonly NumericUpDown _numFolioMop = new();
    private readonly NumericUpDown _numCopias = new();
    private readonly NumericUpDown _numPorcTara = new();
    private readonly NumericUpDown _numCeroKg = new();
    private readonly CheckBox _chkCero = new();
    private readonly CheckBox _chkConfirmPrint = new();
    private readonly ComboBox _cmbPrinter = new();
    private readonly ComboBox _cmbTipoCarga = new();

    public InstalacionForm(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        Text = "Parámetros de Instalación";
        AppTheme.ConfigureResponsiveDialog(this, 680, 480, 640, 450, resizable: true);
        BuildUi();
        Load += async (_, _) => await AppTheme.SafeLoadAsync(this, "Pesaje Dinámico MOP", LoadAsync);
    }

    private void BuildUi()
    {
        AddT("Razón Social", _txtEmpresa, 18, 360);
        AddT("RUT Empresa", _txtRut, 52, 160);
        AddT("Dirección Planta", _txtDir, 86, 440);
        AddT("Nombre Báscula", _txtDescripBasc, 120, 260);
        AddT("Nro. Instalación", _txtNroInst, 154, 100);

        AddN("Folio Pesaje Sig.", _numFolio, 1, 99_999_999, 188, 160);
        AddN("Folio MOP Sig.", _numFolioMop, 1, 99_999_999, 188, 430);
        AddN("Copias Ticket", _numCopias, 1, 10, 224, 160);
        AddN("Tolerancia Tara %", _numPorcTara, 0, 100, 224, 430);
        AddN("Banda Cero (kg)", _numCeroKg, 0, 1000, 260, 160);

        _chkCero.Text = "Exigir báscula en cero entre pesajes";
        _chkCero.Location = new Point(300, 262);
        _chkCero.AutoSize = true;

        _chkConfirmPrint.Text = "Confirmar antes de imprimir ticket";
        _chkConfirmPrint.Location = new Point(160, 294);
        _chkConfirmPrint.AutoSize = true;

        Controls.Add(new Label { Text = "Impresora Tickets", Location = new Point(22, 326), AutoSize = true });
        _cmbPrinter.Location = new Point(160, 324);
        _cmbPrinter.Width = 440;
        _cmbPrinter.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (string p in PrinterSettings.InstalledPrinters)
        {
            _cmbPrinter.Items.Add(p);
        }

        Controls.Add(new Label { Text = "Tipo Carga Defecto", Location = new Point(22, 360), AutoSize = true });
        _cmbTipoCarga.Location = new Point(160, 358);
        _cmbTipoCarga.Width = 300;
        _cmbTipoCarga.DropDownStyle = ComboBoxStyle.DropDownList;

        var btnTestTicket = new Button { Location = new Point(22, 400), Size = new Size(192, 36) };
        AppTheme.StyleSecondaryButton(btnTestTicket, AppIcon.Print, "Imprimir Test Ticket", "Generar ticket de prueba");
        btnTestTicket.Click += (_, _) =>
        {
            var sample = new Pesaje
            {
                FolioPesaje = 999999,
                Patente = "TEST00",
                NombreCliente = "PRUEBA DE SISTEMA",
                NombreProducto = "TICKET DE VERIFICACION",
                Bruto = 25000,
                Tara = 10000,
                Neto = 15000,
                FechaIngreso = DateTime.Today,
                HoraIngreso = "08:00",
                FechaSalida = DateTime.Today,
                HoraSalida = DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            };
            using PrintDocument doc = TicketPrinterService.CreateCommercialTicketDocument(sample, _services.BasculaActual, _services.ConfigActual);
            using var prev = new PrintPreviewDialog { Document = doc, Width = 780, Height = 580 };
            prev.ShowDialog(this);
        };

        var btnSave = new Button { Location = new Point(365, 400), Size = new Size(128, 36) };
        AppTheme.StylePrimaryButton(btnSave, AppIcon.Save, "Guardar", "Guardar parámetros de instalación");
        btnSave.Click += async (_, _) => await SaveAsync();

        var btnClose = new Button { Location = new Point(502, 400), Size = new Size(122, 36) };
        AppTheme.StyleSecondaryButton(btnClose, AppIcon.Cancel, "Cerrar", "Cerrar");
        btnClose.Click += (_, _) => Close();

        Controls.AddRange([_chkCero, _chkConfirmPrint, _cmbPrinter, _cmbTipoCarga, btnTestTicket, btnSave, btnClose]);
    }

    private void AddT(string lbl, TextBox tb, int y, int width)
    {
        Controls.Add(new Label { Text = lbl, Location = new Point(22, y + 3), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        tb.Location = new Point(160, y);
        tb.Width = width;
        Controls.Add(tb);
    }

    private void AddN(string lbl, NumericUpDown n, int min, int max, int y, int x)
    {
        Controls.Add(new Label { Text = lbl, Location = new Point(x - 138, y + 3), AutoSize = true });
        n.Location = new Point(x, y);
        n.Width = 110;
        n.Minimum = min;
        n.Maximum = max;
        Controls.Add(n);
    }

    private async Task LoadAsync()
    {
        BasculaInfo b = await _services.Catalogs.GetBasculaAsync(1);
        ConfiguracionSistema c = await _services.Catalogs.GetConfigAsync();
        FoliosSistema f = await _services.Catalogs.GetFoliosAsync();
        List<TipoCarga> tc = await _services.Catalogs.GetTiposCargaAsync();

        _txtEmpresa.Text = b.NombreEmpresa;
        _txtRut.Text = b.Rut;
        _txtDir.Text = b.Direccion;
        _txtDescripBasc.Text = b.Descripcion;
        _txtNroInst.Text = b.NroInstalacion;

        _numFolio.Value = Math.Clamp(f.FolioPesaje, 1, 99_999_999);
        _numFolioMop.Value = Math.Clamp(b.FolioPesajeMop, 1, 99_999_999);
        _numCopias.Value = Math.Clamp(c.NroCopiasTicket, 1, 10);
        _numPorcTara.Value = Math.Clamp(c.PorcentajeTara, 0, 100);
        _numCeroKg.Value = Math.Clamp(c.CeroBasculaKg, 0, 1000);
        _chkCero.Checked = c.BasculaEnCeroActivo;
        _chkConfirmPrint.Checked = c.ConfirmarImpresionTicket;

        if (_cmbPrinter.Items.Count > 0)
        {
            _cmbPrinter.SelectedIndex = Math.Clamp(c.PrintIndexTicket, 0, _cmbPrinter.Items.Count - 1);
        }

        _cmbTipoCarga.DataSource = tc;
        _cmbTipoCarga.DisplayMember = nameof(TipoCarga.Descripcion);
        _cmbTipoCarga.ValueMember = nameof(TipoCarga.Codigo);
        TipoCarga? norm = tc.FirstOrDefault(x => x.Normal == 1);
        if (norm is not null) _cmbTipoCarga.SelectedItem = norm;
    }

    private async Task SaveAsync()
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ConfigurarInstalacion))
        {
            MessageBox.Show(this, "No está autorizado para modificar parámetros de instalación.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        BasculaInfo b = await _services.Catalogs.GetBasculaAsync(1);
        b.NombreEmpresa = _txtEmpresa.Text.Trim();
        b.Rut = _txtRut.Text.Trim();
        b.Direccion = _txtDir.Text.Trim();
        b.Descripcion = _txtDescripBasc.Text.Trim();
        b.NroInstalacion = _txtNroInst.Text.Trim();
        b.FolioPesajeMop = (int)_numFolioMop.Value;
        await _services.Catalogs.SaveBasculaAsync(b, _services.UsuarioActual?.Id ?? 0);

        ConfiguracionSistema c = await _services.Catalogs.GetConfigAsync();
        c.NroCopiasTicket = (int)_numCopias.Value;
        c.PorcentajeTara = (int)_numPorcTara.Value;
        c.CeroBasculaKg = (int)_numCeroKg.Value;
        c.BasculaEnCeroActivo = _chkCero.Checked;
        c.ConfirmarImpresionTicket = _chkConfirmPrint.Checked;
        if (_cmbPrinter.SelectedIndex >= 0) c.PrintIndexTicket = _cmbPrinter.SelectedIndex;
        await _services.Catalogs.SaveConfigAsync(c);

        FoliosSistema f = await _services.Catalogs.GetFoliosAsync();
        f.FolioPesaje = (int)_numFolio.Value;
        f.FolioPesajeMop = (int)_numFolioMop.Value;
        await _services.Catalogs.SaveFoliosAsync(f);

        if (_cmbTipoCarga.SelectedItem is TipoCarga tc)
        {
            await _services.Catalogs.SaveTipoCargaNormalAsync(tc.Codigo);
        }

        await _services.RefreshMetadataAsync();
        MessageBox.Show(this, "Parámetros guardados correctamente.", "Instalación", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Close();
    }
}

