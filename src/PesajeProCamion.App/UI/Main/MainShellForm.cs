using System.Drawing;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Catalogs;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Management;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.App.UI.Weighing;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;

namespace PesajeProCamion.App.UI.Main;

/// <summary>Ventana principal unificada (Pesaje, Gestión, Informes, Catálogos y Configuración con RBAC estricto).</summary>
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

        Text = "PesajeProCamion - Sistema Integral de Pesaje y Gestión";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1060, 700);
        BackColor = AppTheme.BackgroundLight;
        string icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(icoPath))
        {
            Icon = new Icon(icoPath);
        }

        BuildMenu();
        BuildTabs();
        BuildStatusBar();
        ApplyRolePermissions();

        Load += async (_, _) => await InitializeShellAsync();
    }

    private void BuildMenu()
    {
        _menu.BackColor = AppTheme.NavyDark;
        _menu.ForeColor = Color.White;
        _menu.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

        var mOps = new ToolStripMenuItem("Operación");
        mOps.DropDownItems.Add(MakeMenuItem("Módulo Pesaje Comercial", AppIcon.Scale, () => SelectTab(0)));
        mOps.DropDownItems.Add(MakeMenuItem("Pesaje por Ejes M.O.P.", AppIcon.AxleMop, async () =>
        {
            using var f = new PesajeMopForm(_services);
            f.ShowDialog(this);
            await _reportsTab.GenerateAsync();
        }));
        mOps.DropDownItems.Add(new ToolStripSeparator());
        mOps.DropDownItems.Add(MakeMenuItem("Administración de Pesajes", AppIcon.List, () => SelectTab(1)));
        mOps.DropDownItems.Add(MakeMenuItem("Centro de Informes y Auditoría", AppIcon.Audit, () => SelectTab(2)));

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
        mCfg.DropDownItems.Add(MakeMenuItem("Conexión Firebird y Migración .GDB", AppIcon.Migrate, () => OpenAdminDialog(() => new DatabaseConnectionForm(_services), AccionAutorizacion.ConfigurarInstalacion)));

        var mSes = new ToolStripMenuItem("Sesión");
        mSes.DropDownItems.Add(MakeMenuItem("Cambiar Usuario", AppIcon.Lock, async () => await SwitchUserAsync()));
        mSes.DropDownItems.Add(MakeMenuItem("Acerca de...", AppIcon.Info, () => { using var f = new AcercaForm(_services); f.ShowDialog(this); }));
        mSes.DropDownItems.Add(new ToolStripSeparator());
        mSes.DropDownItems.Add(MakeMenuItem("Salir", AppIcon.Power, Close));

        foreach (ToolStripMenuItem top in new[] { mOps, mCat, mCfg, mSes })
        {
            top.ForeColor = Color.White;
        }
        _menu.Items.AddRange([mOps, mCat, mCfg, mSes]);
    }

    private static ToolStripMenuItem MakeMenuItem(string text, AppIcon icon, Action onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            Image = IconCatalog.Get(icon, 16, AppTheme.NavyDark),
            ForeColor = AppTheme.TextPrimary
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    private void BuildTabs()
    {
        _tabs.Dock = DockStyle.Fill;
        _tabs.Font = new Font("Segoe UI", 10f, FontStyle.Bold);

        var p1 = new TabPage("Pesaje de Camiones") { BackColor = AppTheme.BackgroundLight };
        p1.Controls.Add(_pesajeTab);
        var p2 = new TabPage("Administración de Pesajes") { BackColor = AppTheme.BackgroundLight };
        p2.Controls.Add(_gestionTab);
        var p3 = new TabPage("Informes y Auditoría") { BackColor = AppTheme.BackgroundLight };
        p3.Controls.Add(_reportsTab);

        _tabs.TabPages.AddRange([p1, p2, p3]);
        _tabs.SelectedIndexChanged += async (_, _) =>
        {
            if (_tabs.SelectedIndex == 0) await _pesajeTab.RefreshTransitoAsync();
            else if (_tabs.SelectedIndex == 1) await _gestionTab.RefreshAsync();
            else if (_tabs.SelectedIndex == 2) await _reportsTab.GenerateAsync();
        };

        Controls.AddRange([_tabs, _menu]);
        MainMenuStrip = _menu;
    }

    private void BuildStatusBar()
    {
        _status.BackColor = AppTheme.SurfaceCard;
        _lblConn.Spring = false;
        _lblUser.Spring = false;
        _lblProfile.Spring = false;
        _lblCom.Spring = true;
        _lblCom.TextAlign = ContentAlignment.MiddleRight;
        _status.Items.AddRange([_lblConn, new ToolStripSeparator(), _lblUser, new ToolStripSeparator(), _lblProfile, _lblCom]);
        Controls.Add(_status);
    }

    private async Task InitializeShellAsync()
    {
        await _services.RefreshMetadataAsync();
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
        UpdateStatusBar();
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
        _lblConn.Text = $"BD: {_services.DatabaseSettings.DataSource} ({_services.BasculaActual.NombreEmpresa})";
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
