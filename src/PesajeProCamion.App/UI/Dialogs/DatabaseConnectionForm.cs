using System.Drawing;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Data.Configuration;
using PesajeProCamion.Data.Migration;
using PesajeProCamion.Data.Schema;

namespace PesajeProCamion.App.UI.Dialogs;

/// <summary>Asistente de configuración de servidor Firebird y migración directa desde archivos ODS 9.1 (*.GDB).</summary>
public sealed class DatabaseConnectionForm : Form
{
    private readonly AppServices _services;
    private readonly ToolTip _tt = new();
    private readonly TextBox _txtHost = new();
    private readonly NumericUpDown _numPort = new();
    private readonly TextBox _txtDbPath = new();
    private readonly TextBox _txtUser = new();
    private readonly TextBox _txtPass = new();
    private readonly TextBox _txtGdbLegacy = new();
    private readonly TextBox _txtLog = new();
    private readonly Button _btnTest = new();
    private readonly Button _btnInit = new();
    private readonly Button _btnMigrate = new();
    private readonly Button _btnSave = new();

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
        Text = "Conexión Firebird y Migrador ODS 9.1 (.GDB)";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(650, 470);
        BackColor = AppTheme.BackgroundLight;

        DatabaseSettings s = _services.DatabaseSettings;
        int y = 18;
        AddRow("Servidor / Host", _txtHost, s.DataSource, 220, y);
        _numPort.Location = new Point(495, y);
        _numPort.Width = 100;
        _numPort.Minimum = 1;
        _numPort.Maximum = 65535;
        _numPort.Value = s.Port;
        Controls.Add(new Label { Text = "Puerto", Location = new Point(430, y + 3), AutoSize = true });
        Controls.Add(_numPort);

        y += 34;
        AddRow("Archivo BD (.FDB)", _txtDbPath, s.DatabasePath, 385, y);
        var btnBrowseFdb = new Button { Location = new Point(550, y - 1), Size = new Size(78, 27) };
        AppTheme.StyleSecondaryButton(btnBrowseFdb, AppIcon.Search, "...", "Seleccionar ruta destino .FDB", null, _tt);
        btnBrowseFdb.Click += (_, _) =>
        {
            using var sfd = new SaveFileDialog { Filter = "Firebird Database|*.fdb;*.FDB", FileName = _txtDbPath.Text };
            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                _txtDbPath.Text = sfd.FileName;
            }
        };
        Controls.Add(btnBrowseFdb);

        y += 34;
        AddRow("Usuario Firebird", _txtUser, s.UserId, 160, y);
        Controls.Add(new Label { Text = "Clave", Location = new Point(340, y + 3), AutoSize = true });
        _txtPass.Location = new Point(390, y);
        _txtPass.Width = 175;
        _txtPass.UseSystemPasswordChar = true;
        _txtPass.Text = s.Password;
        Controls.Add(_txtPass);

        y += 42;
        var sep = new GroupBox
        {
            Text = "Migración Automática desde Archivo Legacy InterBase ODS 9.1 (.GDB)",
            Location = new Point(18, y),
            Size = new Size(612, 92),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = AppTheme.NavyDark
        };
        _txtGdbLegacy.Location = new Point(16, 28);
        _txtGdbLegacy.Width = 492;
        _txtGdbLegacy.Text = @"D:\archivos qlabel\PESAJE CAMIONES\placillaPRUEBA\WMDBSPC.GDB";
        var btnPickGdb = new Button { Location = new Point(516, 26), Size = new Size(80, 27) };
        AppTheme.StyleSecondaryButton(btnPickGdb, AppIcon.Search, "Buscar", "Seleccionar archivo WMDBSPC.GDB legacy", null, _tt);
        btnPickGdb.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog { Filter = "InterBase Legacy GDB|*.gdb;*.GDB" };
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                _txtGdbLegacy.Text = ofd.FileName;
            }
        };
        _btnMigrate.Location = new Point(16, 56);
        _btnMigrate.Size = new Size(240, 28);
        AppTheme.StylePrimaryButton(_btnMigrate, AppIcon.Migrate, "Migrar GDB Legacy -> FDB", "Leer ODS 9.1 binario, sanear anomalías e importar a Firebird", _tt);
        _btnMigrate.Click += async (_, _) => await RunMigrationAsync();
        sep.Controls.AddRange([_txtGdbLegacy, btnPickGdb, _btnMigrate]);
        Controls.Add(sep);

        y += 102;
        _txtLog.Location = new Point(18, y);
        _txtLog.Size = new Size(612, 165);
        _txtLog.Multiline = true;
        _txtLog.ScrollBars = ScrollBars.Vertical;
        _txtLog.ReadOnly = true;
        _txtLog.Font = new Font("Consolas", 8.75f);
        Controls.Add(_txtLog);

        y += 178;
        _btnTest.Location = new Point(18, y);
        _btnTest.Size = new Size(140, 34);
        AppTheme.StyleSecondaryButton(_btnTest, AppIcon.Refresh, "Probar Conexión", "Verificar conectividad Firebird", null, _tt);
        _btnTest.Click += async (_, _) => await TestConnectionAsync();

        _btnInit.Location = new Point(168, y);
        _btnInit.Size = new Size(165, 34);
        AppTheme.StyleSecondaryButton(_btnInit, AppIcon.Add, "Crear BD Vacía", "Crear nueva base Firebird limpia con datos iniciales", null, _tt);
        _btnInit.Click += async (_, _) => await InitializeFreshDbAsync();

        _btnSave.Location = new Point(365, y);
        _btnSave.Size = new Size(135, 34);
        AppTheme.StylePrimaryButton(_btnSave, AppIcon.Save, "Guardar y Usar", "Aplicar configuración", _tt);
        _btnSave.Click += (_, _) =>
        {
            _services.UpdateDatabaseSettings(BuildSettingsFromInputs());
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCerrar = new Button { Location = new Point(510, y), Size = new Size(120, 34) };
        AppTheme.StyleSecondaryButton(btnCerrar, AppIcon.Cancel, "Cerrar", "Cerrar asistente", null, _tt);
        btnCerrar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange([_btnTest, _btnInit, _btnSave, btnCerrar]);
    }

    private void AddRow(string label, TextBox tb, string value, int width, int y)
    {
        Controls.Add(new Label { Text = label, Location = new Point(18, y + 3), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        tb.Location = new Point(155, y);
        tb.Width = width;
        tb.Text = value;
        Controls.Add(tb);
    }

    private DatabaseSettings BuildSettingsFromInputs() => new()
    {
        DataSource = _txtHost.Text.Trim(),
        Port = (int)_numPort.Value,
        DatabasePath = _txtDbPath.Text.Trim(),
        UserId = _txtUser.Text.Trim(),
        Password = _txtPass.Text
    };

    private async Task TestConnectionAsync()
    {
        try
        {
            DatabaseSettings s = BuildSettingsFromInputs();
            await using var conn = new FirebirdSql.Data.FirebirdClient.FbConnection(s.BuildConnectionString());
            await conn.OpenAsync();
            _txtLog.Text = $"Conexión exitosa a {s.DataSource}:{s.Port}/{s.DatabasePath} (ServerVersion={conn.ServerVersion}).";
        }
        catch (Exception ex)
        {
            _txtLog.Text = $"Error de conexión: {ex.Message}";
        }
    }

    private async Task InitializeFreshDbAsync()
    {
        try
        {
            DatabaseSettings s = BuildSettingsFromInputs();
            await FirebirdSchemaManager.EnsureDatabaseInitializedAsync(s);
            _txtLog.Text = $"Base Firebird inicializada correctamente en {s.DatabasePath}.\r\nUsuario inicial: ADMINISTRADOR / clave: ADMIN";
        }
        catch (Exception ex)
        {
            _txtLog.Text = $"Error creando base de datos: {ex.Message}";
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
        _txtLog.Text = "Leyendo estructura ODS 9.1 y migrando hacia Firebird UTF8...\r\n";
        try
        {
            MigrationAuditReport rep = await Ods9Migrator.MigrateAsync(gdb, BuildSettingsFromInputs(), hashPasswords: true, overwriteTarget: true);
            var lines = new List<string>
            {
                $"Migración completada ({rep.SourceOdsVersion} -> Firebird UTF8):",
                $"Destino: {rep.TargetDatabase}"
            };
            lines.AddRange(rep.TableRowCounts.Select(kv => $"  - {kv.Key,-15}: {kv.Value} registros"));
            if (rep.Findings.Count > 0)
            {
                lines.Add("Saneamientos aplicados:");
                lines.AddRange(rep.Findings.Select(f => $"  * {f}"));
            }
            _txtLog.Text = string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            _txtLog.Text += $"ERROR: {ex}";
        }
        finally
        {
            _btnMigrate.Enabled = true;
        }
    }
}
