using System.Drawing;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Domain.Diagnostics;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;

namespace PesajeProCamion.App.UI.Dialogs;

/// <summary>
/// Diálogo corporativo responsivo de inicio de sesión con soporte para selección o ingreso manual
/// de usuario (ej. barriagada), selector de BD remota y visor de logs.
/// </summary>
public sealed class LoginForm : Form
{
    private readonly AppServices _services;
    private readonly ToolTip _toolTip = new();
    private readonly ComboBox _cmbUsuarios = new();
    private readonly TextBox _txtPassword = new();
    private readonly Button _btnTogglePw = new();
    private readonly RadioButton _rbPesaje = new();
    private readonly RadioButton _rbGestion = new();
    private readonly RadioButton _rbUnificado = new();
    private readonly Button _btnIngresar = new();
    private readonly Button _btnCambiarPw = new();
    private readonly Button _btnConexionBd = new();
    private readonly Button _btnLogs = new();
    private readonly Button _btnSalir = new();
    private readonly Label _lblRemoteBadge = new();
    private readonly Label _lblStatus = new();
    private List<Usuario> _usuarios = [];

    public LoginForm(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        InitializeUi();
        Load += async (_, _) => await LoadUsersAsync();
    }

    public int SelectedModuleId { get; private set; } = 0;

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
        AppTheme.ConfigureResponsiveDialog(this, 688, 464, 650, 440, resizable: true);
        BackColor = AppTheme.NavyDark;
        KeyPreview = true;

        var backdrop = new Panel { Dock = DockStyle.Fill, Padding = new Padding(26, 20, 26, 16) };
        backdrop.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, backdrop.ClientRectangle, includeGoldBottomAccent: false);

        var outerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));

        // Tarjeta central responsiva
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.BackgroundLight,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 0, 8)
        };

        var cardHeader = new Panel { Dock = DockStyle.Top, Height = 42 };
        cardHeader.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, cardHeader.ClientRectangle, includeGoldBottomAccent: true);
        cardHeader.Controls.Add(new Label
        {
            Text = "  Inicio de Sesión Corporativo — Acceso Autorizado",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        });

        var cardBody = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        cardBody.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 182f));
        cardBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        cardBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var leftCol = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14) };
        leftCol.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, leftCol.ClientRectangle, includeGoldBottomAccent: false);

        var logoCard = new Panel { Dock = DockStyle.Top, Height = 86, BackColor = Color.White };
        logoCard.Controls.Add(new PictureBox
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = IconCatalog.GetCorporateLogo() ?? IconCatalog.RenderTruckBadge(120, 68)
        });

        var lblAccess = new Label
        {
            Dock = DockStyle.Top,
            Height = 56,
            Text = "Pesaje Comercial\r\ny Normativa M.O.P.",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = AppTheme.GoldAccent,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9.25f, FontStyle.Bold)
        };

        _lblRemoteBadge.Dock = DockStyle.Fill;
        _lblRemoteBadge.Text = $"Servidor:\r\n{_services.DatabaseSettings.DataSource}\r\nPuerto {_services.DatabaseSettings.Port}";
        _lblRemoteBadge.TextAlign = ContentAlignment.TopCenter;
        _lblRemoteBadge.ForeColor = Color.LightSteelBlue;
        _lblRemoteBadge.BackColor = Color.Transparent;
        _lblRemoteBadge.Font = new Font("Segoe UI", 8.25f);

        leftCol.Controls.AddRange([_lblRemoteBadge, lblAccess, logoCard]);

        var rightCol = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BackgroundLight, Padding = new Padding(18, 12, 18, 12), AutoScroll = true };
        var formLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7
        };
        formLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        formLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        formLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        formLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        formLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        formLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
        formLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        formLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));

        var lblUser = new Label { Text = "Usuario (seleccione o escriba nombre)", Dock = DockStyle.Fill, Font = AppTheme.FontBodyBold, ForeColor = AppTheme.TextPrimary, TextAlign = ContentAlignment.BottomLeft };
        _cmbUsuarios.Dock = DockStyle.Fill;
        _cmbUsuarios.DropDownStyle = ComboBoxStyle.DropDown;
        _cmbUsuarios.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _cmbUsuarios.AutoCompleteSource = AutoCompleteSource.ListItems;
        _cmbUsuarios.Font = new Font("Segoe UI", 10f);

        var lblPw = new Label { Text = "Contraseña", Dock = DockStyle.Fill, Font = AppTheme.FontBodyBold, ForeColor = AppTheme.TextPrimary, TextAlign = ContentAlignment.BottomLeft };

        var pwRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        pwRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pwRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40f));
        pwRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148f));
        pwRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _txtPassword.Dock = DockStyle.Fill;
        _txtPassword.Margin = new Padding(0, 4, 6, 2);
        _txtPassword.UseSystemPasswordChar = true;
        _txtPassword.MaxLength = 32;
        _txtPassword.Font = new Font("Segoe UI", 10f);
        _txtPassword.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await AuthenticateAsync();
            }
        };

        _btnTogglePw.Dock = DockStyle.Fill;
        _btnTogglePw.Margin = new Padding(0, 2, 6, 2);
        AppTheme.StyleSecondaryButton(_btnTogglePw, AppIcon.Eye, string.Empty, "Mostrar u ocultar contraseña", AppTheme.NavyDark, _toolTip);
        _btnTogglePw.Click += (_, _) => _txtPassword.UseSystemPasswordChar = !_txtPassword.UseSystemPasswordChar;

        _btnCambiarPw.Dock = DockStyle.Fill;
        _btnCambiarPw.Margin = new Padding(0, 2, 0, 2);
        AppTheme.StyleSecondaryButton(_btnCambiarPw, AppIcon.Key, "Cambiar Clave", "Cambiar contraseña del usuario ingresado", null, _toolTip);
        _btnCambiarPw.Click += async (_, _) => await OpenChangePasswordAsync();

        pwRow.Controls.Add(_txtPassword, 0, 0);
        pwRow.Controls.Add(_btnTogglePw, 1, 0);
        pwRow.Controls.Add(_btnCambiarPw, 2, 0);

        var grpModulo = new GroupBox
        {
            Text = "Módulo de Trabajo",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 4),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = AppTheme.NavyDark
        };
        var modFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 2, 8, 2), WrapContents = false };
        _rbPesaje.Text = "Pesaje"; _rbPesaje.AutoSize = true; _rbPesaje.Font = AppTheme.FontBody; _rbPesaje.Margin = new Padding(4, 4, 14, 2);
        _rbGestion.Text = "Gestión"; _rbGestion.AutoSize = true; _rbGestion.Font = AppTheme.FontBody; _rbGestion.Margin = new Padding(4, 4, 14, 2);
        _rbUnificado.Text = "Suite Completa"; _rbUnificado.AutoSize = true; _rbUnificado.Checked = true; _rbUnificado.Font = AppTheme.FontBody; _rbUnificado.Margin = new Padding(4, 4, 4, 2);
        modFlow.Controls.AddRange([_rbPesaje, _rbGestion, _rbUnificado]);
        grpModulo.Controls.Add(modFlow);

        var btnRow1 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        _btnIngresar.Size = new Size(124, 36);
        AppTheme.StylePrimaryButton(_btnIngresar, AppIcon.Check, "Ingresar", "Autenticar e ingresar al sistema", _toolTip);
        _btnIngresar.Click += async (_, _) => await AuthenticateAsync();

        _btnConexionBd.Size = new Size(144, 36);
        _btnConexionBd.Margin = new Padding(8, 3, 8, 3);
        AppTheme.StyleSecondaryButton(_btnConexionBd, AppIcon.Database, "Servidor BD", "Configurar conexión local o remota y probar ruta UNC/TCP", null, _toolTip);
        _btnConexionBd.Click += async (_, _) =>
        {
            using var dlg = new DatabaseConnectionForm(_services);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _lblRemoteBadge.Text = $"Servidor:\r\n{_services.DatabaseSettings.DataSource}\r\nPuerto {_services.DatabaseSettings.Port}";
                await LoadUsersAsync();
            }
        };

        _btnSalir.Size = new Size(112, 36);
        AppTheme.StyleDangerButton(_btnSalir, AppIcon.Power, "Salir", "Cerrar la aplicación", _toolTip);
        _btnSalir.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        btnRow1.Controls.AddRange([_btnIngresar, _btnConexionBd, _btnSalir]);

        var btnRow2 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        _btnLogs.Size = new Size(170, 34);
        AppTheme.StyleSecondaryButton(_btnLogs, AppIcon.Logs, "Logs de Errores", "Abrir diagnóstico y registro de errores", AppTheme.AccentAmber, _toolTip);
        _btnLogs.Click += (_, _) => { using var f = new ErrorLogViewerDialog(); f.ShowDialog(this); };
        btnRow2.Controls.Add(_btnLogs);

        formLayout.Controls.Add(lblUser, 0, 0);
        formLayout.Controls.Add(_cmbUsuarios, 0, 1);
        formLayout.Controls.Add(lblPw, 0, 2);
        formLayout.Controls.Add(pwRow, 0, 3);
        formLayout.Controls.Add(grpModulo, 0, 4);
        formLayout.Controls.Add(btnRow1, 0, 5);
        formLayout.Controls.Add(btnRow2, 0, 6);
        rightCol.Controls.Add(formLayout);

        cardBody.Controls.Add(leftCol, 0, 0);
        cardBody.Controls.Add(rightCol, 1, 0);
        card.Controls.AddRange([cardBody, cardHeader]);

        var footerCard = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.SurfaceCard };
        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.Padding = new Padding(10, 0, 10, 0);
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.AutoEllipsis = true;
        _lblStatus.Font = new Font("Segoe UI", 8.75f, FontStyle.Bold);
        footerCard.Controls.Add(_lblStatus);

        outerLayout.Controls.Add(card, 0, 0);
        outerLayout.Controls.Add(footerCard, 0, 1);
        backdrop.Controls.Add(outerLayout);
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

            int barIdx = _usuarios.FindIndex(u => string.Equals(u.Nombre.Trim(), "BARRIAGADA", StringComparison.OrdinalIgnoreCase));
            if (barIdx >= 0)
            {
                _cmbUsuarios.SelectedIndex = barIdx;
            }
            else if (_usuarios.Count > 0)
            {
                _cmbUsuarios.SelectedIndex = 0;
            }

            string emp = string.IsNullOrWhiteSpace(_services.BasculaActual.NombreEmpresa) ? _services.DatabaseSettings.DatabasePath : _services.BasculaActual.NombreEmpresa;
            _lblStatus.Text = $"Conectado a {_services.DatabaseSettings.DataSource}: {emp} ({_usuarios.Count} usuarios activos)";
            _lblStatus.ForeColor = AppTheme.AccentEmerald;
            _btnIngresar.Enabled = _usuarios.Count > 0;
            _btnIngresar.BackColor = _btnIngresar.Enabled ? AppTheme.NavyPrimary : AppTheme.BorderSubtle;
        }
        catch (Exception ex)
        {
            string corrId = AppLogger.ErrorCorrelated("Login", "Fallo al cargar usuarios desde Firebird.", ex);
            _lblStatus.Text = $"Sin conexión a BD ({corrId}): {AppLogger.SanitizeText(ex.Message)} — Abra 'Servidor BD' o 'Logs de Errores'.";
            _lblStatus.ForeColor = AppTheme.AccentDanger;
            _btnIngresar.Enabled = false;
            _btnIngresar.BackColor = AppTheme.BorderSubtle;
        }
    }

    private (short? Id, string? Nombre) ResolveUserInput()
    {
        string typed = _cmbUsuarios.Text.Trim();
        if (_cmbUsuarios.SelectedItem is Usuario sel &&
            (string.IsNullOrWhiteSpace(typed) || string.Equals(sel.Nombre.Trim(), typed, StringComparison.OrdinalIgnoreCase)))
        {
            return (sel.Id, sel.Nombre);
        }

        Usuario? exact = _usuarios.FirstOrDefault(u => string.Equals(u.Nombre.Trim(), typed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return (exact.Id, exact.Nombre);
        }

        return (null, typed);
    }

    private async Task AuthenticateAsync()
    {
        (short? userId, string? userName) = ResolveUserInput();
        if (!userId.HasValue && string.IsNullOrWhiteSpace(userName))
        {
            MessageBox.Show(this, "Seleccione o escriba un usuario válido.", "Inicio de Sesión", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int modulo = _rbPesaje.Checked ? 2 : (_rbGestion.Checked ? 1 : 0);
        int sistemaLog = modulo == 0 ? 2 : modulo;

        try
        {
            Usuario? authenticated = await _services.Security.AuthenticateByLoginAsync(userId, userName, _txtPassword.Text, sistemaLog);
            if (authenticated is null)
            {
                MessageBox.Show(this, "Usuario inactivo o contraseña incorrecta.", "Acceso Denegado", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            string corrId = AppLogger.ErrorCorrelated("Login", "Error inesperado durante autenticación.", ex);
            MessageBox.Show(this, $"Error durante el inicio de sesión (Ref: {corrId}):\r\n{AppLogger.SanitizeText(ex.Message)}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OpenChangePasswordAsync()
    {
        (short? userId, string? userName) = ResolveUserInput();
        if (!userId.HasValue && string.IsNullOrWhiteSpace(userName))
        {
            return;
        }
        Usuario? auth = await _services.Security.AuthenticateByLoginAsync(userId, userName, _txtPassword.Text, 2);
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
