using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.Domain.Diagnostics;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;
using PesajeProCamion.Hardware.Export;
using PesajeProCamion.Hardware.Printing;

namespace PesajeProCamion.App.UI.Theme;

/// <summary>Descriptor declarativo de una acción contextual sensible a selección, compatibilidad y RBAC.</summary>
public sealed class ContextActionSpec
{
    public required string Text { get; init; }
    public AppIcon Icon { get; init; } = AppIcon.Check;
    public Color? IconTint { get; init; }
    public int Group { get; init; }
    public AccionAutorizacion? RequiredPermission { get; init; }
    public bool RequiresRow { get; init; }
    public Func<object?, bool>? IsVisible { get; init; }
    public Func<object?, Task>? ExecuteAsync { get; init; }
}

/// <summary>
/// Servicio compartido centralizado para estandarizar menús contextuales con clic derecho en todas las grillas,
/// formularios, resultados de búsqueda y lienzos del proyecto, garantizando compatibilidad, permisos RBAC y exportaciones uniformes.
/// </summary>
public static class UniversalContextMenuService
{
    public static ContextMenuStrip AttachToGrid(
        DataGridView grid,
        Func<Usuario?> currentUserProvider,
        IReadOnlyList<ContextActionSpec>? customActions = null,
        string? exportTitle = null,
        Func<string>? exportSubtitleProvider = null,
        string sheetName = "Datos",
        Func<Task>? refreshAsync = null,
        bool allowPrint = true,
        bool allowExports = true,
        bool allowCopy = true)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(currentUserProvider);

        grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                if (e.RowIndex >= 0 && e.RowIndex < grid.Rows.Count)
                {
                    grid.ClearSelection();
                    grid.Rows[e.RowIndex].Selected = true;
                    int colIdx = e.ColumnIndex >= 0 && e.ColumnIndex < grid.Columns.Count && grid.Columns[e.ColumnIndex].Visible
                        ? e.ColumnIndex
                        : FirstVisibleColumnIndex(grid);
                    if (colIdx >= 0)
                    {
                        grid.CurrentCell = grid.Rows[e.RowIndex].Cells[colIdx];
                    }
                }
                else
                {
                    grid.ClearSelection();
                    grid.CurrentCell = null;
                }
            }
        };

        var specs = new List<ContextActionSpec>();
        if (customActions is not null)
        {
            specs.AddRange(customActions);
        }

        if (allowCopy)
        {
            specs.Add(new ContextActionSpec
            {
                Text = "Copiar Celda Seleccionada",
                Icon = AppIcon.Copy,
                IconTint = AppTheme.NavyPrimary,
                Group = 80,
                RequiresRow = true,
                IsVisible = _ => !string.IsNullOrEmpty(grid.CurrentCell?.Value?.ToString()),
                ExecuteAsync = _ =>
                {
                    string val = grid.CurrentCell?.Value?.ToString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(val)) Clipboard.SetText(val);
                    return Task.CompletedTask;
                }
            });
            specs.Add(new ContextActionSpec
            {
                Text = "Copiar Fila Completa",
                Icon = AppIcon.Duplicate,
                IconTint = AppTheme.NavyDark,
                Group = 80,
                RequiresRow = true,
                ExecuteAsync = _ =>
                {
                    if (grid.CurrentRow is null) return Task.CompletedTask;
                    string line = string.Join(" | ",
                        grid.CurrentRow.Cells.Cast<DataGridViewCell>()
                            .Where(c => c.OwningColumn is null || c.OwningColumn.Visible)
                            .Select(c => c.Value?.ToString() ?? string.Empty));
                    if (!string.IsNullOrWhiteSpace(line)) Clipboard.SetText(line);
                    return Task.CompletedTask;
                }
            });
        }

        if (allowPrint)
        {
            specs.Add(new ContextActionSpec
            {
                Text = "Imprimir / Vista Previa Lista...",
                Icon = AppIcon.Print,
                IconTint = AppTheme.NavyPrimary,
                Group = 90,
                RequiredPermission = AccionAutorizacion.ImprimirTicket,
                IsVisible = _ => grid.Rows.Count > 0,
                ExecuteAsync = _ =>
                {
                    StandardTableReport rep = ExtractGridReport(grid, exportTitle ?? "Listado del Sistema", exportSubtitleProvider?.Invoke() ?? string.Empty, sheetName);
                    PrintStandardReportInteractive(grid, rep);
                    return Task.CompletedTask;
                }
            });
        }

        if (allowExports)
        {
            string baseTitle = exportTitle ?? "Exportación de Datos";
            specs.Add(new ContextActionSpec
            {
                Text = "Exportar Vista a PDF...",
                Icon = AppIcon.Pdf,
                IconTint = AppTheme.AccentDanger,
                Group = 90,
                IsVisible = _ => grid.Rows.Count > 0,
                ExecuteAsync = _ =>
                {
                    StandardTableReport rep = ExtractGridReport(grid, baseTitle, exportSubtitleProvider?.Invoke() ?? string.Empty, sheetName);
                    ExportReportToPdfInteractive(grid, rep);
                    return Task.CompletedTask;
                }
            });
            specs.Add(new ContextActionSpec
            {
                Text = "Exportar Vista a Excel (.xlsx)...",
                Icon = AppIcon.ExportExcel,
                IconTint = AppTheme.AccentEmerald,
                Group = 90,
                IsVisible = _ => grid.Rows.Count > 0,
                ExecuteAsync = _ =>
                {
                    StandardTableReport rep = ExtractGridReport(grid, baseTitle, exportSubtitleProvider?.Invoke() ?? string.Empty, sheetName);
                    ExportReportToXlsxInteractive(grid, rep);
                    return Task.CompletedTask;
                }
            });
            specs.Add(new ContextActionSpec
            {
                Text = "Exportar Vista a CSV...",
                Icon = AppIcon.List,
                IconTint = AppTheme.TextPrimary,
                Group = 90,
                IsVisible = _ => grid.Rows.Count > 0,
                ExecuteAsync = _ =>
                {
                    StandardTableReport rep = ExtractGridReport(grid, baseTitle, exportSubtitleProvider?.Invoke() ?? string.Empty, sheetName);
                    ExportReportToCsvInteractive(grid, rep);
                    return Task.CompletedTask;
                }
            });
        }

        if (refreshAsync is not null)
        {
            specs.Add(new ContextActionSpec
            {
                Text = "Actualizar Listado",
                Icon = AppIcon.Refresh,
                IconTint = AppTheme.NavyPrimary,
                Group = 100,
                ExecuteAsync = _ => refreshAsync()
            });
        }

        ContextMenuStrip cms = BuildMenu(
            owner: grid,
            currentUserProvider: currentUserProvider,
            selectionProvider: () => grid.CurrentRow is not null ? (grid.CurrentRow.Tag ?? grid.CurrentRow) : null,
            actions: specs);
        grid.ContextMenuStrip = cms;
        return cms;
    }

    public static ContextMenuStrip AttachToControl(
        Control surface,
        Func<Usuario?> currentUserProvider,
        IReadOnlyList<ContextActionSpec> actions,
        Func<object?>? contextProvider = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(currentUserProvider);
        ArgumentNullException.ThrowIfNull(actions);

        ContextMenuStrip cms = BuildMenu(surface, currentUserProvider, contextProvider ?? (() => surface), actions);
        surface.ContextMenuStrip = cms;
        return cms;
    }

    public static ContextMenuStrip BuildMenu(
        Control owner,
        Func<Usuario?> currentUserProvider,
        Func<object?> selectionProvider,
        IReadOnlyList<ContextActionSpec> actions)
    {
        var cms = new ContextMenuStrip();
        AppTheme.StyleContextMenu(cms);

        var itemsWithMeta = new List<(ToolStripItem Item, ContextActionSpec? Spec, int Group)>();
        int? prevGroup = null;
        foreach (ContextActionSpec spec in actions)
        {
            if (prevGroup.HasValue && prevGroup.Value != spec.Group)
            {
                var sep = new ToolStripSeparator();
                cms.Items.Add(sep);
                itemsWithMeta.Add((sep, null, spec.Group));
            }
            var mi = new ToolStripMenuItem(spec.Text, IconCatalog.Get(spec.Icon, 16, spec.IconTint ?? AppTheme.NavyPrimary));
            mi.Click += async (_, _) =>
            {
                try
                {
                    object? sel = selectionProvider();
                    if (spec.RequiresRow && sel is null) return;
                    if (spec.RequiredPermission.HasValue && !PermissionService.EstaAutorizado(currentUserProvider(), spec.RequiredPermission.Value))
                    {
                        MessageBox.Show(owner, "No posee permisos suficientes para esta operación.", "Seguridad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (spec.ExecuteAsync is not null)
                    {
                        await spec.ExecuteAsync(sel);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("ContextMenu", $"Error ejecutando '{spec.Text}': {ex.Message}", ex);
                    MessageBox.Show(owner, $"Error ejecutando acción '{spec.Text}':\r\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            cms.Items.Add(mi);
            itemsWithMeta.Add((mi, spec, spec.Group));
            prevGroup = spec.Group;
        }

        cms.Opening += (_, e) =>
        {
            Usuario? user = currentUserProvider();
            object? sel = selectionProvider();

            foreach ((ToolStripItem item, ContextActionSpec? spec, int _) in itemsWithMeta)
            {
                if (spec is null) continue;
                bool visible = true;
                if (spec.RequiresRow && sel is null) visible = false;
                if (visible && spec.RequiredPermission.HasValue && !PermissionService.EstaAutorizado(user, spec.RequiredPermission.Value)) visible = false;
                if (visible && spec.IsVisible is not null && !spec.IsVisible(sel)) visible = false;
                item.Visible = visible;
            }

            // Ocultar separadores huérfanos o consecutivos para limpieza visual
            bool anyVisibleBefore = false;
            ToolStripSeparator? pendingSep = null;
            foreach ((ToolStripItem item, ContextActionSpec? spec, int _) in itemsWithMeta)
            {
                if (item is ToolStripSeparator sep)
                {
                    sep.Visible = false;
                    if (anyVisibleBefore) pendingSep = sep;
                }
                else if (item.Visible)
                {
                    if (pendingSep is not null)
                    {
                        pendingSep.Visible = true;
                        pendingSep = null;
                    }
                    anyVisibleBefore = true;
                }
            }

            if (!anyVisibleBefore)
            {
                e.Cancel = true;
            }
        };

        return cms;
    }

    public static StandardTableReport ExtractGridReport(DataGridView grid, string title, string subtitle, string sheetName)
    {
        List<DataGridViewColumn> visCols = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
        var headers = visCols.Select(c => c.HeaderText).ToList();
        var rows = new List<IReadOnlyList<object?>>(grid.Rows.Count);
        int? folioIdx = null;
        for (int c = 0; c < headers.Count; c++)
        {
            if (headers[c].Contains("Folio", StringComparison.OrdinalIgnoreCase))
            {
                folioIdx = c;
                break;
            }
        }

        foreach (DataGridViewRow dr in grid.Rows)
        {
            if (dr.IsNewRow) continue;
            var cells = new object?[visCols.Count];
            for (int i = 0; i < visCols.Count; i++)
            {
                object? raw = dr.Cells[visCols[i].Index].Value;
                cells[i] = ParseSmartCellValue(headers[i], raw);
            }
            rows.Add(cells);
        }

        return new StandardTableReport
        {
            Title = title.ToUpperInvariant(),
            Subtitle = string.IsNullOrWhiteSpace(subtitle) ? $"Registros exportados: {rows.Count:N0} | Generado {DateTime.Now:dd/MM/yyyy HH:mm}" : subtitle,
            SheetName = sheetName,
            Headers = headers,
            Rows = rows,
            FolioColumnIndex = folioIdx,
            Landscape = headers.Count >= 6
        };
    }

    public static void ExportReportToPdfInteractive(IWin32Window owner, StandardTableReport report, string? defaultFileName = null)
    {
        try
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "Documento PDF|*.pdf",
                FileName = defaultFileName ?? $"{SanitizeFilePart(report.SheetName)}_{DateTime.Today:yyyyMMdd}.pdf"
            };
            if (sfd.ShowDialog(owner) != DialogResult.OK) return;
            DataExporter.ExportStandardReportToPdf(sfd.FileName, report);
            MessageBox.Show(owner, $"Informe PDF exportado correctamente:\r\n{sfd.FileName}", "Exportar PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("ExportPDF", $"Error exportando PDF interactivo: {ex.Message}", ex);
            MessageBox.Show(owner, $"Error exportando a PDF:\r\n{ex.Message}", "Error PDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public static void ExportReportToXlsxInteractive(IWin32Window owner, StandardTableReport report, string? defaultFileName = null)
    {
        try
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "Excel OpenXML|*.xlsx",
                FileName = defaultFileName ?? $"{SanitizeFilePart(report.SheetName)}_{DateTime.Today:yyyyMMdd}.xlsx"
            };
            if (sfd.ShowDialog(owner) != DialogResult.OK) return;
            DataExporter.ExportStandardReportToXlsx(sfd.FileName, report);
            MessageBox.Show(owner, $"Informe Excel (.xlsx) exportado correctamente:\r\n{sfd.FileName}", "Exportar Excel", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("ExportXLSX", $"Error exportando XLSX interactivo: {ex.Message}", ex);
            MessageBox.Show(owner, $"Error exportando a Excel:\r\n{ex.Message}", "Error Excel", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public static void ExportReportToCsvInteractive(IWin32Window owner, StandardTableReport report, string? defaultFileName = null)
    {
        try
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "CSV UTF-8|*.csv",
                FileName = defaultFileName ?? $"{SanitizeFilePart(report.SheetName)}_{DateTime.Today:yyyyMMdd}.csv"
            };
            if (sfd.ShowDialog(owner) != DialogResult.OK) return;
            var strRows = report.Rows.Select(r => (IReadOnlyList<string?>)r.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)).ToList()).ToList();
            DataExporter.ExportToCsv(sfd.FileName, report.Headers, strRows);
            MessageBox.Show(owner, $"Informe CSV exportado correctamente:\r\n{sfd.FileName}", "Exportar CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("ExportCSV", $"Error exportando CSV interactivo: {ex.Message}", ex);
            MessageBox.Show(owner, $"Error exportando a CSV:\r\n{ex.Message}", "Error CSV", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public static void PrintStandardReportInteractive(IWin32Window owner, StandardTableReport report)
    {
        try
        {
            using PrintDocument doc = TicketPrinterService.CreateStandardReportDocument(report);
            using var prev = new PrintPreviewDialog { Document = doc, Width = 980, Height = 700 };
            prev.ShowDialog(owner);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Print", $"Error imprimiendo informe: {ex.Message}", ex);
            MessageBox.Show(owner, $"Error en impresión / vista previa:\r\n{ex.Message}", "Error Impresión", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static object? ParseSmartCellValue(string header, object? raw)
    {
        if (raw is null) return string.Empty;
        if (raw is int or long or short or double or decimal) return raw;
        string s = raw.ToString()?.Trim() ?? string.Empty;
        if (header.Contains("(kg)", StringComparison.OrdinalIgnoreCase) ||
            header.Contains("Folio", StringComparison.OrdinalIgnoreCase) ||
            header.Contains("Fardos", StringComparison.OrdinalIgnoreCase) ||
            header.Contains("Tickets", StringComparison.OrdinalIgnoreCase) ||
            header.Equals("ID", StringComparison.OrdinalIgnoreCase) ||
            header.Equals("Bruto", StringComparison.OrdinalIgnoreCase) ||
            header.Equals("Tara", StringComparison.OrdinalIgnoreCase) ||
            header.Equals("Neto", StringComparison.OrdinalIgnoreCase))
        {
            string digitsOnly = s.Replace(".", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal);
            if (int.TryParse(digitsOnly, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
            {
                return iv;
            }
        }
        return s;
    }

    private static int FirstVisibleColumnIndex(DataGridView grid)
    {
        for (int i = 0; i < grid.Columns.Count; i++)
        {
            if (grid.Columns[i].Visible) return i;
        }
        return -1;
    }

    private static string SanitizeFilePart(string raw)
    {
        string s = string.IsNullOrWhiteSpace(raw) ? "Informe" : raw.Trim();
        foreach (char bad in Path.GetInvalidFileNameChars()) s = s.Replace(bad, '_');
        return s.Replace(' ', '_');
    }
}
