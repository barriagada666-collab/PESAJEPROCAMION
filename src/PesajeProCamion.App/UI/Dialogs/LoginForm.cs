using System.Drawing;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Domain.Diagnostics;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;

namespace PesajeProCamion.App.UI.Dialogs;

/// <summary>
/// Diálogo corporativo de inicio de sesión inspirado en carrusel_mockups + Logo Cambiaso,
/// con conexión directa a BD local/remota y visor de logs de errores.
/// </summary>
public sealed class LoginForm : Form
{
    private readonly AppServices _services;
    private readonly ToolTip _toolTip = new();
    private readonly ComboBox _cmbUsuarios = new();
    private readonly TextBox _txtPassword = new();
    private readonly RadioButton _rbPesaje = new();
    private readonly RadioButton _rbGestion = new();
    private readonly RadioButton _rbUnificado = new();
    private readonly Button _btnIngresar = new();
    private readonly Button _btnCambiarPw = new();
    private readonly Button _btnConexionBd = new();
    private readonly Button _btnLogs = new();
    private readonly Button _btnSalir = new();
    private readonly Label _lblStatus = new();
    private List<Usuario> _usuarios = [];

    public LoginForm(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        InitializeUi();
        Load += async (_, _) => await LoadUsersAsync();
    }

    public int SelectedModuleId { get; private set; } = 0; // 2=Pesaje, 1=Gestión, 0=Suite Completa

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeUi()
    {
        Text = "Inicio de Sesión — PesajeProCamion";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 420);
        BackColor = AppTheme.NavyDark;
        AppTheme.ApplyFormIcon(this);
        KeyPreview = true;

        var backdrop = new Panel { Dock = DockStyle.Fill };
        backdrop.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, backdrop.ClientRectangle, includeGoldBottomAccent: false);

        // Tarjeta central estilizada igual que mockup 02/40
        var card = new Panel
        {
            Location = new Point(36, 26),
            Size = new Size(568, 336),
            BackColor = AppTheme.BackgroundLight,
            BorderStyle = BorderStyle.FixedSingle
        };

        var cardHeader = new Panel { Dock = DockStyle.Top, Height = 40 };
        cardHeader.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, cardHeader.ClientRectangle, includeGoldBottomAccent: true);
        var lblHead = new Label
        {
            Text = "  Inicio de Sesión Corporativo — Acceso Autorizado",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        cardHeader.Controls.Add(lblHead);

        var leftCol = new Panel { Location = new Point(0, 40), Size = new Size(168, 296) };
        leftCol.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, leftCol.ClientRectangle, includeGoldBottomAccent: false);

        var logoCard = new Panel
        {
            Location = new Point(16, 26),
            Size = new Size(136, 84),
            BackColor = Color.White
        };
        var picLogo = new PictureBox
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = IconCatalog.GetCorporateLogo() ?? IconCatalog.RenderTruckBadge(120, 68)
        };
        logoCard.Controls.Add(picLogo);

        var lblAccess = new Label
        {
            Location = new Point(12, 128),
            Size = new Size(144, 52),
            Text = "Pesaje Comercial\r\ny Normativa M.O.P.",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = AppTheme.GoldAccent,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9.25f, FontStyle.Bold)
        };

        var lblRemoteBadge = new Label
        {
            Location = new Point(12, 200),
            Size = new Size(144, 72),
            Text = $"Servidor:\r\n{_services.DatabaseSettings.DataSource}\r\nPuerto {_services.DatabaseSettings.Port}",
            TextAlign = ContentAlignment.TopCenter,
            ForeColor = Color.LightSteelBlue,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.25f)
        };
        leftCol.Controls.AddRange([logoCard, lblAccess, lblRemoteBadge]);

        var rightCol = new Panel
        {
            Location = new Point(168, 40),
            Size = new Size(400, 296),
            BackColor = AppTheme.BackgroundLight
        };

        var lblUser = new Label
        {
            Text = "Usuario",
            Location = new Point(22, 16),
            AutoSize = true,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.TextPrimary
        };
        _cmbUsuarios.Location = new Point(22, 38);
        _cmbUsuarios.Width = 354;
        _cmbUsuarios.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbUsuarios.Font = new Font("Segoe UI", 10f);

        var lblPw = new Label
        {
            Text = "Contraseña",
            Location = new Point(22, 74),
            AutoSize = true,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.TextPrimary
        };
        _txtPassword.Location = new Point(22, 96);
        _txtPassword.Width = 200;
        _txtPassword.UseSystemPasswordChar = true;
        _txtPassword.MaxLength = 32;
        _txtPassword.Font = new Font("Segoe UI", 10f);
        _txtPassword.CharacterCasing = CharacterCasing.Upper;
        _txtPassword.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await AuthenticateAsync();
            }
        };

        _btnCambiarPw.Location = new Point(232, 94);
        _btnCambiarPw.Size = new Size(144, 28);
        AppTheme.StyleSecondaryButton(_btnCambiarPw, AppIcon.Key, "Cambiar Clave", "Cambiar contraseña del usuario seleccionado", null, _toolTip);
        _btnCambiarPw.Click += async (_, _) => await OpenChangePasswordAsync();

        var grpModulo = new GroupBox
        {
            Text = "Módulo de Trabajo",
            Location = new Point(22, 134),
            Size = new Size(354, 54),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = AppTheme.NavyDark
        };
        _rbPesaje.Text = "Pesaje"; _rbPesaje.Location = new Point(12, 22); _rbPesaje.AutoSize = true; _rbPesaje.Font = AppTheme.FontBody;
        _rbGestion.Text = "Gestión"; _rbGestion.Location = new Point(108, 22); _rbGestion.AutoSize = true; _rbGestion.Font = AppTheme.FontBody;
        _rbUnificado.Text = "Suite Completa"; _rbUnificado.Location = new Point(206, 22); _rbUnificado.AutoSize = true; _rbUnificado.Checked = true; _rbUnificado.Font = AppTheme.FontBody;
        grpModulo.Controls.AddRange([_rbPesaje, _rbGestion, _rbUnificado]);

        _btnIngresar.Location = new Point(22, 200);
        _btnIngresar.Size = new Size(116, 36);
        AppTheme.StylePrimaryButton(_btnIngresar, AppIcon.Check, "Ingresar", "Autenticar e ingresar al sistema", _toolTip);
        _btnIngresar.Click += async (_, _) => await AuthenticateAsync();

        _btnConexionBd.Location = new Point(146, 200);
        _btnConexionBd.Size = new Size(124, 36);
        AppTheme.StyleSecondaryButton(_btnConexionBd, AppIcon.Database, "Servidor BD", "Configurar conexión local o remota y probar ruta UNC/TCP", null, _toolTip);
        _btnConexionBd.Click += async (_, _) =>
        {
            using var dlg = new DatabaseConnectionForm(_services);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                lblRemoteBadge.Text = $"Servidor:\r\n{_services.DatabaseSettings.DataSource}\r\nPuerto {_services.DatabaseSettings.Port}";
                await LoadUsersAsync();
            }
        };

        _btnSalir.Location = new Point(278, 200);
        _btnSalir.Size = new Size(98, 36);
        AppTheme.StyleDangerButton(_btnSalir, AppIcon.Power, "Salir", "Cerrar la aplicación", _toolTip);
        _btnSalir.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        _btnLogs.Location = new Point(22, 244);
        _btnLogs.Size = new Size(156, 32);
        AppTheme.StyleSecondaryButton(_btnLogs, AppIcon.Logs, "Logs de Errores", "Abrir diagnóstico y registro de errores", AppTheme.AccentAmber, _toolTip);
        _btnLogs.Click += (_, _) => { using var f = new ErrorLogViewerDialog(); f.ShowDialog(this); };

        rightCol.Controls.AddRange([lblUser, _cmbUsuarios, lblPw, _txtPassword, _btnCambiarPw, grpModulo, _btnIngresar, _btnConexionBd, _btnSalir, _btnLogs]);
        card.Controls.AddRange([leftCol, rightCol, cardHeader]);

        var footerCard = new Panel
        {
            Location = new Point(36, 372),
            Size = new Size(568, 32),
            BackColor = AppTheme.SurfaceCard
        };
        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.Padding = new Padding(10, 0, 10, 0);
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.Font = new Font("Segoe UI", 8.75f, FontStyle.Bold);
        footerCard.Controls.Add(_lblStatus);

        backdrop.Controls.AddRange([card, footerCard]);
        Controls.Add(backdrop);

        AcceptButton = _btnIngresar;
        CancelButton = _btnSalir;
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            _lblStatus.Text = $"Conectando a {_services.DatabaseSettings.DataSource}:{_services.DatabaseSettings.Port}...";
            _lblStatus.ForeColor = AppTheme.NavyPrimary;
            await _services.RefreshMetadataAsync();
            _usuarios = await _services.Security.GetUsuariosAsync(soloActivos: true);
            _cmbUsuarios.DataSource = null;
            _cmbUsuarios.DataSource = _usuarios;
            _cmbUsuarios.DisplayMember = nameof(Usuario.Nombre);
            _cmbUsuarios.ValueMember = nameof(Usuario.Id);
            string emp = string.IsNullOrWhiteSpace(_services.BasculaActual.NombreEmpresa) ? _services.DatabaseSettings.DatabasePath : _services.BasculaActual.NombreEmpresa;
            _lblStatus.Text = $"Conectado a {_services.DatabaseSettings.DataSource}: {emp} ({_usuarios.Count} usuarios activos)";
            _lblStatus.ForeColor = AppTheme.AccentEmerald;
            _btnIngresar.Enabled = _usuarios.Count > 0;
            _btnIngresar.BackColor = _btnIngresar.Enabled ? AppTheme.NavyPrimary : AppTheme.BorderSubtle;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Login", "Fallo al cargar usuarios desde Firebird.", ex);
            _lblStatus.Text = $"Sin conexión a BD: {ex.Message} — Abra 'Servidor BD' o 'Logs de Errores'.";
            _lblStatus.ForeColor = AppTheme.AccentDanger;
            _btnIngresar.Enabled = false;
            _btnIngresar.BackColor = AppTheme.BorderSubtle;
        }
    }

    private async Task AuthenticateAsync()
    {
        if (_cmbUsuarios.SelectedItem is not Usuario selected)
        {
            MessageBox.Show(this, "Seleccione un usuario válido.", "Inicio de Sesión", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int modulo = _rbPesaje.Checked ? 2 : (_rbGestion.Checked ? 1 : 0);
        int sistemaLog = modulo == 0 ? 2 : modulo;

        try
        {
            Usuario? authenticated = await _services.Security.AuthenticateAsync(selected.Id, _txtPassword.Text, sistemaLog);
            if (authenticated is null)
            {
                MessageBox.Show(this, "Contraseña incorrecta o usuario inactivo.", "Acceso Denegado", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _txtPassword.SelectAll();
                _txtPassword.Focus();
                return;
            }

            if (modulo == 2 && !PermissionService.EstaAutorizado(authenticated, AccionAutorizacion.IngresarPesaje))
            {
                MessageBox.Show(this, "El usuario no tiene permisos para el módulo de Pesaje de Camiones.", "Acceso Restringido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (modulo == 1 && !PermissionService.EstaAutorizado(authenticated, AccionAutorizacion.IngresarGestion))
            {
                MessageBox.Show(this, "El usuario no tiene permisos para el módulo de Gestión e Informes.", "Acceso Restringido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _services.UsuarioActual = authenticated;
            _services.SistemaActivoId = sistemaLog;
            SelectedModuleId = modulo;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Login", "Error inesperado durante autenticación.", ex);
            MessageBox.Show(this, $"Error durante el inicio de sesión:\r\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OpenChangePasswordAsync()
    {
        if (_cmbUsuarios.SelectedItem is not Usuario selected)
        {
            return;
        }
        Usuario? auth = await _services.Security.AuthenticateAsync(selected.Id, _txtPassword.Text, 2);
        if (auth is null)
        {
            MessageBox.Show(this, "Ingrese primero su contraseña actual correcta.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!PermissionService.EstaAutorizado(auth, AccionAutorizacion.CambiarPassword))
        {
            MessageBox.Show(this, "Este usuario no está autorizado para cambiar su contraseña.", "Acceso Restringido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dlg = new ChangePasswordForm();
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            await _services.Security.ChangePasswordAsync(auth.Id, dlg.NuevaPassword, auth.Id);
            _txtPassword.Text = dlg.NuevaPassword;
            MessageBox.Show(this, "Contraseña actualizada correctamente.", "Seguridad", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
