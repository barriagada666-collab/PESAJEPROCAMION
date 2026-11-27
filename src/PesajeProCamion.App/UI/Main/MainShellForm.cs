using System.Drawing;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Catalogs;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Management;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.App.UI.Weighing;
using PesajeProCamion.Domain.Diagnostics;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;

namespace PesajeProCamion.App.UI.Main;

/// <summary>
/// Ventana principal corporativa Petrol Teal + Logo Cambiaso, con acceso directo a
/// Configurar Servidor BD, Visor de Logs de Errores, Pesaje Comercial/MOP, Gestión e Informes.
/// </summary>
public sealed class MainShellForm : Form
{
    private readonly AppServices _services;
    private readonly int _initialModule;
    private readonly MenuStrip _menu = new();
    private readonly TabControl _tabs = new();
    private readonly PesajeWorkspaceControl _pesajeTab;
    private readonly GestionPesajesControl _gestionTab;
    private readonly ReportsWorkspaceControl _reportsTab;
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _lblConn = new();
    private readonly ToolStripStatusLabel _lblUser = new();
    private readonly ToolStripStatusLabel _lblProfile = new();
    private readonly ToolStripStatusLabel _lblCom = new();

    public MainShellForm(AppServices services, int initialModule = 0)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _initialModule = initialModule;

        _pesajeTab = new PesajeWorkspaceControl(services);
        _gestionTab = new GestionPesajesControl(services);
        _reportsTab = new ReportsWorkspaceControl(services);

        Text = "PesajeProCamion — Sistema Integral de Pesaje y Gestión";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1024, 700);
        BackColor = AppTheme.BackgroundLight;
        AppTheme.ApplyFormIcon(this);

        BuildMenu();
        BuildTabs();
        BuildStatusBar();
        ApplyRolePermissions();

        Load += async (_, _) => await AppTheme.SafeLoadAsync(this, "Inicio", InitializeShellAsync);
    }

    private void BuildMenu()
    {
        _menu.Renderer = AppTheme.CreateMenuRenderer();
        _menu.ForeColor = Color.White;
        _menu.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _menu.Padding = new Padding(8, 5, 8, 5);

        var mOps = new ToolStripMenuItem("Operación");
        mOps.DropDownItems.Add(MakeMenuItem("Módulo Pesaje Comercial", AppIcon.Scale, () => SelectTab(0)));
        mOps.DropDownItems.Add(MakeMenuItem("Pesaje por Ejes M.O.P.", AppIcon.AxleMop, async () =>
        {
            using var f = new PesajeMopForm(_services);
            f.ShowDialog(this);
            await _reportsTab.GenerateAsync();
        }));
        mOps.DropDownItems.Add(new ToolStripSeparator());
        mOps.DropDownItems.Add(MakeMenuItem("Crear Nuevo Ticket Manual...", AppIcon.Add, async () =>
        {
            if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AgregarRegistro))
            {
                MessageBox.Show(this, "No tiene permiso para crear tickets.", "Acceso Denegado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using var f = new TicketEditorDialog(_services, TicketEditorMode.CreateNew);
            if (f.ShowDialog(this) == DialogResult.OK)
            {
                await _pesajeTab.RefreshTransitoAsync();
                await _gestionTab.RefreshAsync();
            }
        }));
        mOps.DropDownItems.Add(MakeMenuItem("Administración de Pesajes", AppIcon.List, () => SelectTab(1)));
        mOps.DropDownItems.Add(MakeMenuItem("Centro de Informes KPI y Auditoría", AppIcon.Chart, () => SelectTab(2)));

        var mCat = new ToolStripMenuItem("Catálogos");
        mCat.DropDownItems.Add(MakeMenuItem("Camiones", AppIcon.Truck, () => ShowCatalog(() => new CamionesForm(_services))));
        mCat.DropDownItems.Add(MakeMenuItem("Clientes", AppIcon.Client, () => ShowCatalog(() => new ClientesForm(_services))));
        mCat.DropDownItems.Add(MakeMenuItem("Productos", AppIcon.Product, () => ShowCatalog(() => new ProductosForm(_services))));
        mCat.DropDownItems.Add(MakeMenuItem("Transportistas", AppIcon.Carrier, () => ShowCatalog(() => new TransportistasForm(_services))));
        mCat.DropDownItems.Add(MakeMenuItem("Choferes", AppIcon.Driver, () => ShowCatalog(() => new ChoferesForm(_services))));
        mCat.DropDownItems.Add(MakeMenuItem("Orígenes", AppIcon.Origin, () => ShowCatalog(() => new OrigenesForm(_services))));
        mCat.DropDownItems.Add(MakeMenuItem("Destinos", AppIcon.Destination, () => ShowCatalog(() => new DestinosForm(_services))));

        var mCfg = new ToolStripMenuItem("Configuración");
        mCfg.DropDownItems.Add(MakeMenuItem("Visor Serial / Báscula", AppIcon.SerialPort, () => OpenAdminDialog(() => new VisorSetupForm(_services), AccionAutorizacion.ConfigurarVisorBascula)));
        mCfg.DropDownItems.Add(MakeMenuItem("Pesaje Dinámico MOP", AppIcon.Settings, () => OpenAdminDialog(() => new DinamicoSetupForm(_services), AccionAutorizacion.ConfigurarPesajeDinamico)));
        mCfg.DropDownItems.Add(MakeMenuItem("Parámetros de Instalación", AppIcon.Settings, () => OpenAdminDialog(() => new InstalacionForm(_services), AccionAutorizacion.ConfigurarInstalacion)));
        mCfg.DropDownItems.Add(new ToolStripSeparator());
        mCfg.DropDownItems.Add(MakeMenuItem("Usuarios y Permisos", AppIcon.User, () => OpenAdminDialog(() => new UsuariosForm(_services), AccionAutorizacion.AdministrarUsuarios)));
        mCfg.DropDownItems.Add(MakeMenuItem("Conexión Servidor BD y Migración", AppIcon.Database, async () => await OpenDbConfigAsync()));

        // Accesos directos visibles en barra superior estilo carrusel_mockups
        var mRemoteDb = new ToolStripMenuItem("Configurar Servidor BD...")
        {
            ForeColor = Color.FromArgb(148, 224, 244),
            Image = IconCatalog.Get(AppIcon.Database, 16, AppTheme.GoldAccent)
        };
        mRemoteDb.Click += async (_, _) => await OpenDbConfigAsync();

        var mLogs = new ToolStripMenuItem("Logs de Errores")
        {
            ForeColor = AppTheme.GoldAccent,
            Image = IconCatalog.Get(AppIcon.Logs, 16, AppTheme.GoldAccent)
        };
        mLogs.Click += (_, _) =>
        {
            using var dlg = new ErrorLogViewerDialog();
            dlg.ShowDialog(this);
        };

        var mSes = new ToolStripMenuItem("Sesión");
        mSes.DropDownItems.Add(MakeMenuItem("Cambiar Mi Contraseña...", AppIcon.Key, async () => await ChangeOwnPasswordAsync()));
        mSes.DropDownItems.Add(MakeMenuItem("Cambiar Usuario", AppIcon.Lock, async () => await SwitchUserAsync()));
        mSes.DropDownItems.Add(MakeMenuItem("Visor de Logs de Errores", AppIcon.Logs, () => { using var f = new ErrorLogViewerDialog(); f.ShowDialog(this); }));
        mSes.DropDownItems.Add(MakeMenuItem("Acerca de...", AppIcon.Info, () => { using var f = new AcercaForm(_services); f.ShowDialog(this); }));
        mSes.DropDownItems.Add(new ToolStripSeparator());
        mSes.DropDownItems.Add(MakeMenuItem("Salir", AppIcon.Power, Close));

        foreach (ToolStripMenuItem top in new[] { mOps, mCat, mCfg, mSes })
        {
            top.ForeColor = Color.White;
        }
        _menu.Items.AddRange([mOps, mCat, mCfg, mSes, mRemoteDb, mLogs]);
    }

    private static ToolStripMenuItem MakeMenuItem(string text, AppIcon icon, Action onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            Image = IconCatalog.Get(icon, 16, AppTheme.NavyPrimary),
            ForeColor = AppTheme.TextPrimary
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    private void BuildTabs()
    {
        _tabs.Dock = DockStyle.Fill;
        _tabs.Font = AppTheme.FontBodyBold;

        var p1 = new TabPage("Pesaje de Camiones") { BackColor = AppTheme.BackgroundLight };
        p1.Controls.Add(_pesajeTab);
        var p2 = new TabPage("Administración de Pesajes") { BackColor = AppTheme.BackgroundLight };
        p2.Controls.Add(_gestionTab);
        var p3 = new TabPage("Informes y Auditoría") { BackColor = AppTheme.BackgroundLight };
        p3.Controls.Add(_reportsTab);

        _tabs.TabPages.AddRange([p1, p2, p3]);
        _tabs.SelectedIndexChanged += async (_, _) =>
        {
            try
            {
                if (_tabs.SelectedIndex == 0) await _pesajeTab.RefreshTransitoAsync();
                else if (_tabs.SelectedIndex == 1) await _gestionTab.RefreshAsync();
                else if (_tabs.SelectedIndex == 2) await _reportsTab.GenerateAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Error("MainShell", "Error refrescando pestaña activa.", ex);
                MessageBox.Show(this, $"Error al consultar datos:\r\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        Controls.AddRange([_tabs, _menu]);
        MainMenuStrip = _menu;
    }

    private void BuildStatusBar()
    {
        _status.BackColor = AppTheme.NavyDark;
        _status.ForeColor = Color.White;
        _lblConn.Spring = false;
        _lblConn.ForeColor = Color.FromArgb(148, 224, 244);
        _lblUser.Spring = false;
        _lblUser.ForeColor = Color.White;
        _lblProfile.Spring = false;
        _lblProfile.ForeColor = AppTheme.GoldAccent;
        _lblCom.Spring = true;
        _lblCom.TextAlign = ContentAlignment.MiddleRight;
        _lblCom.ForeColor = AppTheme.LcdDigitOn;
        _status.Items.AddRange([_lblConn, new ToolStripSeparator(), _lblUser, new ToolStripSeparator(), _lblProfile, _lblCom]);
        Controls.Add(_status);
    }

    private async Task InitializeShellAsync()
    {
        await _services.RefreshMetadataSafeAsync();
        await _services.InitializeScaleConnectionAsync();

        if (_initialModule == 1 && PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.IngresarGestion))
        {
            _tabs.SelectedIndex = 1;
        }
        else if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.IngresarPesaje) &&
                 PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.IngresarGestion))
        {
            _tabs.SelectedIndex = 1;
        }

        await _pesajeTab.RefreshTransitoAsync();
        await _gestionTab.RefreshAsync();
        await _reportsTab.GenerateAsync();
        UpdateStatusBar();
    }

    private async Task OpenDbConfigAsync()
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ConfigurarInstalacion))
        {
            MessageBox.Show(this, "El usuario actual no tiene privilegios de administración para configurar la BD.", "Acceso Denegado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dlg = new DatabaseConnectionForm(_services);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            await _services.RefreshMetadataSafeAsync();
            await _pesajeTab.RefreshTransitoAsync();
            await _gestionTab.RefreshAsync();
            UpdateStatusBar();
        }
    }

    private async Task ChangeOwnPasswordAsync()
    {
        if (_services.UsuarioActual is not { } u) return;
        if (!PermissionService.EstaAutorizado(u, AccionAutorizacion.CambiarPassword))
        {
            MessageBox.Show(this, "Su perfil no está autorizado para cambiar su contraseña.", "Seguridad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dlg = new ChangePasswordForm();
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            await _services.Security.ChangePasswordAsync(u.Id, dlg.NuevaPassword, u.Id);
            MessageBox.Show(this, "Contraseña actualizada correctamente.", "Seguridad", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ApplyRolePermissions()
    {
        Usuario? u = _services.UsuarioActual;
        bool canPesaje = PermissionService.EstaAutorizado(u, AccionAutorizacion.IngresarPesaje);
        bool canGestion = PermissionService.EstaAutorizado(u, AccionAutorizacion.IngresarGestion);
        _pesajeTab.Enabled = canPesaje;
        _gestionTab.Enabled = canGestion || u?.Perfil == PerfilOperacion.Consulta;
        UpdateStatusBar();
    }

    private void UpdateStatusBar()
    {
        Usuario? u = _services.UsuarioActual;
        _lblConn.Text = $"BD: {_services.DatabaseSettings.DataSource}:{_services.DatabaseSettings.Port} ({_services.BasculaActual.NombreEmpresa})";
        _lblUser.Text = $"Usuario: {u?.Nombre ?? "-"}";
        _lblProfile.Text = u is null ? "Perfil: -" : $"Perfil: {PermissionService.DescribirPerfil(u.Perfil)}";
        _lblCom.Text = _services.Scale.StatusDescription;
    }

    private void SelectTab(int index)
    {
        if (index >= 0 && index < _tabs.TabPages.Count)
        {
            _tabs.SelectedIndex = index;
        }
    }

    private void ShowCatalog(Func<Form> factory)
    {
        using Form dlg = factory();
        dlg.ShowDialog(this);
    }

    private void OpenAdminDialog(Func<Form> factory, AccionAutorizacion req)
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, req))
        {
            MessageBox.Show(this, "El usuario actual no tiene privilegios de administración para este módulo.", "Acceso Denegado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using Form dlg = factory();
        dlg.ShowDialog(this);
        UpdateStatusBar();
    }

    private async Task SwitchUserAsync()
    {
        if (_services.UsuarioActual is not null)
        {
            await _services.Security.RegistrarLogIngresoAsync(_services.UsuarioActual.Id, _services.SistemaActivoId, accionId: 2);
        }
        using var login = new LoginForm(_services);
        if (login.ShowDialog(this) == DialogResult.OK)
        {
            ApplyRolePermissions();
            await _pesajeTab.RefreshTransitoAsync();
            await _gestionTab.RefreshAsync();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_services.UsuarioActual is not null)
        {
            _ = _services.Security.RegistrarLogIngresoAsync(_services.UsuarioActual.Id, _services.SistemaActivoId, accionId: 2);
        }
    }
}
