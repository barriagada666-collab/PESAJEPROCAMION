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
        ClientSize = new Size(680, 500);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AppTheme.BackgroundLight;
        AppTheme.ApplyFormIcon(this);
        BuildUi();
        Load += async (_, _) => await LoadCurrentAsync();
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
        Controls.Add(header);

        _previewLcd.Location = new Point(18, 56);
        _previewLcd.Size = new Size(340, 82);
        Controls.Add(_previewLcd);

        _txtParsed.Location = new Point(372, 56);
        _txtParsed.Width = 288;
        _txtParsed.BackColor = AppTheme.LcdBackground;
        _txtParsed.ForeColor = AppTheme.LcdDigitOn;
        _txtParsed.Font = new Font("Consolas", 11f, FontStyle.Bold);
        _txtParsed.ReadOnly = true;

        _txtRaw.Location = new Point(372, 88);
        _txtRaw.Size = new Size(288, 178);
        _txtRaw.Multiline = true;
        _txtRaw.ScrollBars = ScrollBars.Vertical;
        _txtRaw.BackColor = AppTheme.LcdBackground;
        _txtRaw.ForeColor = Color.LightGreen;
        _txtRaw.Font = new Font("Consolas", 8.75f);
        _txtRaw.ReadOnly = true;
        Controls.AddRange([_txtParsed, _txtRaw]);

        var grpSerial = new GroupBox { Text = "Configuración Puerto Serial", Location = new Point(18, 150), Size = new Size(340, 232), Font = AppTheme.FontBodyBold, ForeColor = AppTheme.NavyDark };
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

        var btnOpen = new Button { Location = new Point(12, 160), Size = new Size(100, 30) };
        AppTheme.StylePrimaryButton(btnOpen, AppIcon.SerialPort, "Abrir", "Abrir puerto serial físico del visor");
        btnOpen.Click += async (_, _) => await TestOpenSerialAsync();

        var btnCloseCom = new Button { Location = new Point(118, 160), Size = new Size(100, 30) };
        AppTheme.StyleSecondaryButton(btnCloseCom, AppIcon.Cancel, "Cerrar", "Cerrar puerto serial activo", AppTheme.AccentDanger);
        btnCloseCom.Click += (_, _) =>
        {
            _services.Scale.Close();
            _txtRaw.AppendText("[COM] Puerto serial cerrado." + Environment.NewLine);
        };

        var btnSim = new Button { Location = new Point(224, 160), Size = new Size(104, 30) };
        AppTheme.StyleSecondaryButton(btnSim, AppIcon.Refresh, "Simular", "Emitir lectura simulada al visor", AppTheme.AccentEmerald);
        btnSim.Click += (_, _) => _services.Scale.EmitSimulatedWeight(18450, true);

        var btnTestFrame = new Button { Location = new Point(12, 196), Size = new Size(156, 28) };
        AppTheme.StyleSecondaryButton(btnTestFrame, AppIcon.Scale, "Probar Trama STX", "Inyectar trama ASCII de prueba según configuración STX/Pos/Len", AppTheme.NavyPrimary);
        btnTestFrame.Click += (_, _) =>
        {
            _services.Scale.UpdateConfig(BuildTempConfig());
            string sample = "\x02+ 18450 kg\r\n";
            _services.Scale.FeedRawAsciiTestFrame(sample);
        };

        var btnDefaults = new Button { Location = new Point(176, 196), Size = new Size(152, 28) };
        AppTheme.StyleSecondaryButton(btnDefaults, AppIcon.Settings, "Defecto", "Restaurar valores por defecto");
        btnDefaults.Click += (_, _) =>
        {
            _cmbCom.SelectedIndex = 0; _cmbBaud.SelectedIndex = 0; _cmbDataBits.SelectedIndex = 1; _cmbParity.SelectedIndex = 0;
            _numStx.Value = 2; _numLen.Value = 5; _numPos.Value = 3; _numLimit.Value = 1; _numDelta.Value = 10;
        };
        grpSerial.Controls.AddRange([btnOpen, btnCloseCom, btnSim, btnTestFrame, btnDefaults]);
        Controls.Add(grpSerial);

        var grpDatos = new GroupBox { Text = "Trama de Datos (0 = línea CR/LF sin STX)", Location = new Point(372, 274), Size = new Size(288, 160), Font = AppTheme.FontBodyBold, ForeColor = AppTheme.NavyDark };
        AddPairNum(grpDatos, "STX (ASCII)", _numStx, 0, 255, 26);
        AddPairNum(grpDatos, "Posición Dato", _numPos, 1, 50, 56);
        AddPairNum(grpDatos, "Largo Dato", _numLen, 1, 15, 86);
        AddPairNum(grpDatos, "Muestras Estab.", _numLimit, 1, 50, 116);
        _numStx.Value = 2; _numPos.Value = 3; _numLen.Value = 5; _numLimit.Value = 1;
        Controls.Add(grpDatos);

        var btnSave = new Button { Location = new Point(362, 448), Size = new Size(168, 36) };
        AppTheme.StylePrimaryButton(btnSave, AppIcon.Save, "Guardar y Aplicar", "Guardar y aplicar configuración de visor");
        btnSave.Click += async (_, _) => await SaveAsync();

        var btnClose = new Button { Location = new Point(538, 448), Size = new Size(122, 36) };
        AppTheme.StyleSecondaryButton(btnClose, AppIcon.Cancel, "Cerrar", "Cerrar ventana");
        btnClose.Click += (_, _) => Close();

        Controls.AddRange([btnSave, btnClose]);
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
        ClientSize = new Size(640, 440);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AppTheme.BackgroundLight;
        BuildUi();
        Load += async (_, _) => await LoadAsync();
    }

    private void BuildUi()
    {
        var g1 = new GroupBox { Text = "Cortes (%)", Location = new Point(16, 16), Size = new Size(190, 115) };
        AddN(g1, "Inicial (%)", _numCutLeft, 0, 45, 28);
        AddN(g1, "Final (%)", _numCutRight, 0, 45, 64);

        var g2 = new GroupBox { Text = "Filtros", Location = new Point(220, 16), Size = new Size(200, 115) };
        AddN(g2, "Nro Filtros", _numFiltros, 1, 500, 24);
        AddN(g2, "Hi Factor", _numUp, 0, 10, 52, decimals: 2);
        AddN(g2, "Low Factor", _numDown, 0, 10, 80, decimals: 2);

        var g3 = new GroupBox { Text = "Límites Disparo (kg)", Location = new Point(434, 16), Size = new Size(190, 115) };
        AddN(g3, "Inicial (Under)", _numUnder, 10, 5000, 28);
        AddN(g3, "Final (Over)", _numOver, 10, 5000, 64);

        var g4 = new GroupBox { Text = "Calibración y Offsets por Eje (Ejes 1 a 7)", Location = new Point(16, 144), Size = new Size(608, 232) };
        for (int i = 0; i < 7; i++)
        {
            int y = 24 + i * 28;
            g4.Controls.Add(new Label { Text = $"Eje {i + 1}", Location = new Point(18, y + 3), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            g4.Controls.Add(new Label { Text = "Calib:", Location = new Point(95, y + 3), AutoSize = true });
            _calibs[i] = new NumericUpDown { Location = new Point(145, y), Width = 120, Minimum = -10000, Maximum = 10000 };
            g4.Controls.Add(new Label { Text = "Offset:", Location = new Point(310, y + 3), AutoSize = true });
            _offsets[i] = new NumericUpDown { Location = new Point(370, y), Width = 120, Minimum = -10000, Maximum = 10000 };
            g4.Controls.AddRange([_calibs[i], _offsets[i]]);
        }

        var btnReset = new Button { Location = new Point(16, 390), Size = new Size(140, 34) };
        AppTheme.StyleSecondaryButton(btnReset, AppIcon.Refresh, "Limpiar Offsets", "Reiniciar calibraciones y offsets a 0");
        btnReset.Click += (_, _) =>
        {
            for (int i = 0; i < 7; i++) { _calibs[i].Value = 0; _offsets[i].Value = 0; }
        };

        var btnSave = new Button { Location = new Point(368, 390), Size = new Size(125, 34) };
        AppTheme.StylePrimaryButton(btnSave, AppIcon.Save, "Guardar", "Guardar parámetros dinámicos");
        btnSave.Click += async (_, _) => await SaveAsync();

        var btnClose = new Button { Location = new Point(504, 390), Size = new Size(120, 34) };
        AppTheme.StyleSecondaryButton(btnClose, AppIcon.Cancel, "Cerrar", "Cerrar");
        btnClose.Click += (_, _) => Close();

        Controls.AddRange([g1, g2, g3, g4, btnReset, btnSave, btnClose]);
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
        ClientSize = new Size(640, 450);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AppTheme.BackgroundLight;
        BuildUi();
        Load += async (_, _) => await LoadAsync();
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

        var btnTestTicket = new Button { Location = new Point(22, 400), Size = new Size(165, 34) };
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

        var btnSave = new Button { Location = new Point(365, 400), Size = new Size(125, 34) };
        AppTheme.StylePrimaryButton(btnSave, AppIcon.Save, "Guardar", "Guardar parámetros de instalación");
        btnSave.Click += async (_, _) => await SaveAsync();

        var btnClose = new Button { Location = new Point(500, 400), Size = new Size(120, 34) };
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

/// <summary>Administración completa de usuarios y matriz RBAC de privilegios.</summary>
public sealed class UsuariosForm : Form
{
    private readonly AppServices _services;
    private readonly ListBox _lstUsers = new();
    private readonly TextBox _txtNombre = new();
    private readonly TextBox _txtCargo = new();
    private readonly TextBox _txtPassword = new();
    private readonly RadioButton _rbOp = new();
    private readonly RadioButton _rbCon = new();
    private readonly RadioButton _rbAdm = new();
    private readonly CheckBox _chkActivo = new();
    private readonly CheckBox _chkCambioPw = new();
    private readonly CheckBox _chkMod = new();
    private readonly CheckBox _chkAdd = new();
    private readonly CheckBox _chkDel = new();
    private readonly CheckBox _chkPrn = new();
    private readonly CheckBox _chkPesaje = new();
    private readonly CheckBox _chkGestion = new();
    private readonly CheckBox _chkSup = new();
    private readonly CheckBox _chkJefe = new();
    private List<Usuario> _usuarios = [];
    private Usuario? _editing;

    public UsuariosForm(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        Text = "Administración de Usuarios y Permisos";
        ClientSize = new Size(760, 440);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AppTheme.BackgroundLight;
        BuildUi();
        Load += async (_, _) => await LoadAsync();
    }

    private void BuildUi()
    {
        _lstUsers.Location = new Point(16, 16);
        _lstUsers.Size = new Size(210, 360);
        _lstUsers.Font = new Font("Segoe UI", 9.5f);
        _lstUsers.SelectedIndexChanged += (_, _) =>
        {
            if (_lstUsers.SelectedItem is Usuario u) BindUser(u);
        };

        int rx = 246;
        Controls.Add(new Label { Text = "Nombre", Location = new Point(rx, 20), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        _txtNombre.SetBounds(rx + 95, 18, 390, 24);
        _txtNombre.CharacterCasing = CharacterCasing.Upper;

        Controls.Add(new Label { Text = "Cargo", Location = new Point(rx, 54), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        _txtCargo.SetBounds(rx + 95, 52, 390, 24);
        _txtCargo.CharacterCasing = CharacterCasing.Upper;

        Controls.Add(new Label { Text = "Nueva Clave", Location = new Point(rx, 88), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        _txtPassword.SetBounds(rx + 95, 86, 180, 24);
        _txtPassword.UseSystemPasswordChar = true;
        _txtPassword.CharacterCasing = CharacterCasing.Upper;
        Controls.Add(new Label { Text = "(dejar vacío para conservar actual)", Location = new Point(rx + 282, 89), AutoSize = true, ForeColor = AppTheme.TextMuted });

        var grpPerfil = new GroupBox { Text = "Perfil de Operación", Location = new Point(rx, 126), Size = new Size(235, 130) };
        _rbOp.SetBounds(14, 24, 200, 22); _rbOp.Text = "Operación de Pesaje";
        _rbCon.SetBounds(14, 52, 200, 22); _rbCon.Text = "Consulta de Datos";
        _rbAdm.SetBounds(14, 80, 200, 22); _rbAdm.Text = "Administración";
        grpPerfil.Controls.AddRange([_rbOp, _rbCon, _rbAdm]);

        var grpPriv = new GroupBox { Text = "Privilegios", Location = new Point(rx + 245, 126), Size = new Size(240, 130) };
        _chkMod.SetBounds(14, 22, 110, 22); _chkMod.Text = "Modificar";
        _chkAdd.SetBounds(128, 22, 100, 22); _chkAdd.Text = "Agregar";
        _chkDel.SetBounds(14, 50, 110, 22); _chkDel.Text = "Eliminar";
        _chkPrn.SetBounds(128, 50, 100, 22); _chkPrn.Text = "Imprimir";
        _chkSup.SetBounds(14, 78, 110, 22); _chkSup.Text = "Sup. Sistema";
        _chkJefe.SetBounds(128, 78, 105, 22); _chkJefe.Text = "Jefe Turno";
        grpPriv.Controls.AddRange([_chkMod, _chkAdd, _chkDel, _chkPrn, _chkSup, _chkJefe]);

        var grpMod = new GroupBox { Text = "Acceso y Estado", Location = new Point(rx, 266), Size = new Size(485, 110) };
        _chkPesaje.SetBounds(14, 24, 180, 22); _chkPesaje.Text = "Acceso Módulo Pesaje";
        _chkGestion.SetBounds(220, 24, 200, 22); _chkGestion.Text = "Acceso Módulo Gestión";
        _chkActivo.SetBounds(14, 58, 180, 22); _chkActivo.Text = "Usuario Activo";
        _chkCambioPw.SetBounds(220, 58, 220, 22); _chkCambioPw.Text = "Puede Cambiar Contraseña";
        grpMod.Controls.AddRange([_chkPesaje, _chkGestion, _chkActivo, _chkCambioPw]);

        var btnNew = new Button { Location = new Point(16, 390), Size = new Size(105, 34) };
        AppTheme.StyleSecondaryButton(btnNew, AppIcon.Add, "Nuevo", "Crear nuevo usuario", AppTheme.AccentEmerald);
        btnNew.Click += (_, _) =>
        {
            _editing = new Usuario { Id = 0, Activo = true, PuedeImprimir = true, PuedeCambiarPassword = true, IngresoPesaje = true };
            BindUser(_editing);
            _txtNombre.Focus();
        };

        var btnDel = new Button { Location = new Point(130, 390), Size = new Size(105, 34) };
        AppTheme.StyleDangerButton(btnDel, AppIcon.Delete, "Eliminar", "Eliminar usuario seleccionado");
        btnDel.Click += async (_, _) => await DeleteSelectedAsync();

        var btnSave = new Button { Location = new Point(478, 390), Size = new Size(125, 34) };
        AppTheme.StylePrimaryButton(btnSave, AppIcon.Save, "Guardar", "Guardar usuario y permisos");
        btnSave.Click += async (_, _) => await SaveUserAsync();

        var btnClose = new Button { Location = new Point(612, 390), Size = new Size(120, 34) };
        AppTheme.StyleSecondaryButton(btnClose, AppIcon.Cancel, "Cerrar", "Cerrar");
        btnClose.Click += (_, _) => Close();

        Controls.AddRange([_lstUsers, _txtNombre, _txtCargo, _txtPassword, grpPerfil, grpPriv, grpMod, btnNew, btnDel, btnSave, btnClose]);
    }

    private async Task LoadAsync(short? selectId = null)
    {
        _usuarios = await _services.Security.GetUsuariosAsync();
        _lstUsers.DataSource = null;
        _lstUsers.DataSource = _usuarios;
        _lstUsers.DisplayMember = nameof(Usuario.Nombre);
        if (selectId.HasValue)
        {
            Usuario? match = _usuarios.FirstOrDefault(u => u.Id == selectId.Value);
            if (match is not null) _lstUsers.SelectedItem = match;
        }
        else if (_usuarios.Count > 0)
        {
            _lstUsers.SelectedIndex = 0;
        }
    }

    private void BindUser(Usuario u)
    {
        _editing = u;
        _txtNombre.Text = u.Nombre;
        _txtCargo.Text = u.Cargo;
        _txtPassword.Text = string.Empty;
        _rbOp.Checked = u.Perfil == PerfilOperacion.OperacionPesaje;
        _rbCon.Checked = u.Perfil == PerfilOperacion.Consulta;
        _rbAdm.Checked = u.Perfil == PerfilOperacion.Administracion;
        _chkMod.Checked = u.PuedeModificar;
        _chkAdd.Checked = u.PuedeAgregar;
        _chkDel.Checked = u.PuedeEliminar;
        _chkPrn.Checked = u.PuedeImprimir;
        _chkSup.Checked = u.SupervisorSistema;
        _chkJefe.Checked = u.JefeTurno;
        _chkPesaje.Checked = u.IngresoPesaje;
        _chkGestion.Checked = u.IngresoGestion;
        _chkActivo.Checked = u.Activo;
        _chkCambioPw.Checked = u.PuedeCambiarPassword;
    }

    private async Task SaveUserAsync()
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AdministrarUsuarios))
        {
            MessageBox.Show(this, "No está autorizado para administrar usuarios.", "Seguridad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(_txtNombre.Text))
        {
            MessageBox.Show(this, "Debe ingresar el nombre del usuario.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_editing is null) _editing = new Usuario();
        if (_editing.Id == 0 && string.IsNullOrWhiteSpace(_txtPassword.Text))
        {
            MessageBox.Show(this, "Debe asignar una contraseña inicial para el nuevo usuario.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _editing.Nombre = _txtNombre.Text.Trim();
        _editing.Cargo = _txtCargo.Text.Trim();
        _editing.Perfil = _rbAdm.Checked ? PerfilOperacion.Administracion : (_rbCon.Checked ? PerfilOperacion.Consulta : PerfilOperacion.OperacionPesaje);
        _editing.PuedeModificar = _chkMod.Checked;
        _editing.PuedeAgregar = _chkAdd.Checked;
        _editing.PuedeEliminar = _chkDel.Checked;
        _editing.PuedeImprimir = _chkPrn.Checked;
        _editing.SupervisorSistema = _chkSup.Checked;
        _editing.JefeTurno = _chkJefe.Checked;
        _editing.IngresoPesaje = _chkPesaje.Checked;
        _editing.IngresoGestion = _chkGestion.Checked;
        _editing.Activo = _chkActivo.Checked;
        _editing.PuedeCambiarPassword = _chkCambioPw.Checked;

        short id = await _services.Security.SaveUsuarioAsync(_editing, _txtPassword.Text.Trim(), _services.UsuarioActual?.Id ?? 0);
        await LoadAsync(id);
        MessageBox.Show(this, "Usuario guardado correctamente.", "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task DeleteSelectedAsync()
    {
        if (_editing is null || _editing.Id <= 0) return;
        if (_editing.Id == _services.UsuarioActual?.Id)
        {
            MessageBox.Show(this, "No puede eliminar su propio usuario con la sesión iniciada.", "Seguridad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(this, $"¿Eliminar usuario '{_editing.Nombre}'?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        await _services.Security.DeleteUsuarioAsync(_editing.Id, _services.UsuarioActual?.Id ?? 0);
        await LoadAsync();
    }
}
