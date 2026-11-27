using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Domain.Diagnostics;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;
using PesajeProCamion.Hardware.Export;

namespace PesajeProCamion.App.UI.Dialogs;

/// <summary>
/// Administración integral de usuarios de pesaje y gestión: CRUD completo, matriz RBAC,
/// plantillas rápidas de rol, restablecimiento de claves, búsqueda y menú contextual clic derecho.
/// </summary>
public sealed class UsuariosForm : Form
{
    private readonly AppServices _services;
    private readonly ToolTip _tt = new();
    private readonly TextBox _txtSearch = new();
    private readonly DataGridView _gridUsers = new();
    private readonly TextBox _txtId = new() { ReadOnly = true };
    private readonly TextBox _txtNombre = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 40 };
    private readonly TextBox _txtCargo = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 30 };
    private readonly TextBox _txtPassword = new() { UseSystemPasswordChar = true, MaxLength = 32 };
    private readonly TextBox _txtPasswordConfirm = new() { UseSystemPasswordChar = true, MaxLength = 32 };
    private readonly CheckBox _chkShowPw = new();
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
    private readonly Label _lblMode = new();

    private List<Usuario> _usuarios = [];
    private Usuario? _editing;

    public UsuariosForm(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        Text = "Administración Completa de Usuarios y Permisos";
        AppTheme.ConfigureResponsiveDialog(this, 1120, 690, 960, 620, resizable: true);
        BuildUi();
        Load += async (_, _) => await AppTheme.SafeLoadAsync(this, "Usuarios y Permisos", () => LoadAsync());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tt.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 52 };
        header.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, header.ClientRectangle);
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 16, 0),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = AppTheme.FontTitle,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Gestión Integral de Usuarios, Claves y Matriz de Permisos RBAC"
        });

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = AppTheme.SurfaceCard,
            Padding = new Padding(10, 7, 10, 6),
            WrapContents = false,
            AutoScroll = true
        };

        var btnNew = new Button { Size = new Size(138, 34) };
        AppTheme.StyleSecondaryButton(btnNew, AppIcon.Add, "Nuevo Usuario", "Crear una nueva cuenta de acceso", AppTheme.AccentEmerald, _tt);
        btnNew.Click += (_, _) => StartNewUser();

        var btnSave = new Button { Size = new Size(156, 34) };
        AppTheme.StylePrimaryButton(btnSave, AppIcon.Save, "Guardar Usuario", "Guardar datos, clave y permisos del usuario", _tt);
        btnSave.Click += async (_, _) => await SaveUserAsync();

        var btnResetPw = new Button { Size = new Size(164, 34) };
        AppTheme.StyleSecondaryButton(btnResetPw, AppIcon.Key, "Restablecer Clave", "Asignar nueva contraseña al usuario seleccionado", AppTheme.NavyPrimary, _tt);
        btnResetPw.Click += async (_, _) => await ResetPasswordPromptAsync();

        var btnToggle = new Button { Size = new Size(164, 34) };
        AppTheme.StyleSecondaryButton(btnToggle, AppIcon.Toggle, "Activar / Desact.", "Habilitar o deshabilitar acceso del usuario", AppTheme.AccentAmber, _tt);
        btnToggle.Click += async (_, _) => await ToggleActiveAsync();

        var btnDel = new Button { Size = new Size(136, 34) };
        AppTheme.StyleDangerButton(btnDel, AppIcon.Delete, "Eliminar", "Eliminar usuario seleccionado", _tt);
        btnDel.Click += async (_, _) => await DeleteSelectedAsync();

        var btnExport = new Button { Size = new Size(148, 34) };
        AppTheme.StyleSecondaryButton(btnExport, AppIcon.ExportExcel, "Exportar Excel", "Exportar listado de usuarios y permisos a .xlsx", AppTheme.AccentEmerald, _tt);
        btnExport.Click += (_, _) => ExportExcel();

        toolbar.Controls.AddRange([btnNew, btnSave, btnResetPw, btnToggle, btnDel, btnExport]);

        var footerBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = AppTheme.SurfaceCard,
            Padding = new Padding(12, 7, 14, 7)
        };
        var btnClose = new Button { Size = new Size(124, 34) };
        AppTheme.StyleSecondaryButton(btnClose, AppIcon.Cancel, "Cerrar", "Cerrar administración de usuarios");
        btnClose.Click += (_, _) => Close();
        footerBar.Controls.Add(btnClose);

        var split = new SplitContainer
        {
            Size = new Size(1060, 500),
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Panel1MinSize = 360,
            Panel2MinSize = 490,
            SplitterDistance = 500,
            BackColor = AppTheme.BorderSubtle
        };

        // Panel Izquierdo: Buscador + Grilla CRUD + Menú Clic Derecho
        var leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.SurfaceCard, Padding = new Padding(10) };
        var searchBar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 36, ColumnCount = 2, RowCount = 1 };
        searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
        searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        searchBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        searchBar.Controls.Add(new Label { Text = "Buscar:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = AppTheme.FontBodyBold }, 0, 0);
        _txtSearch.Dock = DockStyle.Fill;
        _txtSearch.PlaceholderText = "Nombre, cargo o perfil...";
        _txtSearch.TextChanged += (_, _) => RenderGrid();
        searchBar.Controls.Add(_txtSearch, 1, 0);

        AppTheme.StyleDataGrid(_gridUsers);
        _gridUsers.Dock = DockStyle.Fill;
        _gridUsers.Columns.AddRange([
            new DataGridViewTextBoxColumn { HeaderText = "ID", FillWeight = 46 },
            new DataGridViewTextBoxColumn { HeaderText = "Usuario", FillWeight = 132 },
            new DataGridViewTextBoxColumn { HeaderText = "Cargo", FillWeight = 115 },
            new DataGridViewTextBoxColumn { HeaderText = "Perfil", FillWeight = 112 },
            new DataGridViewTextBoxColumn { HeaderText = "Estado", FillWeight = 82 },
            new DataGridViewTextBoxColumn { HeaderText = "Permisos", FillWeight = 90 }
        ]);
        _gridUsers.SelectionChanged += (_, _) =>
        {
            if (_gridUsers.CurrentRow?.Tag is Usuario u)
            {
                BindUser(u);
            }
        };
        AttachUsersContextMenu();
        leftPanel.Controls.AddRange([_gridUsers, searchBar]);

        // Panel Derecho: Editor Completo Responsivo
        var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.BackgroundLight, Padding = new Padding(10), AutoScroll = true };
        var editorCard = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = AppTheme.SurfaceCard, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(14) };

        var rightLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 166f));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130f));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90f));

        _lblMode.Dock = DockStyle.Fill;
        _lblMode.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        _lblMode.ForeColor = AppTheme.NavyPrimary;
        _lblMode.TextAlign = ContentAlignment.MiddleLeft;
        _lblMode.Text = "Editando usuario existente";

        var accountGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 2, 0, 4) };
        accountGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128f));
        accountGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        accountGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));

        AddEditorRow(accountGrid, 0, "ID Usuario:", _txtId);
        AddEditorRow(accountGrid, 1, "Nombre Acceso *:", _txtNombre);
        AddEditorRow(accountGrid, 2, "Cargo / Área:", _txtCargo);
        AddEditorRow(accountGrid, 3, "Contraseña:", _txtPassword);
        AddEditorRow(accountGrid, 4, "Confirmar Clave:", _txtPasswordConfirm);

        _chkShowPw.Text = "Mostrar clave";
        _chkShowPw.AutoSize = true;
        _chkShowPw.CheckedChanged += (_, _) =>
        {
            _txtPassword.UseSystemPasswordChar = !_chkShowPw.Checked;
            _txtPasswordConfirm.UseSystemPasswordChar = !_chkShowPw.Checked;
        };
        accountGrid.Controls.Add(_chkShowPw, 2, 3);
        accountGrid.Controls.Add(new Label
        {
            Text = "(vacío = conserva)",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Anchor = AnchorStyles.Left
        }, 2, 4);

        var grpTemplates = new GroupBox
        {
            Text = "Plantillas Rápidas de Rol y Permisos",
            Dock = DockStyle.Fill,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.NavyDark
        };
        var tplFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 2), WrapContents = false };
        var btnTplAdm = new Button { Size = new Size(134, 30), Margin = new Padding(0, 1, 8, 1) };
        AppTheme.StylePrimaryButton(btnTplAdm, AppIcon.Lock, "Admin Total", "Asignar rol Administrador con todos los permisos", _tt);
        btnTplAdm.Click += (_, _) => ApplyRoleTemplate(PerfilOperacion.Administracion);

        var btnTplOp = new Button { Size = new Size(152, 30), Margin = new Padding(0, 1, 8, 1) };
        AppTheme.StyleSecondaryButton(btnTplOp, AppIcon.Scale, "Operador Pesaje", "Asignar permisos operativos de báscula", AppTheme.AccentEmerald, _tt);
        btnTplOp.Click += (_, _) => ApplyRoleTemplate(PerfilOperacion.OperacionPesaje);

        var btnTplCon = new Button { Size = new Size(134, 30), Margin = new Padding(0, 1, 0, 1) };
        AppTheme.StyleSecondaryButton(btnTplCon, AppIcon.List, "Solo Consulta", "Asignar permisos de solo lectura e impresión", AppTheme.NavyPrimary, _tt);
        btnTplCon.Click += (_, _) => ApplyRoleTemplate(PerfilOperacion.Consulta);
        tplFlow.Controls.AddRange([btnTplAdm, btnTplOp, btnTplCon]);
        grpTemplates.Controls.Add(tplFlow);

        var permsCols = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 4, 0, 4) };
        permsCols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
        permsCols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));
        permsCols.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var grpPerfil = new GroupBox { Text = "Perfil de Operación", Dock = DockStyle.Fill, Font = AppTheme.FontBodyBold, ForeColor = AppTheme.NavyDark };
        var pf = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10, 4, 8, 4) };
        pf.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pf.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        pf.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        pf.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34f));
        _rbOp.Text = "Operación de Pesaje"; _rbOp.AutoSize = true; _rbOp.Margin = Padding.Empty; _rbOp.Anchor = AnchorStyles.Left; _rbOp.Font = AppTheme.FontBody;
        _rbCon.Text = "Consulta de Datos"; _rbCon.AutoSize = true; _rbCon.Margin = Padding.Empty; _rbCon.Anchor = AnchorStyles.Left; _rbCon.Font = AppTheme.FontBody;
        _rbAdm.Text = "Administración"; _rbAdm.AutoSize = true; _rbAdm.Margin = Padding.Empty; _rbAdm.Anchor = AnchorStyles.Left; _rbAdm.Font = AppTheme.FontBody;
        pf.Controls.Add(_rbOp, 0, 0);
        pf.Controls.Add(_rbCon, 0, 1);
        pf.Controls.Add(_rbAdm, 0, 2);
        grpPerfil.Controls.Add(pf);

        var grpPriv = new GroupBox { Text = "Privilegios CRUD", Dock = DockStyle.Fill, Font = AppTheme.FontBodyBold, ForeColor = AppTheme.NavyDark };
        var pv = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(10, 6, 8, 4) };
        pv.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        pv.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _chkAdd.Text = "Agregar"; _chkAdd.AutoSize = true; _chkAdd.Font = AppTheme.FontBody;
        _chkMod.Text = "Modificar"; _chkMod.AutoSize = true; _chkMod.Font = AppTheme.FontBody;
        _chkDel.Text = "Eliminar"; _chkDel.AutoSize = true; _chkDel.Font = AppTheme.FontBody;
        _chkPrn.Text = "Imprimir"; _chkPrn.AutoSize = true; _chkPrn.Font = AppTheme.FontBody;
        _chkSup.Text = "Sup. Sistema"; _chkSup.AutoSize = true; _chkSup.Font = AppTheme.FontBody;
        _chkJefe.Text = "Jefe Turno"; _chkJefe.AutoSize = true; _chkJefe.Font = AppTheme.FontBody;
        pv.Controls.Add(_chkAdd, 0, 0); pv.Controls.Add(_chkMod, 1, 0);
        pv.Controls.Add(_chkDel, 0, 1); pv.Controls.Add(_chkPrn, 1, 1);
        pv.Controls.Add(_chkSup, 0, 2); pv.Controls.Add(_chkJefe, 1, 2);
        grpPriv.Controls.Add(pv);

        permsCols.Controls.Add(grpPerfil, 0, 0);
        permsCols.Controls.Add(grpPriv, 1, 0);

        var grpMod = new GroupBox { Text = "Acceso a Módulos y Estado de Cuenta", Dock = DockStyle.Fill, Font = AppTheme.FontBodyBold, ForeColor = AppTheme.NavyDark };
        var mv = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(10, 6, 8, 4) };
        mv.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        mv.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _chkPesaje.Text = "Acceso Módulo Pesaje"; _chkPesaje.AutoSize = true; _chkPesaje.Font = AppTheme.FontBody;
        _chkGestion.Text = "Acceso Módulo Gestión / Informes"; _chkGestion.AutoSize = true; _chkGestion.Font = AppTheme.FontBody;
        _chkActivo.Text = "Usuario Activo"; _chkActivo.AutoSize = true; _chkActivo.Font = AppTheme.FontBody;
        _chkCambioPw.Text = "Puede Cambiar Contraseña"; _chkCambioPw.AutoSize = true; _chkCambioPw.Font = AppTheme.FontBody;
        mv.Controls.Add(_chkPesaje, 0, 0); mv.Controls.Add(_chkGestion, 1, 0);
        mv.Controls.Add(_chkActivo, 0, 1); mv.Controls.Add(_chkCambioPw, 1, 1);
        grpMod.Controls.Add(mv);

        rightLayout.Controls.Add(_lblMode, 0, 0);
        rightLayout.Controls.Add(accountGrid, 0, 1);
        rightLayout.Controls.Add(grpTemplates, 0, 2);
        rightLayout.Controls.Add(permsCols, 0, 3);
        rightLayout.Controls.Add(grpMod, 0, 4);
        editorCard.Controls.Add(rightLayout);
        rightPanel.Controls.Add(editorCard);

        split.Panel1.Controls.Add(leftPanel);
        split.Panel2.Controls.Add(rightPanel);
        Controls.AddRange([split, toolbar, footerBar, header]);
    }

    private void AttachUsersContextMenu()
    {
        UniversalContextMenuService.AttachToGrid(
            _gridUsers,
            () => _services.UsuarioActual,
            customActions:
            [
                new ContextActionSpec
                {
                    Text = "Nuevo Usuario",
                    Icon = AppIcon.Add,
                    IconTint = AppTheme.AccentEmerald,
                    Group = 10,
                    RequiredPermission = AccionAutorizacion.AdministrarUsuarios,
                    ExecuteAsync = _ => { StartNewUser(); return Task.CompletedTask; }
                },
                new ContextActionSpec
                {
                    Text = "Editar Seleccionado",
                    Icon = AppIcon.EditTicket,
                    IconTint = AppTheme.NavyPrimary,
                    Group = 10,
                    RequiresRow = true,
                    RequiredPermission = AccionAutorizacion.AdministrarUsuarios,
                    IsVisible = sel => sel is Usuario,
                    ExecuteAsync = _ => { _txtNombre.Focus(); return Task.CompletedTask; }
                },
                new ContextActionSpec
                {
                    Text = "Restablecer Contraseña...",
                    Icon = AppIcon.Key,
                    IconTint = AppTheme.NavyDark,
                    Group = 10,
                    RequiresRow = true,
                    RequiredPermission = AccionAutorizacion.AdministrarUsuarios,
                    IsVisible = sel => sel is Usuario,
                    ExecuteAsync = _ => ResetPasswordPromptAsync()
                },
                new ContextActionSpec
                {
                    Text = "Activar / Desactivar Usuario",
                    Icon = AppIcon.Toggle,
                    IconTint = AppTheme.AccentAmber,
                    Group = 10,
                    RequiresRow = true,
                    RequiredPermission = AccionAutorizacion.AdministrarUsuarios,
                    IsVisible = sel => sel is Usuario,
                    ExecuteAsync = _ => ToggleActiveAsync()
                },
                new ContextActionSpec
                {
                    Text = "Aplicar Perfil: Administrador Total",
                    Icon = AppIcon.Lock,
                    IconTint = AppTheme.NavyPrimary,
                    Group = 20,
                    RequiredPermission = AccionAutorizacion.AdministrarUsuarios,
                    ExecuteAsync = _ => { ApplyRoleTemplate(PerfilOperacion.Administracion); return Task.CompletedTask; }
                },
                new ContextActionSpec
                {
                    Text = "Aplicar Perfil: Operador de Pesaje",
                    Icon = AppIcon.Scale,
                    IconTint = AppTheme.AccentEmerald,
                    Group = 20,
                    RequiredPermission = AccionAutorizacion.AdministrarUsuarios,
                    ExecuteAsync = _ => { ApplyRoleTemplate(PerfilOperacion.OperacionPesaje); return Task.CompletedTask; }
                },
                new ContextActionSpec
                {
                    Text = "Eliminar Usuario",
                    Icon = AppIcon.Delete,
                    IconTint = AppTheme.AccentDanger,
                    Group = 30,
                    RequiresRow = true,
                    RequiredPermission = AccionAutorizacion.AdministrarUsuarios,
                    IsVisible = sel => sel is Usuario u && u.Id != (_services.UsuarioActual?.Id ?? 0),
                    ExecuteAsync = _ => DeleteSelectedAsync()
                }
            ],
            exportTitle: "LISTADO DE USUARIOS Y PERMISOS RBAC",
            exportSubtitleProvider: () => $"Total usuarios: {_usuarios.Count:N0}",
            sheetName: "Usuarios",
            refreshAsync: () => LoadAsync(_editing?.Id));
    }

    private static void AddEditorRow(TableLayoutPanel tbl, int row, string label, TextBox tb)
    {
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        tbl.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.TextPrimary
        }, 0, row);
        tb.Dock = DockStyle.Fill;
        tb.Margin = new Padding(0, 4, 8, 4);
        tbl.Controls.Add(tb, 1, row);
    }

    private async Task LoadAsync(short? selectId = null)
    {
        _usuarios = await _services.Security.GetUsuariosAsync();
        RenderGrid(selectId);
    }

    private void RenderGrid(short? selectId = null)
    {
        string term = _txtSearch.Text.Trim();
        IEnumerable<Usuario> filtered = string.IsNullOrEmpty(term)
            ? _usuarios
            : _usuarios.Where(u =>
                u.Nombre.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                u.Cargo.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                PermissionService.DescribirPerfil(u.Perfil).Contains(term, StringComparison.OrdinalIgnoreCase));

        _gridUsers.Rows.Clear();
        int targetIdx = -1;
        foreach (Usuario u in filtered)
        {
            string flags = $"{(u.PuedeAgregar ? "A" : "-")}{(u.PuedeModificar ? "M" : "-")}{(u.PuedeEliminar ? "E" : "-")}{(u.PuedeImprimir ? "I" : "-")}";
            int idx = _gridUsers.Rows.Add(
                u.Id,
                u.Nombre,
                u.Cargo,
                PermissionService.DescribirPerfil(u.Perfil),
                u.Activo ? "ACTIVO" : "INACTIVO",
                flags);
            _gridUsers.Rows[idx].Tag = u;
            if (!u.Activo)
            {
                _gridUsers.Rows[idx].DefaultCellStyle.ForeColor = AppTheme.AccentDanger;
            }
            if (selectId.HasValue && u.Id == selectId.Value)
            {
                targetIdx = idx;
            }
        }

        if (_gridUsers.Rows.Count > 0)
        {
            int sel = targetIdx >= 0 ? targetIdx : 0;
            _gridUsers.CurrentCell = _gridUsers.Rows[sel].Cells[0];
            if (_gridUsers.Rows[sel].Tag is Usuario selUser)
            {
                BindUser(selUser);
            }
        }
    }

    private void StartNewUser()
    {
        _editing = new Usuario { Id = 0, Activo = true, PuedeImprimir = true, PuedeCambiarPassword = true, IngresoPesaje = true, PuedeAgregar = true };
        BindUser(_editing);
        _lblMode.Text = "Modo: Creando NUEVO usuario de acceso";
        _lblMode.ForeColor = AppTheme.AccentEmerald;
        _txtNombre.Focus();
    }

    private void BindUser(Usuario u)
    {
        _editing = u;
        _txtId.Text = u.Id > 0 ? u.Id.ToString(CultureInfo.InvariantCulture) : "(Auto)";
        _txtNombre.Text = u.Nombre;
        _txtCargo.Text = u.Cargo;
        _txtPassword.Text = string.Empty;
        _txtPasswordConfirm.Text = string.Empty;
        _lblMode.Text = u.Id > 0 ? $"Editando usuario #{u.Id}: {u.Nombre}" : "Creando NUEVO usuario de acceso";
        _lblMode.ForeColor = u.Id > 0 ? AppTheme.NavyPrimary : AppTheme.AccentEmerald;

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

    private void ApplyRoleTemplate(PerfilOperacion perfil)
    {
        _chkActivo.Checked = true;
        _chkCambioPw.Checked = true;
        switch (perfil)
        {
            case PerfilOperacion.Administracion:
                _rbAdm.Checked = true;
                _chkAdd.Checked = _chkMod.Checked = _chkDel.Checked = _chkPrn.Checked = true;
                _chkSup.Checked = _chkJefe.Checked = _chkPesaje.Checked = _chkGestion.Checked = true;
                break;

            case PerfilOperacion.OperacionPesaje:
                _rbOp.Checked = true;
                _chkAdd.Checked = true;
                _chkMod.Checked = false;
                _chkDel.Checked = false;
                _chkPrn.Checked = true;
                _chkSup.Checked = _chkJefe.Checked = false;
                _chkPesaje.Checked = true;
                _chkGestion.Checked = false;
                break;

            case PerfilOperacion.Consulta:
                _rbCon.Checked = true;
                _chkAdd.Checked = _chkMod.Checked = _chkDel.Checked = _chkSup.Checked = _chkJefe.Checked = false;
                _chkPrn.Checked = true;
                _chkPesaje.Checked = false;
                _chkGestion.Checked = true;
                break;
        }
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

        _editing ??= new Usuario();
        string pw1 = _txtPassword.Text;
        string pw2 = _txtPasswordConfirm.Text;
        if (_editing.Id == 0 && string.IsNullOrWhiteSpace(pw1))
        {
            MessageBox.Show(this, "Debe asignar una contraseña inicial para el nuevo usuario.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!string.IsNullOrEmpty(pw1) && !string.Equals(pw1, pw2, StringComparison.Ordinal))
        {
            MessageBox.Show(this, "La contraseña y su confirmación no coinciden.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
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

            short id = await _services.Security.SaveUsuarioAsync(_editing, string.IsNullOrWhiteSpace(pw1) ? null : pw1.Trim(), _services.UsuarioActual?.Id ?? 0);
            await LoadAsync(id);
            MessageBox.Show(this, "Usuario y permisos guardados correctamente.", "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo guardar el usuario:\r\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ResetPasswordPromptAsync()
    {
        if (_editing is null || _editing.Id <= 0)
        {
            MessageBox.Show(this, "Seleccione un usuario guardado en la lista primero.", "Restablecer Clave", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AdministrarUsuarios))
        {
            MessageBox.Show(this, "No está autorizado para restablecer claves.", "Seguridad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dlg = new ChangePasswordForm();
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            await _services.Security.ResetPasswordAsync(_editing.Id, dlg.NuevaPassword, _services.UsuarioActual?.Id ?? 0);
            await LoadAsync(_editing.Id);
            MessageBox.Show(this, $"Contraseña de '{_editing.Nombre}' restablecida correctamente.", "Seguridad", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task ToggleActiveAsync()
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AdministrarUsuarios))
        {
            MessageBox.Show(this, "No está autorizado para activar o desactivar usuarios.", "Seguridad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_editing is null || _editing.Id <= 0) return;
        if (_editing.Id == _services.UsuarioActual?.Id && _editing.Activo)
        {
            MessageBox.Show(this, "No puede desactivar su propio usuario mientras tiene la sesión abierta.", "Seguridad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            await _services.Security.SetUsuarioActivoAsync(_editing.Id, !_editing.Activo, _services.UsuarioActual?.Id ?? 0);
            await LoadAsync(_editing.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Seguridad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AdministrarUsuarios))
        {
            MessageBox.Show(this, "No está autorizado para eliminar usuarios.", "Seguridad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_editing is null || _editing.Id <= 0) return;
        if (_editing.Id == _services.UsuarioActual?.Id)
        {
            MessageBox.Show(this, "No puede eliminar su propio usuario con la sesión iniciada.", "Seguridad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(this, $"¿Eliminar definitivamente al usuario '{_editing.Nombre}'?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            await _services.Security.DeleteUsuarioAsync(_editing.Id, _services.UsuarioActual?.Id ?? 0);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Seguridad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ExportExcel()
    {
        try
        {
            using var sfd = new SaveFileDialog { Filter = "Excel OpenXML|*.xlsx", FileName = $"Usuarios_{DateTime.Today:yyyyMMdd}.xlsx" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            string[] headers = ["ID", "Usuario", "Cargo", "Perfil", "Activo", "Agregar", "Modificar", "Eliminar", "Imprimir", "Mod.Pesaje", "Mod.Gestión", "Sup.Sistema"];
            List<IReadOnlyList<object?>> rows = _usuarios.Select(u => (IReadOnlyList<object?>)
            [
                u.Id, u.Nombre, u.Cargo, PermissionService.DescribirPerfil(u.Perfil), u.Activo ? "SI" : "NO",
                u.PuedeAgregar ? "SI" : "NO", u.PuedeModificar ? "SI" : "NO", u.PuedeEliminar ? "SI" : "NO",
                u.PuedeImprimir ? "SI" : "NO", u.IngresoPesaje ? "SI" : "NO", u.IngresoGestion ? "SI" : "NO", u.SupervisorSistema ? "SI" : "NO"
            ]).ToList();
            DataExporter.ExportStandardReportToXlsx(sfd.FileName, new StandardTableReport
            {
                Title = "USUARIOS Y PERMISOS RBAC DEL SISTEMA",
                Subtitle = $"Total usuarios registrados: {_usuarios.Count:N0}",
                SheetName = "Usuarios",
                Headers = headers,
                Rows = rows,
                Landscape = true
            });
            MessageBox.Show(this, $"Listado exportado:\r\n{sfd.FileName}", "Exportar", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("UsuariosForm.ExportExcel", "Error exportando usuarios a Excel", ex);
            MessageBox.Show(this, $"Error exportando usuarios:\r\n{ex.Message}", "Error Excel", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
