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
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 610);
        BackColor = AppTheme.BackgroundLight;
        AppTheme.ApplyFormIcon(this);

        var header = new Panel { Dock = DockStyle.Top, Height = 56 };
        header.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, header.ClientRectangle);
        var lblHead = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 0, 18, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = AppTheme.FontTitle,
            Text = "Configuración de Servidor BD Remoto / Local y Migración ODS 9.1"
        };
        header.Controls.Add(lblHead);

        DatabaseSettings s = _services.DatabaseSettings;
        int y = 68;

        // Bloque 1: Ruta de Red UNC / Presets rápidos
        var grpRed = new GroupBox
        {
            Text = "Conexión Rápida / Ruta Compartida en Red (UNC o IP)",
            Location = new Point(16, y),
            Size = new Size(726, 94),
            BackColor = AppTheme.SurfaceCard,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.NavyDark
        };
        grpRed.Controls.Add(new Label { Text = "Ruta Red / UNC:", Location = new Point(14, 28), AutoSize = true, Font = AppTheme.FontBodyBold });
        _txtNetworkInput.Location = new Point(132, 25);
        _txtNetworkInput.Width = 420;
        _txtNetworkInput.Font = AppTheme.FontBody;
        _txtNetworkInput.Text = !string.IsNullOrWhiteSpace(s.RawNetworkPath)
            ? s.RawNetworkPath
            : @"\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB";

        _btnResolveUnc.Location = new Point(560, 23);
        _btnResolveUnc.Size = new Size(152, 28);
        AppTheme.StyleSecondaryButton(_btnResolveUnc, AppIcon.Search, "Analizar Ruta Red", "Extraer host IP y ruta del servidor desde ruta UNC o local", AppTheme.NavyPrimary, _tt);
        _btnResolveUnc.Click += (_, _) => ApplyUncInputToFields();

        var btnPresetRemote = new Button { Location = new Point(132, 58), Size = new Size(206, 26) };
        AppTheme.StyleSecondaryButton(btnPresetRemote, AppIcon.Database, "Usar Ruta Red 192.168.2.56", "Cargar \\\\192.168.2.56\\bdwm\\placilla2\\WMDBSPC.GDB", null, _tt);
        btnPresetRemote.Click += (_, _) =>
        {
            _txtNetworkInput.Text = @"\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB";
            ApplyUncInputToFields();
        };

        var btnPresetLocal = new Button { Location = new Point(346, 58), Size = new Size(206, 26) };
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
        grpRed.Controls.AddRange([_txtNetworkInput, _btnResolveUnc, btnPresetRemote, btnPresetLocal]);

        y += 104;

        // Bloque 2: Parámetros TCP Firebird
        var grpParams = new GroupBox
        {
            Text = "Parámetros de Conexión Firebird / InterBase TCP",
            Location = new Point(16, y),
            Size = new Size(726, 140),
            BackColor = AppTheme.SurfaceCard,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.NavyDark
        };

        AddRow(grpParams, "Servidor / IP", _txtHost, s.DataSource, 280, 26, 128);
        grpParams.Controls.Add(new Label { Text = "Puerto TCP", Location = new Point(428, 29), AutoSize = true, Font = AppTheme.FontBodyBold });
        _numPort.SetBounds(520, 26, 86, 24);
        _numPort.Minimum = 1;
        _numPort.Maximum = 65535;
        _numPort.Value = s.Port;

        AddRow(grpParams, "Ruta BD Servidor", _txtDbPath, s.DatabasePath, 462, 60, 128);
        var btnBrowseFdb = new Button { Location = new Point(600, 57), Size = new Size(112, 30) };
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
                if (ofd.FileName.StartsWith(@"\\", StringComparison.Ordinal))
                {
                    ApplyUncInputToFields();
                }
                else
                {
                    _txtDbPath.Text = ofd.FileName;
                }
            }
        };

        grpParams.Controls.Add(new Label { Text = "Usuario", Location = new Point(14, 97), AutoSize = true, Font = AppTheme.FontBodyBold });
        _txtUser.SetBounds(128, 94, 115, 24);
        _txtUser.Text = s.UserId;

        grpParams.Controls.Add(new Label { Text = "Clave", Location = new Point(252, 97), AutoSize = true, Font = AppTheme.FontBodyBold });
        _txtPass.SetBounds(296, 94, 105, 24);
        _txtPass.UseSystemPasswordChar = true;
        _txtPass.Text = s.Password;

        grpParams.Controls.Add(new Label { Text = "Dialecto", Location = new Point(410, 97), AutoSize = true, Font = AppTheme.FontBodyBold });
        _cmbDialect.SetBounds(472, 94, 92, 24);
        _cmbDialect.Items.AddRange(["3 (Moderno)", "1 (Legacy)"]);
        _cmbDialect.SelectedIndex = s.Dialect == 1 ? 1 : 0;

        grpParams.Controls.Add(new Label { Text = "Charset", Location = new Point(574, 97), AutoSize = true, Font = AppTheme.FontBodyBold });
        _cmbCharset.SetBounds(630, 94, 82, 24);
        _cmbCharset.Items.AddRange(["UTF8", "WIN1252", "NONE"]);
        int csIdx = _cmbCharset.FindStringExact(s.Charset);
        _cmbCharset.SelectedIndex = csIdx >= 0 ? csIdx : 0;

        grpParams.Controls.AddRange([_numPort, btnBrowseFdb, _txtUser, _txtPass, _cmbDialect, _cmbCharset]);

        y += 148;

        // Bloque 3: Migración ODS 9.1 -> .FDB
        var grpMig = new GroupBox
        {
            Text = "Migración Automática ODS 9.1 (.GDB) -> Firebird UTF8 (.FDB)",
            Location = new Point(16, y),
            Size = new Size(726, 94),
            BackColor = AppTheme.SurfaceCard,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.NavyDark
        };
        _txtGdbLegacy.SetBounds(16, 24, 496, 24);
        _txtGdbLegacy.Text = @"D:\archivos qlabel\PESAJE CAMIONES\placillaPRUEBA\WMDBSPC.GDB";
        var btnPickGdb = new Button { Location = new Point(520, 21), Size = new Size(96, 30) };
        AppTheme.StyleSecondaryButton(btnPickGdb, AppIcon.Search, "Buscar", "Seleccionar archivo WMDBSPC.GDB legacy", null, _tt);
        btnPickGdb.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog { Filter = "InterBase Legacy GDB|*.gdb;*.GDB" };
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                _txtGdbLegacy.Text = ofd.FileName;
            }
        };
        _btnMigrate.SetBounds(16, 54, 244, 32);
        AppTheme.StyleSecondaryButton(_btnMigrate, AppIcon.Migrate, "Migrar GDB -> FDB Local", "Leer ODS 9.1 binario, sanear anomalías e importar hacia destino .FDB", AppTheme.NavyPrimary, _tt);
        _btnMigrate.Click += async (_, _) => await RunMigrationAsync();

        _btnInit.SetBounds(270, 54, 178, 32);
        AppTheme.StyleSecondaryButton(_btnInit, AppIcon.Add, "Crear BD Vacía", "Crear nueva base Firebird Dialecto 3 limpia", AppTheme.AccentEmerald, _tt);
        _btnInit.Click += async (_, _) => await InitializeFreshDbAsync();

        var btnLogs = new Button { Location = new Point(458, 54), Size = new Size(172, 32) };
        AppTheme.StyleSecondaryButton(btnLogs, AppIcon.Logs, "Ver Logs de Errores", "Abrir diagnóstico de errores", AppTheme.AccentAmber, _tt);
        btnLogs.Click += (_, _) => { using var f = new ErrorLogViewerDialog(); f.ShowDialog(this); };

        grpMig.Controls.AddRange([_txtGdbLegacy, btnPickGdb, _btnMigrate, _btnInit, btnLogs]);

        y += 102;

        // Banner estado prueba + Log consola diagnóstico
        _lblConnBanner.Location = new Point(16, y);
        _lblConnBanner.Size = new Size(726, 26);
        _lblConnBanner.BackColor = AppTheme.SlateHeader;
        _lblConnBanner.ForeColor = Color.White;
        _lblConnBanner.Font = AppTheme.FontBodyBold;
        _lblConnBanner.TextAlign = ContentAlignment.MiddleLeft;
        _lblConnBanner.Padding = new Padding(10, 0, 10, 0);
        _lblConnBanner.Text = "Estado: Presione 'Probar Conexión' para validar host, ruta y esquema.";

        y += 30;
        _txtLog.Location = new Point(16, y);
        _txtLog.Size = new Size(726, 106);
        _txtLog.Multiline = true;
        _txtLog.ScrollBars = ScrollBars.Vertical;
        _txtLog.ReadOnly = true;
        _txtLog.BackColor = AppTheme.LcdBackground;
        _txtLog.ForeColor = Color.Gainsboro;
        _txtLog.Font = AppTheme.FontMono;

        y += 124;

        // Fila inferior de acciones: Probar, Aplicar, Guardar y Cerrar
        _btnTest.SetBounds(16, y, 162, 36);
        AppTheme.StylePrimaryButton(_btnTest, AppIcon.Refresh, "Probar Conexión", "Probar puerto TCP, resolver ruta UNC/local, dialecto y esquema", _tt);
        _btnTest.Click += async (_, _) => await TestConnectionAsync();

        _btnApply.SetBounds(188, y, 178, 36);
        AppTheme.StyleSecondaryButton(_btnApply, AppIcon.Check, "Aplicar (Sin Cerrar)", "Aplicar conexión activa inmediatamente sin cerrar el modal", AppTheme.AccentEmerald, _tt);
        _btnApply.Click += async (_, _) => await ApplyConfigurationAsync(closeAfter: false);

        _btnSave.SetBounds(438, y, 178, 36);
        AppTheme.StylePrimaryButton(_btnSave, AppIcon.Save, "Guardar y Conectar", "Guardar configuración persistente y aplicar", _tt);
        _btnSave.Click += async (_, _) => await ApplyConfigurationAsync(closeAfter: true);

        var btnCerrar = new Button { Location = new Point(624, y), Size = new Size(118, 36) };
        AppTheme.StyleSecondaryButton(btnCerrar, AppIcon.Cancel, "Cerrar", "Cerrar asistente", null, _tt);
        btnCerrar.Click += (_, _) =>
        {
            DialogResult = _appliedAnyChange ? DialogResult.OK : DialogResult.Cancel;
            Close();
        };

        Controls.AddRange([header, grpRed, grpParams, grpMig, _lblConnBanner, _txtLog, _btnTest, _btnApply, _btnSave, btnCerrar]);
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
