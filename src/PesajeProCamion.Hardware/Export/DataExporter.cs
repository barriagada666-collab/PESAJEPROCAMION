using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using PesajeProCamion.Domain.Diagnostics;
using PesajeProCamion.Domain.Entities;

namespace PesajeProCamion.Hardware.Export;

/// <summary>
/// Exportación estandarizada y autoajustable a página completa:
/// - Excel OpenXML (.xlsx) con estilos corporativos, auto-ancho proporcional, Print_Titles repetidos, AutoFilter y fitToPage=1
/// - PDF 1.4 vectorial A4 multipágina / ticket página completa con mismo diseño, totales y jerarquía visual
/// - CSV UTF-8 BOM y dBASE III (.dbf)
/// </summary>
public static class DataExporter
{
    public static void ExportToPdf(
        string filePath,
        string title,
        string subtitle,
        IReadOnlyList<(string Label, string Value)> kpis,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(kpis);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        PdfDocumentWriter.WriteTableReportPdf(filePath, title, subtitle, kpis, headers, rows);
    }

    public static void ExportStandardReportToPdf(string filePath, StandardTableReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(report);
        (IReadOnlySet<int> boldCols, IReadOnlySet<int> largerBoldCols) = DetectWeightEmphasisColumns(report.Headers, report.BoldHighlightColumnIndices);
        PdfDocumentWriter.WriteTableReportPdf(
            filePath,
            report.Title,
            report.Subtitle,
            report.Kpis,
            report.Headers,
            report.Rows,
            report.TotalsRow,
            report.FolioColumnIndex,
            boldCols,
            largerBoldCols);
    }

    public static void ExportStandardReportToXlsx(string filePath, StandardTableReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(report);

        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            string safeName = SanitizeSheetName(report.SheetName, "Informe");
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

            var colCount = Math.Max(1, report.Headers.Count);
            double[] colWidths = ComputeExcelColumnWidths(report.Headers, report.Rows, report.TotalsRow);

            var sb = new StringBuilder();
            sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
            sb.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
            sb.Append("""<sheetPr><pageSetUpPr fitToPage="1"/></sheetPr>""");

            var merges = new List<string>();
            int r = 1;
            var rowsXml = new StringBuilder();

            // 1. Título y subtítulo equivalentes al PDF e impresión
            string lastCol = ColumnLetters(colCount - 1);
            AppendStyledCustomRow(rowsXml, r, 26, [(0, report.Title, 1), .. Enumerable.Range(1, colCount - 1).Select(c => (c, string.Empty, 1))]);
            if (colCount > 1) merges.Add($"A{r}:{lastCol}{r}");
            r++;

            if (!string.IsNullOrWhiteSpace(report.Subtitle))
            {
                AppendStyledCustomRow(rowsXml, r, 20, [(0, report.Subtitle, 2), .. Enumerable.Range(1, colCount - 1).Select(c => (c, string.Empty, 2))]);
                if (colCount > 1) merges.Add($"A{r}:{lastCol}{r}");
                r++;
            }

            if (report.Kpis.Count > 0)
            {
                string kpiBanner = string.Join("   |   ", report.Kpis.Select(k => $"{k.Label}: {k.Value}"));
                AppendStyledCustomRow(rowsXml, r, 21, [(0, kpiBanner, 5), .. Enumerable.Range(1, colCount - 1).Select(c => (c, string.Empty, 5))]);
                if (colCount > 1) merges.Add($"A{r}:{lastCol}{r}");
                r++;
            }

            // 2. Fila de encabezados repetible al imprimir + AutoFilter
            int headerRow = r;
            AppendStyledRow(rowsXml, headerRow, 24.0, report.Headers.Cast<object?>().ToList(), isHeader: true, alt: false);
            r++;

            (IReadOnlySet<int> boldCols, IReadOnlySet<int> largerBoldCols) = DetectWeightEmphasisColumns(report.Headers, report.BoldHighlightColumnIndices);

            for (int i = 0; i < report.Rows.Count; i++)
            {
                AppendStandardReportDataRow(rowsXml, r, 19.5, report.Rows[i], colCount, alt: i % 2 == 1, isTotalsRow: false, report.FolioColumnIndex, boldCols, largerBoldCols);
                r++;
            }

            int lastDataRow = Math.Max(headerRow, r - 1);
            if (report.TotalsRow is not null)
            {
                AppendStandardReportDataRow(rowsXml, r, 23.0, report.TotalsRow, colCount, alt: false, isTotalsRow: true, folioColIdx: null, boldCols, largerBoldCols);
            }

            sb.Append(CultureInfo.InvariantCulture, $"<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"{headerRow}\" topLeftCell=\"A{headerRow + 1}\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            sb.Append("<cols>");
            for (int c = 0; c < colWidths.Length; c++)
            {
                sb.Append(CultureInfo.InvariantCulture, $"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{colWidths[c]:F1}\" bestFit=\"1\" customWidth=\"1\"/>");
            }
            sb.Append("</cols><sheetData>");
            sb.Append(rowsXml);
            sb.Append("</sheetData>");
            if (merges.Count > 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $"<mergeCells count=\"{merges.Count}\">");
                foreach (string m in merges) sb.Append(CultureInfo.InvariantCulture, $"<mergeCell ref=\"{m}\"/>");
                sb.Append("</mergeCells>");
            }
            sb.Append(CultureInfo.InvariantCulture, $"<autoFilter ref=\"A{headerRow}:{lastCol}{lastDataRow}\"/>");
            sb.Append("""<pageMargins left="0.25" right="0.25" top="0.35" bottom="0.35" header="0.2" footer="0.2"/>""");
            sb.Append(CultureInfo.InvariantCulture, $"<pageSetup paperSize=\"9\" orientation=\"{(report.Landscape ? "landscape" : "portrait")}\" fitToWidth=\"1\" fitToHeight=\"0\"/>");
            sb.Append("</worksheet>");

            string escapedSheet = SecurityElement.Escape(safeName) ?? safeName;
            string definedNames = $"<definedNames><definedName name=\"_xlnm.Print_Titles\" localSheetId=\"0\">'{escapedSheet}'!${headerRow}:${headerRow}</definedName></definedNames>";
            WriteXlsxPackageScaffold(zip, [safeName], definedNames);
            WriteZipEntry(zip, "xl/worksheets/sheet1.xml", sb.ToString());
        }
        catch (Exception ex)
        {
            AppLogger.Error("ExportXLSX", $"Error generando informe XLSX '{filePath}': {ex.Message}", ex);
            throw;
        }
    }

    public static void ExportAnalyticsWorkbookToXlsx(
        string filePath,
        ReportFilterCriteria filter,
        AnalyticsResult analytics,
        IReadOnlyList<Pesaje> rows,
        bool includeFardos = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(analytics);
        ArgumentNullException.ThrowIfNull(rows);

        var sheets = new List<(string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows)>
        {
            (
                "Resumen_KPI",
                ["Indicador KPI", "Valor", "Observación"],
                [
                    ["Rango Fechas", $"{filter.Desde?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Inicio"} al {filter.Hasta?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Hoy"}", $"Estado: {filter.Estado}"],
                    ["Filtro Patente", string.IsNullOrWhiteSpace(filter.Patente) ? "(Todas)" : filter.Patente, "Segmentación por vehículo"],
                    ["Filtro Proveedor/Origen", string.IsNullOrWhiteSpace(filter.ProveedorOrigen) ? "(Todos)" : filter.ProveedorOrigen, "Segmentación origen / proveedor"],
                    ["Filtro Cliente", string.IsNullOrWhiteSpace(filter.Cliente) ? "(Todos)" : filter.Cliente, "Segmentación por cliente"],
                    ["Filtro Producto", string.IsNullOrWhiteSpace(filter.Producto) ? "(Todos)" : filter.Producto, "Segmentación por material"],
                    ["Total Tickets Filtrados", analytics.Kpis.TotalTickets, $"Activos: {analytics.Kpis.TicketsActivos} | Anulados: {analytics.Kpis.TicketsAnulados}"],
                    ["Total Fardos", analytics.Kpis.TotalFardos, "Cantidad numérica acumulada"],
                    ["Total Bruto (kg)", analytics.Kpis.TotalBrutoKg, "Acumulado bruto activo"],
                    ["Total Tara (kg)", analytics.Kpis.TotalTaraKg, "Acumulado tara activa"],
                    ["Total Neto (kg)", analytics.Kpis.TotalNetoKg, $"{analytics.Kpis.ToneladasNetas:N2} toneladas"],
                    ["Promedio Neto / Ticket (kg)", analytics.Kpis.PromedioNetoKg, $"Máx: {analytics.Kpis.MaximoNetoKg:N0} kg | Mín: {analytics.Kpis.MinimoNetoKg:N0} kg"],
                    ["Top Patente", analytics.Kpis.TopPatente, "Mayor volumen neto"],
                    ["Top Cliente", analytics.Kpis.TopCliente, "Mayor volumen neto"],
                    ["Top Producto", analytics.Kpis.TopProducto, "Mayor volumen neto"],
                    ["Top Proveedor / Origen", analytics.Kpis.TopProveedorOrigen, "Mayor volumen neto"]
                ]
            ),
            BuildDetalleSheet(rows, includeFardos),
            BuildSegmentSheet("Por_Cliente", "Cliente", analytics.PorCliente),
            BuildSegmentSheet("Por_Proveedor_Origen", "Proveedor / Origen", analytics.PorProveedorOrigen),
            BuildSegmentSheet("Por_Producto", "Producto", analytics.PorProducto),
            BuildSegmentSheet("Por_Patente", "Patente", analytics.PorPatente)
        };

        ExportMultiSheetXlsx(filePath, sheets);
    }

    public static void ExportSingleTicketToXlsx(
        string filePath,
        string companyName,
        string companySub,
        string ticketTitle,
        string tipoBadge,
        string folioText,
        string? estadoText,
        IReadOnlyList<(string Label, string Value)> details,
        int brutoKg,
        int taraKg,
        int netoKg,
        bool isEnProceso,
        bool showTimes,
        string entradaFechaHora,
        string entradaOperador,
        string salidaFechaHora,
        string salidaOperador,
        bool showObs,
        string observaciones)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(details);

        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

            WriteXlsxPackageScaffold(zip, ["Ticket_Pesaje"]);

            var sb = new StringBuilder();
            sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
            sb.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
            sb.Append("""<sheetPr><pageSetUpPr fitToPage="1"/></sheetPr>""");
            sb.Append("""<cols>""");
            sb.Append("""<col min="1" max="1" width="24" customWidth="1"/>""");
            sb.Append("""<col min="2" max="2" width="18" customWidth="1"/>""");
            sb.Append("""<col min="3" max="3" width="18" customWidth="1"/>""");
            sb.Append("""<col min="4" max="4" width="20" customWidth="1"/>""");
            sb.Append("""<col min="5" max="5" width="20" customWidth="1"/>""");
            sb.Append("""<col min="6" max="6" width="22" customWidth="1"/>""");
            sb.Append("""</cols><sheetData>""");

            var merges = new List<string>();
            int r = 1;

            // 1. Encabezado corporativo con Tipo de Pesaje sin recorte
            AppendStyledCustomRow(sb, r, 28, [(0, ticketTitle, 1), (1, string.Empty, 1), (2, string.Empty, 1), (3, tipoBadge, 3), (4, string.Empty, 3), (5, string.Empty, 3)]);
            merges.Add($"A{r}:C{r}");
            merges.Add($"D{r}:F{r}");
            r++;

            AppendStyledCustomRow(sb, r, 21, [(0, companyName, 2), (1, string.Empty, 2), (2, string.Empty, 2), (3, string.Empty, 2), (4, string.Empty, 2), (5, string.Empty, 2)]);
            merges.Add($"A{r}:F{r}");
            r++;

            AppendStyledCustomRow(sb, r, 19, [(0, companySub, 2), (1, string.Empty, 2), (2, string.Empty, 2), (3, string.Empty, 2), (4, string.Empty, 2), (5, string.Empty, 2)]);
            merges.Add($"A{r}:F{r}");
            r++;

            // 2. Franja N° Folio: xxxxxx (Negrita) + Estado opcional
            string rightFolioInfo = !string.IsNullOrWhiteSpace(estadoText) ? $"Estado: {estadoText}" : tipoBadge;
            AppendStyledCustomRow(sb, r, 28, [(0, folioText, 11), (1, string.Empty, 11), (2, string.Empty, 11), (3, rightFolioInfo, 12), (4, string.Empty, 12), (5, string.Empty, 12)]);
            merges.Add($"A{r}:C{r}");
            merges.Add($"D{r}:F{r}");
            r++;

            // 3. Campos Visibles
            double rowHt = details.Count <= 6 ? 26.0 : 22.0;
            for (int i = 0; i < details.Count; i++)
            {
                int valStyle = i % 2 == 0 ? 6 : 7;
                AppendStyledCustomRow(sb, r, rowHt, [
                    (0, details[i].Label + ":", 5),
                    (1, details[i].Value, valStyle), (2, string.Empty, valStyle), (3, string.Empty, valStyle), (4, string.Empty, valStyle), (5, string.Empty, valStyle)
                ]);
                merges.Add($"B{r}:F{r}");
                r++;
            }

            AppendStyledCustomRow(sb, r++, 10, []);

            // 4. Bloque Pesos: Bruto y Tara grandes en Negrita, NETO aún más grande en Negrita
            AppendStyledCustomRow(sb, r, 22, [
                (0, "PESO BRUTO", 13), (1, string.Empty, 13),
                (2, "PESO TARA", 13), (3, string.Empty, 13),
                (4, "PESO NETO", 13), (5, string.Empty, 13)
            ]);
            merges.Add($"A{r}:B{r}");
            merges.Add($"C{r}:D{r}");
            merges.Add($"E{r}:F{r}");
            r++;

            AppendStyledCustomRow(sb, r, 38, [
                (0, $"{brutoKg:N0} kg", 14), (1, string.Empty, 14),
                (2, $"{taraKg:N0} kg", 14), (3, string.Empty, 14),
                (4, isEnProceso ? "PENDIENTE" : $"{netoKg:N0} kg", 15), (5, string.Empty, 15)
            ]);
            merges.Add($"A{r}:B{r}");
            merges.Add($"C{r}:D{r}");
            merges.Add($"E{r}:F{r}");
            r++;

            AppendStyledCustomRow(sb, r++, 10, []);

            if (showTimes)
            {
                AppendStyledCustomRow(sb, r, 21, [
                    (0, "ENTRADA A PLANTA", 4), (1, string.Empty, 4), (2, string.Empty, 4),
                    (3, "SALIDA DE PLANTA", 4), (4, string.Empty, 4), (5, string.Empty, 4)
                ]);
                merges.Add($"A{r}:C{r}");
                merges.Add($"D{r}:F{r}");
                r++;

                AppendStyledCustomRow(sb, r, 21, [
                    (0, $"Fecha/Hora: {entradaFechaHora}", 6), (1, string.Empty, 6), (2, string.Empty, 6),
                    (3, $"Fecha/Hora: {salidaFechaHora}", 6), (4, string.Empty, 6), (5, string.Empty, 6)
                ]);
                merges.Add($"A{r}:C{r}");
                merges.Add($"D{r}:F{r}");
                r++;

                AppendStyledCustomRow(sb, r, 21, [
                    (0, $"Operador: {entradaOperador}", 7), (1, string.Empty, 7), (2, string.Empty, 7),
                    (3, $"Operador: {salidaOperador}", 7), (4, string.Empty, 7), (5, string.Empty, 7)
                ]);
                merges.Add($"A{r}:C{r}");
                merges.Add($"D{r}:F{r}");
                r++;
            }

            if (showObs)
            {
                AppendStyledCustomRow(sb, r, 28, [
                    (0, "OBSERVACIONES:", 5),
                    (1, observaciones, 6), (2, string.Empty, 6), (3, string.Empty, 6), (4, string.Empty, 6), (5, string.Empty, 6)
                ]);
                merges.Add($"B{r}:F{r}");
                r++;
            }

            AppendStyledCustomRow(sb, r++, 14, []);
            AppendStyledCustomRow(sb, r, 19, [
                (0, $"PesajeProCamion - {folioText} - Emitido {DateTime.Now:dd/MM/yyyy HH:mm}", 16),
                (1, string.Empty, 16), (2, string.Empty, 16), (3, string.Empty, 16), (4, string.Empty, 16), (5, string.Empty, 16)
            ]);
            merges.Add($"A{r}:F{r}");

            sb.Append("</sheetData>");
            if (merges.Count > 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $"<mergeCells count=\"{merges.Count}\">");
                foreach (string m in merges) sb.Append(CultureInfo.InvariantCulture, $"<mergeCell ref=\"{m}\"/>");
                sb.Append("</mergeCells>");
            }
            sb.Append("""<pageMargins left="0.3" right="0.3" top="0.4" bottom="0.4" header="0.2" footer="0.2"/>""");
            sb.Append("""<pageSetup paperSize="9" orientation="portrait" fitToWidth="1" fitToHeight="1"/>""");
            sb.Append("</worksheet>");
            WriteZipEntry(zip, "xl/worksheets/sheet1.xml", sb.ToString());
        }
        catch (Exception ex)
        {
            AppLogger.Error("ExportXLSX", $"Error generando ticket XLSX '{filePath}': {ex.Message}", ex);
            throw;
        }
    }

    private static (IReadOnlySet<int> BoldCols, IReadOnlySet<int> LargerBoldCols) DetectWeightEmphasisColumns(
        IReadOnlyList<string> headers,
        IReadOnlySet<int>? explicitLarger)
    {
        var bold = new HashSet<int>();
        var larger = explicitLarger is not null ? new HashSet<int>(explicitLarger) : new HashSet<int>();
        for (int c = 0; c < headers.Count; c++)
        {
            string h = headers[c];
            if (h.Contains("Bruto", StringComparison.OrdinalIgnoreCase) || h.Contains("Tara", StringComparison.OrdinalIgnoreCase))
            {
                bold.Add(c);
            }
            else if (h.Contains("Neto", StringComparison.OrdinalIgnoreCase) && !h.Contains("%", StringComparison.Ordinal))
            {
                larger.Add(c);
            }
        }
        return (bold, larger);
    }

    private static (string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows) BuildDetalleSheet(
        IReadOnlyList<Pesaje> rows,
        bool includeFardos)
    {
        if (includeFardos)
        {
            return (
                "Detalle_Pesajes",
                ["N° Folio", "Fecha", "Hora", "Patente", "Acoplado", "Tipo de Pesaje", "Cliente", "Proveedor/Origen", "Transportista", "Producto", "Fardos", "Bruto (kg)", "Tara (kg)", "Neto (kg)", "Estado"],
                rows.Select(p => (IReadOnlyList<object?>)
                [
                    p.FolioPesaje, p.FechaSalida.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), p.HoraSalida,
                    p.Patente, p.Acoplado, TicketCustomizationHelper.FormatTipoPesajeNombre(p.TipoPesaje), p.NombreCliente, p.NombreOrigen,
                    p.NombreTransportista, p.NombreProducto, p.Fardos, p.Bruto, p.Tara, p.Neto, p.Anulado ? "ANULADO" : "ACTIVO"
                ]).ToList()
            );
        }
        return (
            "Detalle_Pesajes",
            ["N° Folio", "Fecha", "Hora", "Patente", "Acoplado", "Tipo de Pesaje", "Cliente", "Proveedor/Origen", "Transportista", "Producto", "Bruto (kg)", "Tara (kg)", "Neto (kg)", "Estado"],
            rows.Select(p => (IReadOnlyList<object?>)
            [
                p.FolioPesaje, p.FechaSalida.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), p.HoraSalida,
                p.Patente, p.Acoplado, TicketCustomizationHelper.FormatTipoPesajeNombre(p.TipoPesaje), p.NombreCliente, p.NombreOrigen,
                p.NombreTransportista, p.NombreProducto, p.Bruto, p.Tara, p.Neto, p.Anulado ? "ANULADO" : "ACTIVO"
            ]).ToList()
        );
    }

    private static (string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows) BuildSegmentSheet(
        string sheetName, string dimHeader, IReadOnlyList<SegmentSummaryRow> segs)
    {
        return (
            sheetName,
            ["Código", dimHeader, "Tickets", "Fardos", "Bruto (kg)", "Tara (kg)", "Neto (kg)", "Toneladas", "Promedio Neto (kg)", "% Participación"],
            segs.Select(s => (IReadOnlyList<object?>)
            [
                s.Clave, s.Nombre, s.Tickets, s.TotalFardos, s.TotalBrutoKg, s.TotalTaraKg, s.TotalNetoKg, s.ToneladasNetas, s.PromedioNetoKg, s.PorcentajeNeto
            ]).ToList()
        );
    }

    public static void ExportMultiSheetXlsx(
        string filePath,
        IReadOnlyList<(string SheetName, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows)> sheets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(sheets);
        if (sheets.Count == 0) throw new ArgumentException("Debe incluir al menos una hoja.", nameof(sheets));

        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

            var cleanNames = sheets.Select((s, idx) => SanitizeSheetName(s.SheetName, $"Hoja{idx + 1}")).ToList();
            var defSb = new StringBuilder("<definedNames>");
            for (int i = 0; i < cleanNames.Count; i++)
            {
                string esc = SecurityElement.Escape(cleanNames[i]) ?? cleanNames[i];
                defSb.Append(CultureInfo.InvariantCulture, $"<definedName name=\"_xlnm.Print_Titles\" localSheetId=\"{i}\">'{esc}'!$1:$1</definedName>");
            }
            defSb.Append("</definedNames>");

            WriteXlsxPackageScaffold(zip, cleanNames, defSb.ToString());

            for (int i = 0; i < sheets.Count; i++)
            {
                string xml = BuildAutoFitTableWorksheetXml(sheets[i].Headers, sheets[i].Rows);
                WriteZipEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", xml);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("ExportXLSX", $"Error exportando libro XLSX '{filePath}': {ex.Message}", ex);
            throw;
        }
    }

    public static void ExportToXlsx(string filePath, string sheetName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        ExportMultiSheetXlsx(filePath, [(string.IsNullOrWhiteSpace(sheetName) ? "Datos" : sheetName, headers, rows)]);
    }

    private static string BuildAutoFitTableWorksheetXml(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        double[] colWidths = ComputeExcelColumnWidths(headers, rows, null);
        (IReadOnlySet<int> boldCols, IReadOnlySet<int> largerBoldCols) = DetectWeightEmphasisColumns(headers, null);
        int? folioIdx = null;
        for (int i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("Folio", StringComparison.OrdinalIgnoreCase)) { folioIdx = i; break; }
        }

        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        sb.Append("""<sheetPr><pageSetUpPr fitToPage="1"/></sheetPr>""");
        sb.Append("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");
        sb.Append("<cols>");
        for (int c = 0; c < colWidths.Length; c++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{colWidths[c]:F1}\" bestFit=\"1\" customWidth=\"1\"/>");
        }
        sb.Append("</cols><sheetData>");

        AppendStyledRow(sb, 1, 24.0, headers.Cast<object?>().ToList(), isHeader: true, alt: false);
        for (int r = 0; r < rows.Count; r++)
        {
            AppendStandardReportDataRow(sb, r + 2, 19.5, rows[r], headers.Count, alt: r % 2 == 1, isTotalsRow: false, folioIdx, boldCols, largerBoldCols);
        }
        sb.Append("</sheetData>");
        string lastCol = ColumnLetters(Math.Max(0, headers.Count - 1));
        int lastRow = Math.Max(1, rows.Count + 1);
        sb.Append(CultureInfo.InvariantCulture, $"<autoFilter ref=\"A1:{lastCol}{lastRow}\"/>");
        sb.Append("""<pageMargins left="0.25" right="0.25" top="0.35" bottom="0.35" header="0.2" footer="0.2"/>""");
        sb.Append("""<pageSetup paperSize="9" orientation="landscape" fitToWidth="1" fitToHeight="0"/>""");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private static double[] ComputeExcelColumnWidths(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows, IReadOnlyList<object?>? totalsRow)
    {
        int n = Math.Max(1, headers.Count);
        double[] widths = new double[n];
        for (int c = 0; c < n; c++)
        {
            int maxChars = c < headers.Count ? headers[c].Length + 4 : 10;
            int sample = Math.Min(rows.Count, 200);
            for (int r = 0; r < sample; r++)
            {
                if (c < rows[r].Count && rows[r][c] is { } v)
                {
                    string s = Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty;
                    maxChars = Math.Max(maxChars, Math.Min(s.Length + 4, 44));
                }
            }
            if (totalsRow is not null && c < totalsRow.Count && totalsRow[c] is { } tv)
            {
                string ts = Convert.ToString(tv, CultureInfo.InvariantCulture) ?? string.Empty;
                maxChars = Math.Max(maxChars, Math.Min(ts.Length + 4, 44));
            }
            widths[c] = Math.Clamp(maxChars * 1.15, 10.0, 48.0);
        }

        double sum = widths.Sum();
        if (sum > 0 && sum < 140.0)
        {
            double factor = 148.0 / sum;
            for (int c = 0; c < n; c++)
            {
                widths[c] = Math.Round(Math.Min(56.0, widths[c] * factor), 1);
            }
        }
        return widths;
    }

    private static string SanitizeSheetName(string? rawName, string fallback)
    {
        string s = string.IsNullOrWhiteSpace(rawName) ? fallback : rawName;
        foreach (char bad in new[] { '\\', '/', '?', '*', '[', ']', ':' })
        {
            s = s.Replace(bad, '_');
        }
        return s.Length > 31 ? s[..31] : s;
    }

    private static void WriteXlsxPackageScaffold(ZipArchive zip, IReadOnlyList<string> rawSheetNames, string definedNamesXml = "")
    {
        var ctOverrides = new StringBuilder();
        var rels = new StringBuilder();
        var wbSheets = new StringBuilder();

        for (int i = 0; i < rawSheetNames.Count; i++)
        {
            int idx = i + 1;
            string safeName = SanitizeSheetName(rawSheetNames[i], $"Hoja{idx}");
            ctOverrides.Append(CultureInfo.InvariantCulture, $"  <Override PartName=\"/xl/worksheets/sheet{idx}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>\n");
            rels.Append(CultureInfo.InvariantCulture, $"  <Relationship Id=\"rId{idx}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{idx}.xml\"/>\n");
            wbSheets.Append(CultureInfo.InvariantCulture, $"<sheet name=\"{SecurityElement.Escape(safeName)}\" sheetId=\"{idx}\" r:id=\"rId{idx}\"/>");
        }
        int stylesRelId = rawSheetNames.Count + 1;
        rels.Append(CultureInfo.InvariantCulture, $"  <Relationship Id=\"rId{stylesRelId}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>\n");

        WriteZipEntry(zip, "[Content_Types].xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-officedocument.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            {ctOverrides}</Types>
            """);

        WriteZipEntry(zip, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);

        WriteZipEntry(zip, "xl/_rels/workbook.xml.rels", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            {rels}</Relationships>
            """);

        WriteZipEntry(zip, "xl/workbook.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>{wbSheets}</sheets>{definedNamesXml}
            </workbook>
            """);

        WriteZipEntry(zip, "xl/styles.xml", BuildCorporateStylesXml());
    }

    private static string BuildCorporateStylesXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="3">
            <numFmt numFmtId="164" formatCode="#,##0"/>
            <numFmt numFmtId="165" formatCode="#,##0.00"/>
            <numFmt numFmtId="166" formatCode="000000"/>
          </numFmts>
          <fonts count="12">
            <font><sz val="10"/><color rgb="FF162433"/><name val="Calibri"/></font>
            <font><b/><sz val="14"/><color rgb="FFFFFFFF"/><name val="Calibri"/></font>
            <font><sz val="10"/><color rgb="FFCCA969"/><name val="Calibri"/></font>
            <font><b/><sz val="10"/><color rgb="FFFFFFFF"/><name val="Calibri"/></font>
            <font><b/><sz val="10"/><color rgb="FF082D45"/><name val="Calibri"/></font>
            <font><b/><sz val="14"/><color rgb="FF082D45"/><name val="Calibri"/></font>
            <font><b/><sz val="11"/><color rgb="FF063953"/><name val="Calibri"/></font>
            <font><b/><sz val="9"/><color rgb="FFCCA969"/><name val="Calibri"/></font>
            <font><b/><sz val="15"/><color rgb="FFFFFFFF"/><name val="Calibri"/></font>
            <font><b/><sz val="19"/><color rgb="FF38EF9B"/><name val="Calibri"/></font>
            <font><b/><sz val="10.5"/><color rgb="FF162433"/><name val="Calibri"/></font>
            <font><b/><sz val="12"/><color rgb="FF065F46"/><name val="Calibri"/></font>
          </fonts>
          <fills count="8">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="gray125"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FF082D45"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FF063953"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFCCA969"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFF0F6F7"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFF7FAFA"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFE6F1F5"/></patternFill></fill>
          </fills>
          <borders count="3">
            <border><left/><right/><top/><bottom/><diagonal/></border>
            <border>
              <left style="thin"><color rgb="FFC0D0D6"/></left>
              <right style="thin"><color rgb="FFC0D0D6"/></right>
              <top style="thin"><color rgb="FFC0D0D6"/></top>
              <bottom style="thin"><color rgb="FFC0D0D6"/></bottom>
              <diagonal/>
            </border>
            <border>
              <left style="medium"><color rgb="FFCCA969"/></left>
              <right style="medium"><color rgb="FFCCA969"/></right>
              <top style="medium"><color rgb="FFCCA969"/></top>
              <bottom style="medium"><color rgb="FFCCA969"/></bottom>
              <diagonal/>
            </border>
          </borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="22">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1" applyAlignment="1"><alignment vertical="center" horizontal="left"/></xf>
            <xf numFmtId="0" fontId="2" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1" applyAlignment="1"><alignment vertical="center" horizontal="left"/></xf>
            <xf numFmtId="0" fontId="4" fillId="4" borderId="2" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="center" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="3" fillId="3" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="center" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="4" fillId="5" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="left"/></xf>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyFont="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="left" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="0" fillId="6" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="left" wrapText="1"/></xf>
            <xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyFont="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="right"/></xf>
            <xf numFmtId="164" fontId="0" fillId="6" borderId="1" xfId="0" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="right"/></xf>
            <xf numFmtId="165" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyFont="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="right"/></xf>
            <xf numFmtId="0" fontId="5" fillId="7" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="left"/></xf>
            <xf numFmtId="0" fontId="6" fillId="7" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="center"/></xf>
            <xf numFmtId="0" fontId="7" fillId="2" borderId="2" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="center"/></xf>
            <xf numFmtId="0" fontId="8" fillId="2" borderId="2" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="center"/></xf>
            <xf numFmtId="0" fontId="9" fillId="2" borderId="2" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="center"/></xf>
            <xf numFmtId="0" fontId="4" fillId="0" borderId="0" xfId="0" applyFont="1" applyAlignment="1"><alignment vertical="center" horizontal="left"/></xf>
            <xf numFmtId="166" fontId="4" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyFont="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="center"/></xf>
            <xf numFmtId="164" fontId="10" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyFont="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="right"/></xf>
            <xf numFmtId="164" fontId="11" fillId="7" borderId="1" xfId="0" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="right"/></xf>
            <xf numFmtId="0" fontId="4" fillId="7" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="left"/></xf>
            <xf numFmtId="164" fontId="4" fillId="7" borderId="1" xfId="0" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" horizontal="right"/></xf>
          </cellXfs>
        </styleSheet>
        """;

    public static void ExportToCsv(string filePath, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(";", headers.Select(EscapeCsv)));
            foreach (IReadOnlyList<string?> row in rows)
            {
                sb.AppendLine(string.Join(";", row.Select(EscapeCsv)));
            }
            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex)
        {
            AppLogger.Error("ExportCSV", $"Error generando CSV '{filePath}': {ex.Message}", ex);
            throw;
        }
    }

    public static void ExportToDbf(string filePath, IReadOnlyList<(string Name, int Width)> columns, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding cp1252 = Encoding.GetEncoding(1252);

            short headerSize = checked((short)(32 + columns.Count * 32 + 1));
            short recordSize = checked((short)(1 + columns.Sum(c => Math.Clamp(c.Width, 1, 250))));

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            byte[] header = new byte[32];
            DateTime now = DateTime.Today;
            header[0] = 0x03;
            header[1] = checked((byte)(now.Year - 1900));
            header[2] = checked((byte)now.Month);
            header[3] = checked((byte)now.Day);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), rows.Count);
            BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(8, 2), headerSize);
            BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(10, 2), recordSize);
            header[29] = 0x03;
            fs.Write(header);

            foreach ((string colName, int colWidth) in columns)
            {
                byte[] fdesc = new byte[32];
                byte[] nameBytes = Encoding.ASCII.GetBytes(colName.ToUpperInvariant());
                Array.Copy(nameBytes, fdesc, Math.Min(10, nameBytes.Length));
                fdesc[11] = (byte)'C';
                fdesc[16] = checked((byte)Math.Clamp(colWidth, 1, 250));
                fs.Write(fdesc);
            }
            fs.WriteByte(0x0D);

            byte[] recBuf = new byte[recordSize];
            foreach (IReadOnlyList<string?> row in rows)
            {
                Array.Fill(recBuf, (byte)' ');
                int offset = 1;
                for (int c = 0; c < columns.Count; c++)
                {
                    int w = Math.Clamp(columns[c].Width, 1, 250);
                    string val = c < row.Count && row[c] is not null ? row[c]! : string.Empty;
                    byte[] b = cp1252.GetBytes(val);
                    Array.Copy(b, 0, recBuf, offset, Math.Min(w, b.Length));
                    offset += w;
                }
                fs.Write(recBuf);
            }
            fs.WriteByte(0x1A);
        }
        catch (Exception ex)
        {
            AppLogger.Error("ExportDBF", $"Error generando DBF '{filePath}': {ex.Message}", ex);
            throw;
        }
    }

    private static string EscapeCsv(string? value)
    {
        string s = value ?? string.Empty;
        return s.Contains(';') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : s;
    }

    private static void WriteZipEntry(ZipArchive zip, string entryName, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void AppendStyledCustomRow(StringBuilder sb, int rowNum, double height, IReadOnlyList<(int Col, string Text, int StyleId)> cells)
    {
        sb.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowNum}\" ht=\"{height:F1}\" customHeight=\"1\">");
        foreach ((int col, string text, int style) in cells)
        {
            string cellRef = $"{ColumnLetters(col)}{rowNum}";
            string escaped = SecurityElement.Escape(text) ?? string.Empty;
            sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{cellRef}\" s=\"{style}\" t=\"inlineStr\"><is><t>{escaped}</t></is></c>");
        }
        sb.Append("</row>");
    }

    private static void AppendStyledRow(StringBuilder sb, int rowNum, double height, IReadOnlyList<object?> values, bool isHeader, bool alt)
    {
        AppendStandardReportDataRow(sb, rowNum, height, values, values.Count, alt, isTotalsRow: false, folioColIdx: null, boldCols: null, largerBoldCols: null, isHeader: isHeader);
    }

    private static void AppendStandardReportDataRow(
        StringBuilder sb,
        int rowNum,
        double height,
        IReadOnlyList<object?> values,
        int colCount,
        bool alt,
        bool isTotalsRow,
        int? folioColIdx,
        IReadOnlySet<int>? boldCols,
        IReadOnlySet<int>? largerBoldCols,
        bool isHeader = false)
    {
        sb.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowNum}\" ht=\"{height:F1}\" customHeight=\"1\">");
        for (int c = 0; c < colCount; c++)
        {
            string cellRef = $"{ColumnLetters(c)}{rowNum}";
            object? val = c < values.Count ? values[c] : null;
            bool isFolio = !isHeader && !isTotalsRow && folioColIdx == c && val is int;
            if (!isHeader && (isFolio || val is int or long or short or double or decimal))
            {
                int style = isFolio
                    ? 17
                    : (isTotalsRow
                        ? (largerBoldCols?.Contains(c) == true ? 19 : 21)
                        : (largerBoldCols?.Contains(c) == true
                            ? 19
                            : (boldCols?.Contains(c) == true ? 18 : (val is double or decimal ? 10 : (alt ? 9 : 8)))));
                string numStr = Convert.ToString(val, CultureInfo.InvariantCulture) ?? "0";
                sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{cellRef}\" s=\"{style}\"><v>{numStr}</v></c>");
            }
            else
            {
                int style = isHeader ? 4 : (isTotalsRow ? 20 : (alt ? 7 : 6));
                string txt = SecurityElement.Escape(Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty) ?? string.Empty;
                sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{cellRef}\" s=\"{style}\" t=\"inlineStr\"><is><t>{txt}</t></is></c>");
            }
        }
        sb.Append("</row>");
    }

    private static string ColumnLetters(int zeroBased)
    {
        int n = zeroBased + 1;
        var chars = new Stack<char>();
        while (n > 0)
        {
            n--;
            chars.Push((char)('A' + (n % 26)));
            n /= 26;
        }
        return new string(chars.ToArray());
    }
}
