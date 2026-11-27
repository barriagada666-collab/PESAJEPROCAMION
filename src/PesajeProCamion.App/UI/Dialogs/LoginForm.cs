using System.Drawing;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;

namespace PesajeProCamion.App.UI.Dialogs;

/// <summary>Diálogo moderno de autenticación con selección de módulo y acceso a migración/conexión BD.</summary>
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

    public int SelectedModuleId { get; private set; } = 2; // 2=Pesaje, 1=Gestión, 0=Unificado

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
        Text = "Inicio de Sesión - PesajeProCamion";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 390);
        BackColor = AppTheme.BackgroundLight;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        KeyPreview = true;

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 92,
            BackColor = AppTheme.NavyDark
        };
        var picTruck = new PictureBox
        {
            Location = new Point(18, 16),
            Size = new Size(92, 60),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = IconCatalog.RenderTruckBadge(92, 60)
        };
        var lblBrand = new Label
        {
            Text = "PESAJE PRO CAMIÓN",
            Location = new Point(122, 18),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 16f, FontStyle.Bold)
        };
        var lblSubtitle = new Label
        {
            Text = "Sistema Integral de Control de Pesaje Comercial y Ejes MOP",
            Location = new Point(124, 50),
            AutoSize = true,
            ForeColor = Color.FromArgb(190, 212, 240),
            Font = new Font("Segoe UI", 9.25f)
        };
        header.Controls.AddRange([picTruck, lblBrand, lblSubtitle]);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 18, 24, 14)
        };

        var lblUser = new Label
        {
            Text = "Usuario",
            Location = new Point(26, 20),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = AppTheme.TextPrimary
        };
        _cmbUsuarios.Location = new Point(140, 17);
        _cmbUsuarios.Width = 348;
        _cmbUsuarios.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbUsuarios.Font = new Font("Segoe UI", 10f);

        var lblPw = new Label
        {
            Text = "Contraseña",
            Location = new Point(26, 62),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = AppTheme.TextPrimary
        };
        _txtPassword.Location = new Point(140, 59);
        _txtPassword.Width = 175;
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

        _btnCambiarPw.Location = new Point(324, 57);
        _btnCambiarPw.Size = new Size(164, 30);
        AppTheme.StyleSecondaryButton(_btnCambiarPw, AppIcon.Key, "Cambiar Clave", "Cambiar contraseña del usuario actual", null, _toolTip);
        _btnCambiarPw.Click += async (_, _) => await OpenChangePasswordAsync();

        var grpModulo = new GroupBox
        {
            Text = "Módulo de Trabajo",
            Location = new Point(26, 102),
            Size = new Size(462, 62),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = AppTheme.NavyDark
        };
        _rbPesaje.Text = "Pesaje Camiones";
        _rbPesaje.Location = new Point(18, 26);
        _rbPesaje.AutoSize = true;
        _rbPesaje.Font = new Font("Segoe UI", 9.25f);
        _rbGestion.Text = "Gestión e Informes";
        _rbGestion.Location = new Point(158, 26);
        _rbGestion.AutoSize = true;
        _rbGestion.Font = new Font("Segoe UI", 9.25f);
        _rbUnificado.Text = "Suite Completa";
        _rbUnificado.Location = new Point(322, 26);
        _rbUnificado.AutoSize = true;
        _rbUnificado.Checked = true;
        _rbUnificado.Font = new Font("Segoe UI", 9.25f);
        grpModulo.Controls.AddRange([_rbPesaje, _rbGestion, _rbUnificado]);

        _lblStatus.Location = new Point(26, 174);
        _lblStatus.Size = new Size(462, 24);
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _lblStatus.Font = new Font("Segoe UI", 8.75f);

        _btnIngresar.Location = new Point(26, 212);
        _btnIngresar.Size = new Size(148, 36);
        AppTheme.StylePrimaryButton(_btnIngresar, AppIcon.Check, "Ingresar", "Iniciar sesión en el sistema", _toolTip);
        _btnIngresar.Click += async (_, _) => await AuthenticateAsync();

        _btnConexionBd.Location = new Point(184, 212);
        _btnConexionBd.Size = new Size(154, 36);
        AppTheme.StyleSecondaryButton(_btnConexionBd, AppIcon.Migrate, "Base de Datos", "Configurar servidor Firebird o migrar archivo .GDB legacy", null, _toolTip);
        _btnConexionBd.Click += async (_, _) =>
        {
            using var dlg = new DatabaseConnectionForm(_services);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                await LoadUsersAsync();
            }
        };

        _btnSalir.Location = new Point(348, 212);
        _btnSalir.Size = new Size(140, 36);
        AppTheme.StyleDangerButton(_btnSalir, AppIcon.Power, "Salir del Sistema", "Cerrar la aplicación", _toolTip);
        _btnSalir.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        AcceptButton = _btnIngresar;
        CancelButton = _btnSalir;

        body.Controls.AddRange([lblUser, _cmbUsuarios, lblPw, _txtPassword, _btnCambiarPw, grpModulo, _lblStatus, _btnIngresar, _btnConexionBd, _btnSalir]);
        Controls.AddRange([body, header]);
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            _lblStatus.Text = "Conectando a base de datos Firebird...";
            _lblStatus.ForeColor = AppTheme.NavyPrimary;
            await _services.RefreshMetadataAsync();
            _usuarios = await _services.Security.GetUsuariosAsync(soloActivos: true);
            _cmbUsuarios.DataSource = null;
            _cmbUsuarios.DataSource = _usuarios;
            _cmbUsuarios.DisplayMember = nameof(Usuario.Nombre);
            _cmbUsuarios.ValueMember = nameof(Usuario.Id);
            _lblStatus.Text = $"Conectado: {_services.BasculaActual.NombreEmpresa} ({_usuarios.Count} usuarios activos)";
            _lblStatus.ForeColor = AppTheme.AccentEmerald;
            _btnIngresar.Enabled = _usuarios.Count > 0;
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"Sin conexión a BD ({ex.Message}). Use 'Base de Datos' para configurar o migrar.";
            _lblStatus.ForeColor = AppTheme.AccentDanger;
            _btnIngresar.Enabled = false;
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
            MessageBox.Show(this, "Contraseña actualizada con cifrado PBKDF2-SHA256.", "Seguridad", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
