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
        Size = new Size(760, 500);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.BackgroundLight;
        KeyPreview = true;

        var top = new Panel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(12, 10, 12, 8), BackColor = AppTheme.SurfaceCard };
        var lbl = new Label { Text = "Buscar:", Location = new Point(14, 15), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        _txtSearch.Location = new Point(82, 12);
        _txtSearch.Width = 420;
        _txtSearch.Font = new Font("Segoe UI", 10f);
        _txtSearch.TextChanged += (_, _) => ApplyFilter();

        var btnSelect = new Button { Location = new Point(518, 10), Size = new Size(108, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        AppTheme.StylePrimaryButton(btnSelect, AppIcon.Check, "Seleccionar", "Confirmar registro seleccionado");
        btnSelect.Click += (_, _) => ConfirmSelection();

        var btnCancel = new Button { Location = new Point(632, 10), Size = new Size(100, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        AppTheme.StyleSecondaryButton(btnCancel, AppIcon.Cancel, "Cerrar", "Cancelar búsqueda");
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        top.Controls.AddRange([lbl, _txtSearch, btnSelect, btnCancel]);

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
