using System.Drawing;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Domain.Security;

namespace PesajeProCamion.App.UI.Catalogs;

/// <summary>Base reutilizable para formularios maestros con barra estándar, navegación, RBAC y auditoría.</summary>
public abstract class CatalogCrudBaseForm<TItem> : Form
{
    private readonly AppServices _services;
    private readonly ToolTip _toolTip = new();
    private readonly ToolStrip _toolbar = new();
    private readonly Panel _formPanel = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _lblRecord = new();

    private List<TItem> _items = [];
    private int _currentIndex = -1;
    private bool _isNewRecord;

    protected CatalogCrudBaseForm(AppServices services, string title, int width = 680, int height = 430)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;

        Text = title;
        AppTheme.ConfigureResponsiveDialog(this, width + 40, height + 20, Math.Max(540, width - 80), Math.Max(260, height - 40), resizable: true);
        KeyPreview = true;

        BuildShell();
        Load += async (_, _) => await AppTheme.SafeLoadAsync(this, Text, () => ReloadAsync());
    }

    protected AppServices Services => _services;
    protected Panel FormSurface => _formPanel;
    protected ToolTip Tip => _toolTip;
    protected bool IsNewRecord => _isNewRecord;
    protected IReadOnlyList<TItem> Items => _items;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildShell()
    {
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.BackColor = AppTheme.SurfaceCard;
        _toolbar.Padding = new Padding(6, 4, 6, 4);

        ToolStripButton btnNew = AppTheme.CreateToolButton(AppIcon.Add, "Nuevo", "Crear nuevo registro", AppTheme.AccentEmerald);
        ToolStripButton btnSave = AppTheme.CreateToolButton(AppIcon.Save, "Guardar", "Guardar cambios", AppTheme.NavyPrimary);
        ToolStripButton btnCancel = AppTheme.CreateToolButton(AppIcon.Cancel, "Cancelar", "Descartar cambios");
        ToolStripButton btnDel = AppTheme.CreateToolButton(AppIcon.Delete, "Eliminar", "Eliminar registro actual", AppTheme.AccentDanger);
        ToolStripButton btnList = AppTheme.CreateToolButton(AppIcon.List, "Listar", "Buscar y listar registros");
        ToolStripButton btnFirst = AppTheme.CreateToolButton(AppIcon.First, string.Empty, "Primer registro");
        ToolStripButton btnPrev = AppTheme.CreateToolButton(AppIcon.Prev, string.Empty, "Registro anterior");
        ToolStripButton btnNext = AppTheme.CreateToolButton(AppIcon.Next, string.Empty, "Registro siguiente");
        ToolStripButton btnLast = AppTheme.CreateToolButton(AppIcon.Last, string.Empty, "Último registro");
        ToolStripButton btnClose = AppTheme.CreateToolButton(AppIcon.Power, "Cerrar", "Cerrar ventana");

        btnNew.Click += (_, _) => StartNewRecord();
        btnSave.Click += async (_, _) => await SaveCurrentAsync();
        btnCancel.Click += (_, _) => CancelEdits();
        btnDel.Click += async (_, _) => await DeleteCurrentAsync();
        btnList.Click += (_, _) => OpenListSelector();
        btnFirst.Click += (_, _) => MoveTo(0);
        btnPrev.Click += (_, _) => MoveTo(_currentIndex - 1);
        btnNext.Click += (_, _) => MoveTo(_currentIndex + 1);
        btnLast.Click += (_, _) => MoveTo(_items.Count - 1);
        btnClose.Click += (_, _) => Close();

        _toolbar.Items.AddRange([
            btnNew, btnSave, btnCancel, new ToolStripSeparator(),
            btnDel, new ToolStripSeparator(),
            btnList, new ToolStripSeparator(),
            btnFirst, btnPrev, btnNext, btnLast,
            new ToolStripSeparator(), btnClose
        ]);

        _formPanel.Dock = DockStyle.Fill;
        _formPanel.Padding = new Padding(18);
        _formPanel.AutoScroll = true;

        _status.BackColor = AppTheme.SurfaceCard;
        _lblRecord.Spring = true;
        _lblRecord.TextAlign = ContentAlignment.MiddleLeft;
        _status.Items.Add(_lblRecord);

        Controls.AddRange([_formPanel, _status, _toolbar]);
    }

    protected Label AddFieldLabel(string text, int x, int y)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(x, y + 3),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
            ForeColor = AppTheme.TextPrimary
        };
        _formPanel.Controls.Add(lbl);
        return lbl;
    }

    protected TextBox AddTextBox(int x, int y, int width, int maxLength = 40, CharacterCasing casing = CharacterCasing.Normal)
    {
        var tb = new TextBox
        {
            Location = new Point(x, y),
            Width = width,
            MaxLength = maxLength,
            CharacterCasing = casing,
            Font = new Font("Segoe UI", 9.5f)
        };
        _formPanel.Controls.Add(tb);
        return tb;
    }

    protected async Task ReloadAsync(string? selectKey = null)
    {
        _items = await FetchAllAsync();
        if (!string.IsNullOrEmpty(selectKey))
        {
            int idx = _items.FindIndex(item => string.Equals(GetItemKey(item), selectKey, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                MoveTo(idx);
                return;
            }
        }
        if (_items.Count > 0)
        {
            MoveTo(0);
        }
        else
        {
            StartNewRecord();
        }
    }

    protected void StartNewRecord()
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AgregarRegistro))
        {
            MessageBox.Show(this, "No está autorizado para agregar registros.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _isNewRecord = true;
        ClearInputs();
        SetKeyEditable(true);
        _lblRecord.Text = "Modo: Nuevo registro";
    }

    private void CancelEdits()
    {
        _isNewRecord = false;
        if (_items.Count > 0 && _currentIndex >= 0 && _currentIndex < _items.Count)
        {
            BindItem(_items[_currentIndex]);
            SetKeyEditable(false);
            UpdateStatusBar();
        }
        else
        {
            ClearInputs();
        }
    }

    private void MoveTo(int index)
    {
        if (_items.Count == 0)
        {
            _currentIndex = -1;
            ClearInputs();
            UpdateStatusBar();
            return;
        }
        _isNewRecord = false;
        _currentIndex = Math.Clamp(index, 0, _items.Count - 1);
        BindItem(_items[_currentIndex]);
        SetKeyEditable(false);
        UpdateStatusBar();
    }

    private void UpdateStatusBar()
    {
        _lblRecord.Text = _items.Count == 0
            ? "Sin registros"
            : $"Registro {_currentIndex + 1} de {_items.Count}  |  Clave: {GetItemKey(_items[_currentIndex])}";
    }

    private async Task SaveCurrentAsync()
    {
        AccionAutorizacion req = _isNewRecord ? AccionAutorizacion.AgregarRegistro : AccionAutorizacion.ModificarRegistro;
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, req))
        {
            MessageBox.Show(this, "No está autorizado para guardar cambios en este catálogo.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string? validationError = ValidateInputs();
        if (!string.IsNullOrEmpty(validationError))
        {
            MessageBox.Show(this, validationError, "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        TItem entity = BuildEntityFromInputs();
        string key = GetItemKey(entity);
        if (_isNewRecord && _items.Any(i => string.Equals(GetItemKey(i), key, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, $"Ya existe un registro con clave '{key}'.", "Registro Duplicado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await PersistEntityAsync(entity, _services.UsuarioActual?.Id ?? 0);
        _isNewRecord = false;
        await ReloadAsync(key);
        MessageBox.Show(this, "Registro guardado correctamente.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task DeleteCurrentAsync()
    {
        if (_isNewRecord || _currentIndex < 0 || _currentIndex >= _items.Count)
        {
            return;
        }
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.EliminarRegistro))
        {
            MessageBox.Show(this, "No está autorizado para eliminar registros.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        TItem current = _items[_currentIndex];
        string key = GetItemKey(current);
        if (MessageBox.Show(this, $"¿Desea eliminar el registro '{key}'?", "Confirmar Eliminación", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        await DeleteByKeyAsync(key);
        await ReloadAsync();
    }

    private void OpenListSelector()
    {
        TItem? chosen = ShowLookupDialog(_items);
        if (chosen is not null)
        {
            string key = GetItemKey(chosen);
            int idx = _items.FindIndex(i => string.Equals(GetItemKey(i), key, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                MoveTo(idx);
            }
        }
    }

    protected abstract Task<List<TItem>> FetchAllAsync();
    protected abstract Task PersistEntityAsync(TItem entity, short usuarioId);
    protected abstract Task DeleteByKeyAsync(string key);
    protected abstract string GetItemKey(TItem item);
    protected abstract void ClearInputs();
    protected abstract void BindItem(TItem item);
    protected abstract TItem BuildEntityFromInputs();
    protected abstract string? ValidateInputs();
    protected abstract void SetKeyEditable(bool editable);
    protected abstract TItem? ShowLookupDialog(IReadOnlyList<TItem> items);
}
