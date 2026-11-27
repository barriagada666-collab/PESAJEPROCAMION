using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Data.Configuration;
using PesajeProCamion.Data.Migration;
using PesajeProCamion.Data.Schema;
using PesajeProCamion.Domain.Diagnostics;

namespace PesajeProCamion.App.UI.Dialogs;

/// <summary>
/// Modal avanzado de conexión a servidor de base de datos local/remoto (con resolución UNC \\IP\recurso\archivo.GDB),
/// prueba de conexión en vivo, aplicación inmediata, guardado persistente y migrador ODS 9.1.
/// </summary>
public sealed class DatabaseConnectionForm : Form
{
    private readonly AppServices _services;
    private readonly ToolTip _tt = new();
    private readonly TextBox _txtNetworkInput = new();
    private readonly TextBox _txtHost = new();
    private readonly NumericUpDown _numPort = new();
    private readonly TextBox _txtDbPath = new();
    private readonly ComboBox _cmbDialect = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbCharset = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtUser = new();
    private readonly TextBox _txtPass = new();
    private readonly TextBox _txtGdbLegacy = new();
    private readonly TextBox _txtLog = new();
    private readonly Label _lblConnBanner = new();
    private readonly Button _btnResolveUnc = new();
    private readonly Button _btnTest = new();
    private readonly Button _btnInit = new();
    private readonly Button _btnMigrate = new();
    private readonly Button _btnApply = new();
    private readonly Button _btnSave = new();

    private DatabaseSettings? _lastVerifiedSettings;
    private bool _appliedAnyChange;

    public DatabaseConnectionForm(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        InitializeUi();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tt.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeUi()
    {
        Text = "Conexión a Base de Datos Remota / Local y Migrador (.GDB)";
        AppTheme.ConfigureResponsiveDialog(this, 790, 650, 760, 620, resizable: true);

        var header = new Panel { Dock = DockStyle.Top, Height = 54 };
        header.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, header.ClientRectangle);
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 0, 18, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = AppTheme.FontTitle,
            Text = "Configuración de Servidor BD Remoto / Local y Migración ODS 9.1"
        });

        var footerBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 8, 14, 8),
            BackColor = AppTheme.SurfaceCard,
            WrapContents = false
        };

        var btnCerrar = new Button { Size = new Size(122, 36) };
        AppTheme.StyleSecondaryButton(btnCerrar, AppIcon.Cancel, "Cerrar", "Cerrar asistente", null, _tt);
        btnCerrar.Click += (_, _) =>
        {
            DialogResult = _appliedAnyChange ? DialogResult.OK : DialogResult.Cancel;
            Close();
        };

        _btnSave.Size = new Size(188, 36);
        AppTheme.StylePrimaryButton(_btnSave, AppIcon.Save, "Guardar y Conectar", "Guardar configuración persistente y aplicar", _tt);
        _btnSave.Click += async (_, _) => await ApplyConfigurationAsync(closeAfter: true);

        _btnApply.Size = new Size(192, 36);
        AppTheme.StyleSecondaryButton(_btnApply, AppIcon.Check, "Aplicar (Sin Cerrar)", "Aplicar conexión activa inmediatamente sin cerrar el modal", AppTheme.AccentEmerald, _tt);
        _btnApply.Click += async (_, _) => await ApplyConfigurationAsync(closeAfter: false);

        _btnTest.Size = new Size(168, 36);
        AppTheme.StylePrimaryButton(_btnTest, AppIcon.Refresh, "Probar Conexión", "Probar puerto TCP, resolver ruta UNC/local, dialecto y esquema", _tt);
        _btnTest.Click += async (_, _) => await TestConnectionAsync();

        footerBar.Controls.AddRange([btnCerrar, _btnSave, _btnApply, _btnTest]);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(14, 8, 14, 8)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 112f));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 140f));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 112f));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        DatabaseSettings s = _services.DatabaseSettings;

        // Bloque 1: Ruta de Red UNC / Presets rápidos
        var grpRed = new GroupBox
        {
            Text = "Conexión Rápida / Ruta Compartida en Red (UNC o IP)",
            Dock = DockStyle.Fill,
            BackColor = AppTheme.SurfaceCard,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.NavyDark
        };
        var redLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(10, 6, 10, 4) };
        redLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118f));
        redLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        redLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176f));
        redLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        redLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        redLayout.Controls.Add(new Label { Text = "Ruta Red / UNC:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = AppTheme.FontBodyBold }, 0, 0);
        _txtNetworkInput.Dock = DockStyle.Fill;
        _txtNetworkInput.Margin = new Padding(0, 4, 8, 2);
        _txtNetworkInput.Font = AppTheme.FontBody;
        _txtNetworkInput.Text = !string.IsNullOrWhiteSpace(s.RawNetworkPath)
            ? s.RawNetworkPath
            : @"\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB";
        redLayout.Controls.Add(_txtNetworkInput, 1, 0);

        _btnResolveUnc.Dock = DockStyle.Fill;
        _btnResolveUnc.Margin = new Padding(0, 1, 0, 1);
        AppTheme.StyleSecondaryButton(_btnResolveUnc, AppIcon.Search, "Analizar Ruta Red", "Extraer host IP y ruta del servidor desde ruta UNC o local", AppTheme.NavyPrimary, _tt);
        _btnResolveUnc.Click += (_, _) => ApplyUncInputToFields();
        redLayout.Controls.Add(_btnResolveUnc, 2, 0);

        var presetFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var btnPresetRemote = new Button { Size = new Size(228, 30), Margin = new Padding(0, 1, 8, 1) };
        AppTheme.StyleSecondaryButton(btnPresetRemote, AppIcon.Database, "Usar Ruta Red 192.168.2.56", "Cargar \\\\192.168.2.56\\bdwm\\placilla2\\WMDBSPC.GDB", null, _tt);
        btnPresetRemote.Click += (_, _) =>
        {
            _txtNetworkInput.Text = @"\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB";
            ApplyUncInputToFields();
        };

        var btnPresetLocal = new Button { Size = new Size(220, 30), Margin = new Padding(0, 1, 0, 1) };
        AppTheme.StyleSecondaryButton(btnPresetLocal, AppIcon.Database, "Usar Base Local (.FDB)", "Configurar base local localhost WMDBSPC.FDB", null, _tt);
        btnPresetLocal.Click += (_, _) =>
        {
            _txtHost.Text = "localhost";
            _numPort.Value = 3050;
            _txtDbPath.Text = DatabaseSettings.GetDefaultDatabasePath();
            _cmbDialect.SelectedIndex = 0;
            _cmbCharset.SelectedIndex = 0;
            _txtNetworkInput.Text = _txtDbPath.Text;
            _lastVerifiedSettings = null;
        };
        presetFlow.Controls.AddRange([btnPresetRemote, btnPresetLocal]);
        redLayout.Controls.Add(presetFlow, 1, 1);
        redLayout.SetColumnSpan(presetFlow, 2);
        grpRed.Controls.Add(redLayout);

        // Bloque 2: Parámetros TCP Firebird
        var grpParams = new GroupBox
        {
            Text = "Parámetros de Conexión Firebird / InterBase TCP",
            Dock = DockStyle.Fill,
            BackColor = AppTheme.SurfaceCard,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.NavyDark
        };
        var paramLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Padding = new Padding(10, 6, 10, 4) };
        paramLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118f));
        paramLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        paramLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 134f));
        paramLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        paramLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        paramLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        paramLayout.Controls.Add(new Label { Text = "Servidor / IP:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = AppTheme.FontBodyBold }, 0, 0);
        var hostRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        _txtHost.Width = 280; _txtHost.Text = s.DataSource; _txtHost.Font = AppTheme.FontBody; _txtHost.Margin = new Padding(0, 4, 14, 0);
        _numPort.Width = 86; _numPort.Minimum = 1; _numPort.Maximum = 65535; _numPort.Value = s.Port; _numPort.Margin = new Padding(4, 4, 0, 0);
        hostRow.Controls.AddRange([_txtHost, new Label { Text = "Puerto TCP:", AutoSize = true, Margin = new Padding(4, 7, 4, 0), Font = AppTheme.FontBodyBold }, _numPort]);
        paramLayout.Controls.Add(hostRow, 1, 0);
        paramLayout.SetColumnSpan(hostRow, 2);

        paramLayout.Controls.Add(new Label { Text = "Ruta BD Servidor:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = AppTheme.FontBodyBold }, 0, 1);
        _txtDbPath.Dock = DockStyle.Fill; _txtDbPath.Text = s.DatabasePath; _txtDbPath.Font = AppTheme.FontBody; _txtDbPath.Margin = new Padding(0, 4, 8, 2);
        paramLayout.Controls.Add(_txtDbPath, 1, 1);

        var btnBrowseFdb = new Button { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 1) };
        AppTheme.StyleSecondaryButton(btnBrowseFdb, AppIcon.Search, "Examinar", "Seleccionar archivo local/red (.FDB/.GDB)", null, _tt);
        btnBrowseFdb.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Firebird/InterBase|*.fdb;*.FDB;*.gdb;*.GDB|Todos|*.*",
                CheckFileExists = false,
                FileName = _txtDbPath.Text
            };
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                _txtNetworkInput.Text = ofd.FileName;
                if (ofd.FileName.StartsWith(@"\\", StringComparison.Ordinal)) ApplyUncInputToFields();
                else _txtDbPath.Text = ofd.FileName;
            }
        };
        paramLayout.Controls.Add(btnBrowseFdb, 2, 1);

        paramLayout.Controls.Add(new Label { Text = "Usuario:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = AppTheme.FontBodyBold }, 0, 2);
        var credsRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        _txtUser.Width = 104; _txtUser.Text = s.UserId; _txtUser.Margin = new Padding(0, 5, 10, 0);
        _txtPass.Width = 96; _txtPass.UseSystemPasswordChar = true; _txtPass.Text = s.Password; _txtPass.Margin = new Padding(2, 5, 10, 0);
        _cmbDialect.Width = 96; _cmbDialect.Items.AddRange(["3 (Moderno)", "1 (Legacy)"]); _cmbDialect.SelectedIndex = s.Dialect == 1 ? 1 : 0; _cmbDialect.Margin = new Padding(2, 5, 10, 0);
        _cmbCharset.Width = 86; _cmbCharset.Items.AddRange(["UTF8", "WIN1252", "NONE"]);
        int csIdx = _cmbCharset.FindStringExact(s.Charset);
        _cmbCharset.SelectedIndex = csIdx >= 0 ? csIdx : 0;
        _cmbCharset.Margin = new Padding(2, 5, 0, 0);
        credsRow.Controls.AddRange([
            _txtUser,
            new Label { Text = "Clave:", AutoSize = true, Margin = new Padding(0, 8, 4, 0), Font = AppTheme.FontBodyBold }, _txtPass,
            new Label { Text = "Dialecto:", AutoSize = true, Margin = new Padding(0, 8, 4, 0), Font = AppTheme.FontBodyBold }, _cmbDialect,
            new Label { Text = "Charset:", AutoSize = true, Margin = new Padding(0, 8, 4, 0), Font = AppTheme.FontBodyBold }, _cmbCharset
        ]);
        paramLayout.Controls.Add(credsRow, 1, 2);
        paramLayout.SetColumnSpan(credsRow, 2);
        grpParams.Controls.Add(paramLayout);

        // Bloque 3: Migración ODS 9.1 -> .FDB
        var grpMig = new GroupBox
        {
            Text = "Migración Automática ODS 9.1 (.GDB) -> Firebird UTF8 (.FDB)",
            Dock = DockStyle.Fill,
            BackColor = AppTheme.SurfaceCard,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.NavyDark
        };
        var migLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(10, 6, 10, 4) };
        migLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        migLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 134f));
        migLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        migLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _txtGdbLegacy.Dock = DockStyle.Fill;
        _txtGdbLegacy.Margin = new Padding(0, 4, 8, 2);
        _txtGdbLegacy.Text = @"D:\archivos qlabel\PESAJE CAMIONES\placillaPRUEBA\WMDBSPC.GDB";
        migLayout.Controls.Add(_txtGdbLegacy, 0, 0);

        var btnPickGdb = new Button { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 1) };
        AppTheme.StyleSecondaryButton(btnPickGdb, AppIcon.Search, "Examinar...", "Seleccionar archivo WMDBSPC.GDB legacy", null, _tt);
        btnPickGdb.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog { Filter = "InterBase Legacy GDB|*.gdb;*.GDB" };
            if (ofd.ShowDialog(this) == DialogResult.OK) _txtGdbLegacy.Text = ofd.FileName;
        };
        migLayout.Controls.Add(btnPickGdb, 1, 0);

        var migBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        _btnMigrate.Size = new Size(248, 30);
        _btnMigrate.Margin = new Padding(0, 1, 8, 1);
        AppTheme.StyleSecondaryButton(_btnMigrate, AppIcon.Migrate, "Migrar GDB -> FDB Local", "Leer ODS 9.1 binario, sanear anomalías e importar hacia destino .FDB", AppTheme.NavyPrimary, _tt);
        _btnMigrate.Click += async (_, _) => await RunMigrationAsync();

        _btnInit.Size = new Size(184, 30);
        _btnInit.Margin = new Padding(0, 1, 8, 1);
        AppTheme.StyleSecondaryButton(_btnInit, AppIcon.Add, "Crear BD Vacía", "Crear nueva base Firebird Dialecto 3 limpia", AppTheme.AccentEmerald, _tt);
        _btnInit.Click += async (_, _) => await InitializeFreshDbAsync();

        var btnLogs = new Button { Size = new Size(184, 30), Margin = new Padding(0, 1, 0, 1) };
        AppTheme.StyleSecondaryButton(btnLogs, AppIcon.Logs, "Ver Logs de Errores", "Abrir diagnóstico de errores", AppTheme.AccentAmber, _tt);
        btnLogs.Click += (_, _) => { using var f = new ErrorLogViewerDialog(); f.ShowDialog(this); };
        migBtns.Controls.AddRange([_btnMigrate, _btnInit, btnLogs]);
        migLayout.Controls.Add(migBtns, 0, 1);
        migLayout.SetColumnSpan(migBtns, 2);
        grpMig.Controls.Add(migLayout);

        _lblConnBanner.Dock = DockStyle.Fill;
        _lblConnBanner.Margin = new Padding(0, 4, 0, 2);
        _lblConnBanner.BackColor = AppTheme.SlateHeader;
        _lblConnBanner.ForeColor = Color.White;
        _lblConnBanner.Font = AppTheme.FontBodyBold;
        _lblConnBanner.TextAlign = ContentAlignment.MiddleLeft;
        _lblConnBanner.AutoEllipsis = true;
        _lblConnBanner.Padding = new Padding(10, 0, 10, 0);
        _lblConnBanner.Text = "Estado: Presione 'Probar Conexión' para validar host, ruta y esquema.";

        _txtLog.Dock = DockStyle.Fill;
        _txtLog.Multiline = true;
        _txtLog.ScrollBars = ScrollBars.Vertical;
        _txtLog.ReadOnly = true;
        _txtLog.BackColor = AppTheme.LcdBackground;
        _txtLog.ForeColor = Color.Gainsboro;
        _txtLog.Font = AppTheme.FontMono;

        body.Controls.Add(grpRed, 0, 0);
        body.Controls.Add(grpParams, 0, 1);
        body.Controls.Add(grpMig, 0, 2);
        body.Controls.Add(_lblConnBanner, 0, 3);
        body.Controls.Add(_txtLog, 0, 4);
        Controls.AddRange([body, footerBar, header]);
    }

    private static void AddRow(Control parent, string label, TextBox tb, string value, int width, int y, int inputX)
    {
        parent.Controls.Add(new Label { Text = label, Location = new Point(14, y + 3), AutoSize = true, Font = AppTheme.FontBodyBold });
        tb.Location = new Point(inputX, y);
        tb.Width = width;
        tb.Text = value;
        tb.Font = AppTheme.FontBody;
        parent.Controls.Add(tb);
    }

    private void ApplyUncInputToFields()
    {
        string raw = _txtNetworkInput.Text.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        (string? host, IReadOnlyList<string> candidates) = RemoteDatabaseResolver.BuildCandidates(_txtHost.Text, raw);
        if (!string.IsNullOrWhiteSpace(host))
        {
            _txtHost.Text = host;
        }
        if (candidates.Count > 0)
        {
            _txtDbPath.Text = candidates[0];
        }
        if (raw.EndsWith(".GDB", StringComparison.OrdinalIgnoreCase))
        {
            _cmbDialect.SelectedIndex = 1; // 1 (Legacy)
            _cmbCharset.SelectedIndex = 2; // NONE (compatible ODS 9.1 sin ICU/intl)
        }
        _lastVerifiedSettings = null;
        _txtLog.Text = $"Ruta analizada:\r\n  Host IP  : {_txtHost.Text}\r\n  Candidato: {_txtDbPath.Text}\r\nPresione 'Probar Conexión' para autocomprobar los candidatos en red.";
    }

    private DatabaseSettings BuildSettingsFromInputs()
    {
        string dbInput = _txtDbPath.Text.Trim();
        string rawNet = _txtNetworkInput.Text.Trim();
        return new DatabaseSettings
        {
            DataSource = _txtHost.Text.Trim(),
            Port = (int)_numPort.Value,
            DatabasePath = dbInput,
            UserId = _txtUser.Text.Trim(),
            Password = _txtPass.Text,
            Dialect = _cmbDialect.SelectedIndex == 1 ? 1 : 3,
            Charset = _cmbCharset.SelectedItem?.ToString() ?? "UTF8",
            RawNetworkPath = rawNet.StartsWith(@"\\", StringComparison.Ordinal) ? rawNet : null
        };
    }

    private async Task<DatabaseProbeResult> TestConnectionAsync()
    {
        _btnTest.Enabled = false;
        _lblConnBanner.BackColor = AppTheme.NavyPrimary;
        _lblConnBanner.Text = "Probando conectividad TCP y resolviendo base de datos...";
        _txtLog.Text = "Iniciando diagnóstico de conexión...\r\n";

        try
        {
            string inputToProbe = !string.IsNullOrWhiteSpace(_txtDbPath.Text)
                ? (_txtNetworkInput.Text.Trim().StartsWith(@"\\", StringComparison.Ordinal) &&
                   string.Equals(_txtHost.Text.Trim(), RemoteDatabaseResolver.BuildCandidates(_txtHost.Text, _txtNetworkInput.Text).ExtractedHost, StringComparison.OrdinalIgnoreCase)
                    ? _txtNetworkInput.Text.Trim()
                    : _txtDbPath.Text.Trim())
                : _txtNetworkInput.Text.Trim();

            DatabaseSettings candidate = BuildSettingsFromInputs();
            DatabaseProbeResult probe = await RemoteDatabaseResolver.ResolveAndProbeAsync(candidate, inputToProbe, autoDetectDialectAndCharset: true);
            _txtLog.Text = string.Join(Environment.NewLine, probe.AttemptLog);

            if (probe.Success)
            {
                _lastVerifiedSettings = probe.ResolvedSettings;
                _txtHost.Text = probe.ResolvedSettings.DataSource;
                _numPort.Value = probe.ResolvedSettings.Port;
                _txtDbPath.Text = probe.ResolvedSettings.DatabasePath;
                _cmbDialect.SelectedIndex = probe.ResolvedSettings.Dialect == 1 ? 1 : 0;
                int csIdx = _cmbCharset.FindStringExact(probe.ResolvedSettings.Charset);
                if (csIdx >= 0) _cmbCharset.SelectedIndex = csIdx;

                _lblConnBanner.BackColor = AppTheme.AccentEmerald;
                _lblConnBanner.Text = $"CONECTADO ({probe.LatencyMs} ms) — {probe.Empresa} | {probe.TotalPesajes:N0} pesajes | {probe.ServerVersion}";
            }
            else
            {
                _lastVerifiedSettings = null;
                _lblConnBanner.BackColor = AppTheme.AccentDanger;
                _lblConnBanner.Text = $"ERROR: {probe.ErrorMessage}";
            }
            return probe;
        }
        finally
        {
            _btnTest.Enabled = true;
        }
    }

    private async Task ApplyConfigurationAsync(bool closeAfter)
    {
        DatabaseSettings toApply = _lastVerifiedSettings ?? BuildSettingsFromInputs();

        // Si el usuario modificó la ruta UNC o aún no se ha verificado, autoprobar primero
        if (_lastVerifiedSettings is null ||
            !string.Equals(_lastVerifiedSettings.DataSource, _txtHost.Text.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_lastVerifiedSettings.DatabasePath, _txtDbPath.Text.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            DatabaseProbeResult probe = await TestConnectionAsync();
            if (!probe.Success)
            {
                if (MessageBox.Show(
                        this,
                        $"La prueba de conexión falló:\r\n{probe.ErrorMessage}\r\n\r\n¿Desea guardar estos parámetros de todas formas?",
                        "Advertencia de Conexión",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
            }
            else
            {
                toApply = probe.ResolvedSettings;
            }
        }

        _services.UpdateDatabaseSettings(toApply);
        await _services.RefreshMetadataSafeAsync();
        _appliedAnyChange = true;
        AppLogger.Info("DBConfig", $"Configuración aplicada y guardada: {toApply.DataSource}:{toApply.Port}/{toApply.DatabasePath} (D={toApply.Dialect}, C={toApply.Charset})");

        if (closeAfter)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
        else
        {
            _lblConnBanner.BackColor = AppTheme.AccentEmerald;
            _lblConnBanner.Text = $"APLICADO Y GUARDADO EN: {AppConfigStore.ConfigFilePath}";
            MessageBox.Show(this, $"Configuración aplicada y guardada correctamente.\r\nServidor: {toApply.DataSource}:{toApply.Port}\r\nBase: {toApply.DatabasePath}", "Conexión Aplicada", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task InitializeFreshDbAsync()
    {
        try
        {
            DatabaseSettings s = BuildSettingsFromInputs();
            if (s.Dialect != 3)
            {
                s.Dialect = 3;
                _cmbDialect.SelectedIndex = 0;
            }
            if (!string.Equals(s.Charset, "UTF8", StringComparison.OrdinalIgnoreCase))
            {
                s.Charset = "UTF8";
                _cmbCharset.SelectedIndex = 0;
            }
            await FirebirdSchemaManager.EnsureDatabaseInitializedAsync(s);
            _lastVerifiedSettings = s;
            _lblConnBanner.BackColor = AppTheme.AccentEmerald;
            _lblConnBanner.Text = $"Base Firebird inicializada correctamente en {s.DatabasePath}";
            _txtLog.Text = $"Base Firebird creada/verificada en {s.DatabasePath}.\r\nUsuario administrador inicial: ADMINISTRADOR / clave: ADMIN";
        }
        catch (Exception ex)
        {
            AppLogger.Error("DBConfig", "Error inicializando base de datos limpia.", ex);
            _lblConnBanner.BackColor = AppTheme.AccentDanger;
            _lblConnBanner.Text = $"Error creando base de datos: {ex.Message}";
            _txtLog.Text = ex.ToString();
        }
    }

    private async Task RunMigrationAsync()
    {
        string gdb = _txtGdbLegacy.Text.Trim();
        if (!File.Exists(gdb))
        {
            MessageBox.Show(this, "No se encontró el archivo .GDB especificado.", "Migración", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnMigrate.Enabled = false;
        _lblConnBanner.BackColor = AppTheme.NavyPrimary;
        _lblConnBanner.Text = "Leyendo estructura ODS 9.1 y migrando hacia Firebird UTF8...";
        _txtLog.Text = _lblConnBanner.Text + Environment.NewLine;

        try
        {
            DatabaseSettings target = BuildSettingsFromInputs();
            target.Dialect = 3;
            target.Charset = "UTF8";
            MigrationAuditReport rep = await Ods9Migrator.MigrateAsync(gdb, target, hashPasswords: true, overwriteTarget: true);
            _lastVerifiedSettings = target;
            _cmbDialect.SelectedIndex = 0;
            _cmbCharset.SelectedIndex = 0;

            var lines = new List<string>
            {
                $"Migración completada ({rep.SourceOdsVersion} -> Firebird UTF8):",
                $"Destino: {rep.TargetDatabase}"
            };
            lines.AddRange(rep.TableRowCounts.OrderBy(k => k.Key).Select(kv => string.Create(CultureInfo.InvariantCulture, $"  - {kv.Key,-15}: {kv.Value} registros")));
            if (rep.Findings.Count > 0)
            {
                lines.Add("Saneamientos aplicados:");
                lines.AddRange(rep.Findings.Select(f => $"  * {f}"));
            }
            _txtLog.Text = string.Join(Environment.NewLine, lines);
            _lblConnBanner.BackColor = AppTheme.AccentEmerald;
            _lblConnBanner.Text = $"MIGRACIÓN EXITOSA -> {rep.TargetDatabase}";
            AppLogger.Info("Migrator", $"Migración ODS 9.1 completada hacia {rep.TargetDatabase}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Migrator", "Error durante migración ODS 9.1.", ex);
            _lblConnBanner.BackColor = AppTheme.AccentDanger;
            _lblConnBanner.Text = $"ERROR EN MIGRACIÓN: {ex.Message}";
            _txtLog.Text += ex.ToString();
        }
        finally
        {
            _btnMigrate.Enabled = true;
        }
    }
}
