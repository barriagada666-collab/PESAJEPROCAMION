using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Domain.Diagnostics;

namespace PesajeProCamion.App.UI.Dialogs;

/// <summary>Visor diagnóstico de errores, eventos SQL, visor serial y auditoría de conexión en tiempo real.</summary>
public sealed class ErrorLogViewerDialog : Form
{
    private readonly ToolTip _tt = new();
    private readonly ComboBox _cmbLevel = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtFilter = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _txtDetail = new();

    public ErrorLogViewerDialog()
    {
        Text = "Visor de Logs y Diagnóstico de Errores";
        ClientSize = new Size(880, 560);
        MinimumSize = new Size(760, 460);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.BackgroundLight;
        AppTheme.ApplyFormIcon(this);
        BuildUi();
        Load += (_, _) => ReloadGrid();
        AppLogger.EntryLogged += OnLiveEntry;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AppLogger.EntryLogged -= OnLiveEntry;
            _tt.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 54 };
        header.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, header.ClientRectangle);
        var lblHead = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 16, 0),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = AppTheme.FontTitle,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Registro y Diagnóstico de Errores del Sistema"
        };
        header.Controls.Add(lblHead);

        var filterBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = AppTheme.SurfaceCard,
            Padding = new Padding(10, 7, 10, 6),
            WrapContents = false,
            AutoScroll = true
        };
        _cmbLevel.Items.AddRange(["Todos", "Critical", "Error", "Warn", "Sql", "Serial", "Info", "Debug"]);
        _cmbLevel.SelectedIndex = 0;
        _cmbLevel.SelectedIndexChanged += (_, _) => ReloadGrid();
        _cmbLevel.Width = 114;
        _cmbLevel.Margin = new Padding(4, 5, 10, 0);

        _txtFilter.Width = 220;
        _txtFilter.Margin = new Padding(4, 5, 10, 0);
        _txtFilter.TextChanged += (_, _) => ReloadGrid();

        var btnRefresh = new Button { Size = new Size(116, 32) };
        AppTheme.StyleSecondaryButton(btnRefresh, AppIcon.Refresh, "Actualizar", "Recargar registros", null, _tt);
        btnRefresh.Click += (_, _) => ReloadGrid();

        var btnExport = new Button { Size = new Size(146, 32) };
        AppTheme.StylePrimaryButton(btnExport, AppIcon.Save, "Exportar .LOG", "Guardar registros en archivo", _tt);
        btnExport.Click += (_, _) => ExportLogs();

        var btnClear = new Button { Size = new Size(118, 32) };
        AppTheme.StyleDangerButton(btnClear, AppIcon.Delete, "Limpiar", "Limpiar vista en memoria", _tt);
        btnClear.Click += (_, _) => { AppLogger.ClearMemory(); ReloadGrid(); };

        filterBar.Controls.AddRange([
            new Label { Text = "Nivel:", AutoSize = true, Margin = new Padding(2, 8, 2, 0), Font = AppTheme.FontBodyBold },
            _cmbLevel,
            new Label { Text = "Filtro:", AutoSize = true, Margin = new Padding(2, 8, 2, 0), Font = AppTheme.FontBodyBold },
            _txtFilter,
            btnRefresh, btnExport, btnClear
        ]);

        var split = new SplitContainer { Size = new Size(860, 420), Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 240, BackColor = AppTheme.BorderSubtle };
        AppTheme.StyleDataGrid(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.Columns.AddRange([
            new DataGridViewTextBoxColumn { HeaderText = "Fecha / Hora", FillWeight = 95 },
            new DataGridViewTextBoxColumn { HeaderText = "Nivel", FillWeight = 55 },
            new DataGridViewTextBoxColumn { HeaderText = "Módulo", FillWeight = 75 },
            new DataGridViewTextBoxColumn { HeaderText = "Mensaje", FillWeight = 320 }
        ]);
        _grid.SelectionChanged += (_, _) => ShowSelectedDetail();
        UniversalContextMenuService.AttachToGrid(
            _grid,
            () => null,
            customActions:
            [
                new ContextActionSpec
                {
                    Text = "Copiar Detalle Técnico",
                    Icon = AppIcon.Copy,
                    IconTint = AppTheme.NavyPrimary,
                    Group = 10,
                    RequiresRow = true,
                    IsVisible = _ => !string.IsNullOrWhiteSpace(_txtDetail.Text),
                    ExecuteAsync = _ => { Clipboard.SetText(_txtDetail.Text); return Task.CompletedTask; }
                },
                new ContextActionSpec
                {
                    Text = "Exportar Archivo .LOG...",
                    Icon = AppIcon.Save,
                    IconTint = AppTheme.AccentEmerald,
                    Group = 20,
                    ExecuteAsync = _ => { ExportLogs(); return Task.CompletedTask; }
                }
            ],
            exportTitle: "REGISTRO DE LOGS Y DIAGNÓSTICO DEL SISTEMA",
            sheetName: "Logs_Diagnostico",
            refreshAsync: () => { ReloadGrid(); return Task.CompletedTask; },
            allowPrint: false);
        split.Panel1.Controls.Add(_grid);

        var detailPanel = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.SurfaceCard, Padding = new Padding(8) };
        _txtDetail.Dock = DockStyle.Fill;
        _txtDetail.Multiline = true;
        _txtDetail.ReadOnly = true;
        _txtDetail.ScrollBars = ScrollBars.Both;
        _txtDetail.BackColor = AppTheme.LcdBackground;
        _txtDetail.ForeColor = Color.Gainsboro;
        _txtDetail.Font = AppTheme.FontMono;
        detailPanel.Controls.Add(_txtDetail);
        split.Panel2.Controls.Add(detailPanel);

        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 46, BackColor = AppTheme.SurfaceCard, ColumnCount = 3, RowCount = 1, Padding = new Padding(10, 6, 10, 6) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126f));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var lblFile = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.FontSmall,
            Text = $"Archivo actual: {AppLogger.CurrentLogFilePath}"
        };
        var btnFolder = new Button { Dock = DockStyle.Fill };
        AppTheme.StyleSecondaryButton(btnFolder, AppIcon.Search, "Abrir Carpeta", "Abrir carpeta de logs en Explorador", null, _tt);
        btnFolder.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppLogger.LogDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", AppLogger.LogDirectory) { UseShellExecute = true });
        };

        var btnClose = new Button { Dock = DockStyle.Fill };
        AppTheme.StyleSecondaryButton(btnClose, AppIcon.Cancel, "Cerrar", "Cerrar ventana", null, _tt);
        btnClose.Click += (_, _) => Close();
        footer.Controls.Add(lblFile, 0, 0);
        footer.Controls.Add(btnFolder, 1, 0);
        footer.Controls.Add(btnClose, 2, 0);

        Controls.AddRange([split, footer, filterBar, header]);
    }

    private void OnLiveEntry(object? sender, AppLogEntry entry)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(ReloadGrid);
    }

    private void ReloadGrid()
    {
        IReadOnlyList<AppLogEntry> all = AppLogger.GetEntries();
        string levelSel = _cmbLevel.SelectedItem?.ToString() ?? "Todos";
        string filter = _txtFilter.Text.Trim();

        _grid.Rows.Clear();
        foreach (AppLogEntry e in all)
        {
            if (!string.Equals(levelSel, "Todos", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(e.Level.ToString(), levelSel, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!string.IsNullOrEmpty(filter) &&
                !e.Source.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !e.Message.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !(e.CorrelationId?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) &&
                !(e.Detail?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                continue;
            }

            string repeatPrefix = e.RepeatCount > 1 ? $"[x{e.RepeatCount}] " : string.Empty;
            int idx = _grid.Rows.Add(
                e.Timestamp.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                e.Level.ToString().ToUpperInvariant(),
                e.Source,
                repeatPrefix + e.Message);
            _grid.Rows[idx].Tag = e;
            _grid.Rows[idx].DefaultCellStyle.ForeColor = e.Level switch
            {
                AppLogLevel.Critical => AppTheme.AccentDanger,
                AppLogLevel.Error => AppTheme.AccentDanger,
                AppLogLevel.Warn => AppTheme.AccentAmber,
                AppLogLevel.Debug => AppTheme.TextMuted,
                _ => AppTheme.TextPrimary
            };
            if (e.Level == AppLogLevel.Critical)
            {
                _grid.Rows[idx].DefaultCellStyle.Font = AppTheme.FontBodyBold;
            }
        }
        ShowSelectedDetail();
    }

    private void ShowSelectedDetail()
    {
        if (_grid.CurrentRow?.Tag is AppLogEntry e)
        {
            string corr = string.IsNullOrWhiteSpace(e.CorrelationId) ? "-" : e.CorrelationId;
            string rep = e.RepeatCount > 1 ? $" | Repeticiones: {e.RepeatCount}" : string.Empty;
            _txtDetail.Text = $"[{e.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{e.Level}] [{e.Source}] [Ref: {corr}{rep}]\r\n{e.Message}\r\n\r\n{e.Detail}";
        }
        else
        {
            _txtDetail.Text = "Seleccione una fila para ver la traza detallada del evento.";
        }
    }

    private void ExportLogs()
    {
        using var sfd = new SaveFileDialog
        {
            Filter = "Archivo de Log|*.log;*.txt",
            FileName = $"PesajeProCamion_Diagnostico_{DateTime.Now:yyyyMMdd_HHmm}.log"
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        AppLogger.ExportToFile(sfd.FileName);
        MessageBox.Show(this, $"Logs exportados correctamente:\r\n{sfd.FileName}", "Diagnóstico", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
