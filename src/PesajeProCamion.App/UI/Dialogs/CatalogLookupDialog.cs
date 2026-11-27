using System.Drawing;
using System.Windows.Forms;
using PesajeProCamion.App.UI.Theme;

namespace PesajeProCamion.App.UI.Dialogs;

/// <summary>Diálogo genérico de búsqueda rápida (F2) para cualquier catálogo maestro.</summary>
public sealed class CatalogLookupDialog<T> : Form
{
    private readonly IReadOnlyList<T> _allItems;
    private readonly Func<T, string> _searchExtractor;
    private readonly TextBox _txtSearch = new();
    private readonly DataGridView _grid = new();

    public CatalogLookupDialog(
        string title,
        IReadOnlyList<T> items,
        Func<T, string> searchExtractor,
        params (string Header, Func<T, object?> ValueSelector, int FillWeight)[] columns)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(searchExtractor);
        ArgumentNullException.ThrowIfNull(columns);

        _allItems = items;
        _searchExtractor = searchExtractor;

        Text = title;
        AppTheme.ConfigureResponsiveDialog(this, 820, 520, 680, 420, resizable: true);
        KeyPreview = true;

        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(12, 10, 12, 8), BackColor = AppTheme.SurfaceCard, ColumnCount = 4, RowCount = 1 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132f));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));

        var lbl = new Label { Text = "Buscar:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        _txtSearch.Dock = DockStyle.Fill;
        _txtSearch.Margin = new Padding(0, 4, 10, 0);
        _txtSearch.Font = new Font("Segoe UI", 10f);
        _txtSearch.TextChanged += (_, _) => ApplyFilter();

        var btnSelect = new Button { Dock = DockStyle.Fill, Margin = new Padding(4, 0, 6, 0) };
        AppTheme.StylePrimaryButton(btnSelect, AppIcon.Check, "Seleccionar", "Confirmar registro seleccionado");
        btnSelect.Click += (_, _) => ConfirmSelection();

        var btnCancel = new Button { Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0) };
        AppTheme.StyleSecondaryButton(btnCancel, AppIcon.Cancel, "Cerrar", "Cancelar búsqueda");
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        top.Controls.Add(lbl, 0, 0);
        top.Controls.Add(_txtSearch, 1, 0);
        top.Controls.Add(btnSelect, 2, 0);
        top.Controls.Add(btnCancel, 3, 0);

        AppTheme.StyleDataGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        foreach ((string hdr, Func<T, object?> _, int weight) in columns)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = hdr,
                FillWeight = weight,
                ReadOnly = true
            });
        }
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                ConfirmSelection();
            }
        };
        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ConfirmSelection();
            }
        };
        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                _grid.ClearSelection();
                _grid.Rows[e.RowIndex].Selected = true;
                _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
            }
        };

        var cms = new ContextMenuStrip();
        AppTheme.StyleContextMenu(cms);
        var mSel = new ToolStripMenuItem("Seleccionar Registro", IconCatalog.Get(AppIcon.Check, 16, AppTheme.AccentEmerald));
        mSel.Click += (_, _) => ConfirmSelection();
        var mCopyCell = new ToolStripMenuItem("Copiar Celda", IconCatalog.Get(AppIcon.Copy, 16, AppTheme.NavyPrimary));
        mCopyCell.Click += (_, _) =>
        {
            string val = _grid.CurrentCell?.Value?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(val)) Clipboard.SetText(val);
        };
        var mCopyRow = new ToolStripMenuItem("Copiar Fila Completa", IconCatalog.Get(AppIcon.Duplicate, 16, AppTheme.NavyDark));
        mCopyRow.Click += (_, _) =>
        {
            if (_grid.CurrentRow is null) return;
            string line = string.Join(" | ", _grid.CurrentRow.Cells.Cast<DataGridViewCell>().Select(c => c.Value?.ToString() ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(line)) Clipboard.SetText(line);
        };
        cms.Items.AddRange([mSel, new ToolStripSeparator(), mCopyCell, mCopyRow]);
        _grid.ContextMenuStrip = cms;

        _columnSelectors = columns.Select(c => c.ValueSelector).ToArray();
        Controls.AddRange([_grid, top]);

        CancelButton = btnCancel;
        ApplyFilter();
    }

    private readonly Func<T, object?>[] _columnSelectors;

    public T? SelectedItem { get; private set; }

    private void ApplyFilter()
    {
        string term = _txtSearch.Text.Trim();
        IEnumerable<T> filtered = string.IsNullOrEmpty(term)
            ? _allItems
            : _allItems.Where(item => _searchExtractor(item).Contains(term, StringComparison.OrdinalIgnoreCase));

        _grid.Rows.Clear();
        foreach (T item in filtered)
        {
            object[] cells = _columnSelectors.Select(sel => sel(item) ?? string.Empty).ToArray();
            int rowIdx = _grid.Rows.Add(cells);
            _grid.Rows[rowIdx].Tag = item;
        }
    }

    private void ConfirmSelection()
    {
        if (_grid.CurrentRow?.Tag is T item)
        {
            SelectedItem = item;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
