using System.Globalization;
using System.Text;
using PesajeProCamion.Domain.Diagnostics;
using PesajeProCamion.Domain.Entities;

namespace PesajeProCamion.Hardware.Export;

/// <summary>
/// Generador nativo PDF 1.4 vectorial (zero dependencias externas) con paleta corporativa
/// Petrol Teal (#082D45 / #005781) y Dorado (#CCA969) a página completa para tickets comerciales e informes BI.
/// </summary>
internal static class PdfDocumentWriter
{
    private static readonly Encoding WinAnsi = CreateWinAnsi();

    private static Encoding CreateWinAnsi()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    public static void WriteTableReportPdf(
        string filePath,
        string title,
        string subtitle,
        IReadOnlyList<(string Label, string Value)> kpis,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        IReadOnlyList<object?>? totalsRow = null,
        int? folioColumnIndex = null,
        IReadOnlySet<int>? boldCols = null,
        IReadOnlySet<int>? emphasizedLargerBoldCols = null)
    {
        try
        {
            const double pageW = 842.0; // A4 Landscape página completa
            const double pageH = 595.0;
            const double margin = 22.0;
            double contentW = pageW - 2 * margin;

            double[] colWidths = ComputeColumnWidths(headers, rows, totalsRow, contentW);
            var pages = new List<StringBuilder>();

            int rowIndex = 0;
            int pageNum = 0;
            do
            {
                pageNum++;
                var sb = new StringBuilder();
                double y = pageH - margin;

                // Header bar Petrol Teal + Gold accent
                FillRect(sb, margin, y - 40, contentW, 40, 0.031, 0.176, 0.271);
                FillRect(sb, margin, y - 42, contentW, 2.2, 0.800, 0.663, 0.412);
                DrawText(sb, FitText(title, contentW - 24, 13.0, bold: true), margin + 12, y - 18, 13.0, bold: true, 1, 1, 1);
                DrawText(sb, FitText(subtitle, contentW - 24, 8.2, bold: false), margin + 12, y - 33, 8.2, bold: false, 0.86, 0.90, 0.92);
                y -= 50;

                // KPIs strip on Page 1
                if (pageNum == 1 && kpis.Count > 0)
                {
                    int kCount = Math.Min(kpis.Count, 5);
                    double gap = 8.0;
                    double cardW = (contentW - gap * (kCount - 1)) / kCount;
                    for (int k = 0; k < kCount; k++)
                    {
                        double cx = margin + k * (cardW + gap);
                        FillRect(sb, cx, y - 36, cardW, 36, 0.96, 0.97, 0.98);
                        StrokeRect(sb, cx, y - 36, cardW, 36, 0.75, 0.82, 0.85, 0.6);
                        FillRect(sb, cx, y - 3.5, cardW, 3.5, 0.000, 0.341, 0.506);
                        DrawText(sb, FitText(kpis[k].Label, cardW - 10, 7.2, bold: true), cx + 6, y - 14, 7.2, bold: true, 0.35, 0.43, 0.49);
                        DrawText(sb, FitText(kpis[k].Value, cardW - 10, 9.6, bold: true), cx + 6, y - 29, 9.6, bold: true, 0.031, 0.176, 0.271);
                    }
                    y -= 44;
                }

                // Encabezado tabular repetido en cada página
                FillRect(sb, margin, y - 20, contentW, 20, 0.024, 0.224, 0.325);
                double x = margin;
                for (int c = 0; c < headers.Count; c++)
                {
                    DrawText(sb, FitText(headers[c], colWidths[c] - 6, 7.8, bold: true), x + 4, y - 13, 7.8, bold: true, 1, 1, 1);
                    x += colWidths[c];
                }
                y -= 20;

                double minY = margin + 22;
                bool alt = false;
                while (rowIndex < rows.Count && y - 15.5 >= minY)
                {
                    IReadOnlyList<object?> r = rows[rowIndex];
                    if (alt)
                    {
                        FillRect(sb, margin, y - 15.5, contentW, 15.5, 0.94, 0.965, 0.97);
                    }
                    StrokeLine(sb, margin, y - 15.5, margin + contentW, y - 15.5, 0.85, 0.89, 0.91, 0.4);

                    DrawTableCellsRow(sb, r, headers.Count, margin, y, 15.5, colWidths, isTotalsRow: false, folioColumnIndex, boldCols, emphasizedLargerBoldCols);

                    y -= 15.5;
                    rowIndex++;
                    alt = !alt;
                }

                if (rows.Count == 0 && pageNum == 1)
                {
                    DrawText(sb, "No existen registros para los filtros seleccionados.", margin + 8, y - 18, 9.0, bold: false, 0.4, 0.45, 0.5);
                    y -= 22;
                }

                // Fila de totales al finalizar todos los registros (si cabe en esta página; si no, crea página final)
                if (rowIndex >= rows.Count && totalsRow is not null && y - 20.0 >= minY)
                {
                    FillRect(sb, margin, y - 20.0, contentW, 20.0, 0.90, 0.945, 0.96);
                    StrokeRect(sb, margin, y - 20.0, contentW, 20.0, 0.031, 0.176, 0.271, 0.8);
                    DrawTableCellsRow(sb, totalsRow, headers.Count, margin, y, 20.0, colWidths, isTotalsRow: true, folioColumnIndex, boldCols, emphasizedLargerBoldCols);
                    totalsRow = null;
                }

                pages.Add(sb);
            } while (rowIndex < rows.Count || totalsRow is not null);

            for (int i = 0; i < pages.Count; i++)
            {
                StringBuilder sb = pages[i];
                StrokeLine(sb, margin, margin + 16, margin + contentW, margin + 16, 0.78, 0.82, 0.85, 0.6);
                DrawText(sb, $"PesajeProCamion - Generado {DateTime.Now:dd/MM/yyyy HH:mm}", margin, margin + 5, 7.5, bold: false, 0.42, 0.48, 0.54);
                string pgLabel = $"Página {i + 1} de {pages.Count}";
                DrawText(sb, pgLabel, pageW - margin - EstimateTextWidth(pgLabel, 7.5, bold: true), margin + 5, 7.5, bold: true, 0.031, 0.176, 0.271);
            }

            WritePdfFile(filePath, pageW, pageH, pages);
        }
        catch (Exception ex)
        {
            AppLogger.Error("ExportPDF", $"Error generando informe PDF '{filePath}': {ex.Message}", ex);
            throw;
        }
    }

    private static void DrawTableCellsRow(
        StringBuilder sb,
        IReadOnlyList<object?> r,
        int colCount,
        double startX,
        double topY,
        double rowH,
        double[] colWidths,
        bool isTotalsRow,
        int? folioColIdx,
        IReadOnlySet<int>? boldCols,
        IReadOnlySet<int>? largerBoldCols)
    {
        double x = startX;
        for (int c = 0; c < colCount; c++)
        {
            object? val = c < r.Count ? r[c] : null;
            bool isFolio = !isTotalsRow && folioColIdx == c && val is int;
            string txt = isFolio
                ? Convert.ToInt32(val, CultureInfo.InvariantCulture).ToString("D6", CultureInfo.InvariantCulture)
                : val switch
                {
                    int iv => iv.ToString("N0", CultureInfo.InvariantCulture),
                    long lv => lv.ToString("N0", CultureInfo.InvariantCulture),
                    double dv => dv.ToString("N2", CultureInfo.InvariantCulture),
                    _ => Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty
                };

            bool isLarger = largerBoldCols?.Contains(c) == true;
            bool isBold = isTotalsRow || isFolio || isLarger || (boldCols?.Contains(c) == true);
            double fontSize = isLarger ? (isTotalsRow ? 9.2 : 8.5) : (isTotalsRow ? 8.2 : 7.6);
            bool isNumeric = !isFolio && val is int or long or short or double or decimal;

            string fitted = FitText(txt, colWidths[c] - 6, fontSize, isBold);
            (double rr, double gg, double bb) = isFolio
                ? (0.031, 0.176, 0.271)
                : (isLarger ? (0.020, 0.360, 0.240) : (0.09, 0.14, 0.20));

            double textY = topY - (rowH * 0.72);
            if (isNumeric)
            {
                double tw = EstimateTextWidth(fitted, fontSize, isBold);
                double drawX = Math.Max(x + 3, x + colWidths[c] - 4 - tw);
                DrawText(sb, fitted, drawX, textY, fontSize, isBold, rr, gg, bb);
            }
            else
            {
                DrawText(sb, fitted, x + 4, textY, fontSize, isBold, rr, gg, bb);
            }
            x += colWidths[c];
        }
    }

    public static void WriteSingleTicketPdf(
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
        string observaciones,
        string? extraBadgeColor = null)
    {
        try
        {
            const double pageW = 595.0; // A4 Portrait página completa
            const double pageH = 842.0;
            const double margin = 30.0;
            double contentW = pageW - 2 * margin; // 535pt

            var sb = new StringBuilder();
            double yTop = pageH - margin;
            double yBottomFooter = margin + 26.0;

            // 1. Banner corporativo Petrol Teal + barra dorada (#CCA969)
            const double bannerH = 74.0;
            FillRect(sb, margin, yTop - bannerH, contentW, bannerH, 0.031, 0.176, 0.271);
            FillRect(sb, margin, yTop - bannerH - 3.0, contentW, 3.0, 0.800, 0.663, 0.412);

            double badgeW = 0;
            if (!string.IsNullOrWhiteSpace(tipoBadge))
            {
                double badgeFontSize = tipoBadge.Length > 34 ? 7.6 : 8.4;
                badgeW = Math.Clamp(EstimateTextWidth(tipoBadge, badgeFontSize, bold: true) + 28.0, 175.0, contentW * 0.49);
                double bx = margin + contentW - badgeW - 12.0;
                double by = yTop - 35.0;
                (double br, double bg, double bb) = extraBadgeColor switch
                {
                    "DANGER" => (0.776, 0.157, 0.157),
                    "AMBER" => (0.851, 0.467, 0.024),
                    _ => (0.055, 0.624, 0.431)
                };
                FillRect(sb, bx, by, badgeW, 24.0, br, bg, bb);
                StrokeRect(sb, bx, by, badgeW, 24.0, 0.800, 0.663, 0.412, 1.0);
                string fitBadge = FitText(tipoBadge, badgeW - 12.0, badgeFontSize, bold: true);
                double txtW = EstimateTextWidth(fitBadge, badgeFontSize, bold: true);
                double btx = bx + Math.Max(6.0, (badgeW - txtW) / 2.0);
                DrawText(sb, fitBadge, btx, by + 8.5, badgeFontSize, bold: true, 1, 1, 1);
            }

            double titleMaxW = contentW - (badgeW > 0 ? badgeW + 30.0 : 28.0);
            DrawText(sb, FitText(ticketTitle, titleMaxW, 13.5, bold: true), margin + 16, yTop - 25, 13.5, bold: true, 1, 1, 1);
            DrawText(sb, FitText(companyName, titleMaxW, 11.2, bold: true), margin + 16, yTop - 44, 11.2, bold: true, 0.800, 0.663, 0.412);
            DrawText(sb, FitText(companySub, contentW - 32.0, 8.8, bold: false), margin + 16, yTop - 62, 8.8, bold: false, 0.86, 0.91, 0.94);

            double y = yTop - bannerH - 14.0;

            // 2. Franja N° Folio: xxxxxx (Negrita) + Tipo / Estado Opcional
            const double folioBandH = 44.0;
            FillRect(sb, margin, y - folioBandH, contentW, folioBandH, 0.945, 0.969, 0.976);
            StrokeRect(sb, margin, y - folioBandH, contentW, folioBandH, 0.72, 0.80, 0.84, 1.0);
            DrawText(sb, folioText, margin + 16, y - 27, 15.5, bold: true, 0.031, 0.176, 0.271);

            if (!string.IsNullOrWhiteSpace(estadoText))
            {
                string estLabel = $"Estado: {estadoText}";
                double ew = Math.Max(128.0, EstimateTextWidth(estLabel, 9.2, bold: true) + 24.0);
                double ex = margin + contentW - ew - 14.0;
                bool isDanger = estadoText.Contains("ANULADO", StringComparison.OrdinalIgnoreCase);
                FillRect(sb, ex, y - 33, ew, 22, isDanger ? 0.98 : 0.90, isDanger ? 0.90 : 0.96, isDanger ? 0.90 : 0.93);
                StrokeRect(sb, ex, y - 33, ew, 22, isDanger ? 0.78 : 0.06, isDanger ? 0.16 : 0.55, isDanger ? 0.16 : 0.40, 0.9);
                double etx = ex + Math.Max(6.0, (ew - EstimateTextWidth(estLabel, 9.2, bold: true)) / 2.0);
                DrawText(sb, estLabel, etx, y - 25, 9.2, bold: true, isDanger ? 0.75 : 0.04, isDanger ? 0.12 : 0.42, isDanger ? 0.12 : 0.28);
            }
            else if (!string.IsNullOrWhiteSpace(tipoBadge))
            {
                string fitT = FitText(tipoBadge, contentW * 0.52, 9.0, bold: true);
                double tw = EstimateTextWidth(fitT, 9.0, bold: true);
                DrawText(sb, fitT, margin + contentW - tw - 14.0, y - 26, 9.0, bold: true, 0.000, 0.341, 0.506);
            }
            y -= folioBandH + 14.0;

            const double weightsH = 114.0;
            double timesH = showTimes ? 92.0 : 0.0;
            double obsH = showObs ? 52.0 : 0.0;
            const double signH = 54.0;
            double footerReserve = weightsH + 16.0 + (showTimes || showObs ? timesH + obsH + (showTimes && showObs ? 8.0 : 0.0) + 16.0 : 0.0) + signH + 14.0;

            double availableForDetails = Math.Max(140.0, (y - yBottomFooter) - footerReserve);
            int rowCount = Math.Max(1, details.Count);
            double rowH = Math.Clamp((availableForDetails - 16.0) / rowCount, 24.0, 40.0);
            double detailsH = Math.Max(availableForDetails, rowCount * rowH + 16.0);

            // 3. Tabla de Campos Visibles
            FillRect(sb, margin, y - detailsH, contentW, detailsH, 0.992, 0.995, 0.996);
            StrokeRect(sb, margin, y - detailsH, contentW, detailsH, 0.72, 0.80, 0.84, 1.0);

            double startRowY = y - 8.0;
            for (int i = 0; i < details.Count; i++)
            {
                double rTop = startRowY - i * rowH;
                if (i % 2 == 1)
                {
                    FillRect(sb, margin + 2, rTop - rowH, contentW - 4, rowH, 0.945, 0.969, 0.976);
                }
                StrokeLine(sb, margin + 10, rTop - rowH, margin + contentW - 10, rTop - rowH, 0.88, 0.92, 0.94, 0.5);
                double textY = rTop - (rowH * 0.62);
                DrawText(sb, FitText(details[i].Label, 175.0, 9.8, bold: true), margin + 16, textY, 9.8, bold: true, 0.30, 0.41, 0.48);
                DrawText(sb, FitText(details[i].Value, contentW - 208.0, 10.4, bold: false), margin + 194, textY, 10.4, bold: false, 0.08, 0.14, 0.21);
            }
            y -= detailsH + 16.0;

            // 4. Tarjeta de Pesos: Bruto y Tara grandes en Negrita, NETO aún más grande y destacado en Negrita
            FillRect(sb, margin, y - weightsH, contentW, weightsH, 0.031, 0.176, 0.271);
            StrokeRect(sb, margin, y - weightsH, contentW, weightsH, 0.800, 0.663, 0.412, 1.6);
            double colW = contentW / 3.0;
            StrokeLine(sb, margin + colW, y - 14, margin + colW, y - weightsH + 14, 0.14, 0.36, 0.49, 0.8);
            StrokeLine(sb, margin + colW * 2, y - 14, margin + colW * 2, y - weightsH + 14, 0.14, 0.36, 0.49, 0.8);

            DrawWeightColumn(sb, margin, y, weightsH, colW, "PESO BRUTO", $"{brutoKg:N0} kg", valueSize: 17.5, highlightNeto: false);
            DrawWeightColumn(sb, margin + colW, y, weightsH, colW, "PESO TARA", $"{taraKg:N0} kg", valueSize: 17.5, highlightNeto: false);
            DrawWeightColumn(sb, margin + colW * 2, y, weightsH, colW, "PESO NETO", isEnProceso ? "PENDIENTE" : $"{netoKg:N0} kg", valueSize: 22.0, highlightNeto: true);
            y -= weightsH + 16.0;

            // 5. Bloque Tiempos y Observaciones
            if (showTimes || showObs)
            {
                double boxH = (showTimes ? timesH : 0) + (showObs ? obsH : 0) + (showTimes && showObs ? 8.0 : 0.0);
                FillRect(sb, margin, y - boxH, contentW, boxH, 0.96, 0.975, 0.982);
                StrokeRect(sb, margin, y - boxH, contentW, boxH, 0.72, 0.80, 0.84, 0.9);

                double ty = y - 20.0;
                if (showTimes)
                {
                    double midX = margin + contentW / 2.0 + 10.0;
                    DrawText(sb, "ENTRADA A PLANTA", margin + 16, ty, 10.0, bold: true, 0.000, 0.341, 0.506);
                    DrawText(sb, FitText($"Fecha / Hora :  {entradaFechaHora}", contentW / 2.0 - 24, 9.8, bold: false), margin + 16, ty - 22, 9.8, bold: false, 0.09, 0.14, 0.20);
                    DrawText(sb, FitText($"Operador     :  {entradaOperador}", contentW / 2.0 - 24, 9.5, bold: false), margin + 16, ty - 42, 9.5, bold: false, 0.32, 0.42, 0.49);

                    StrokeLine(sb, margin + contentW / 2.0, y - 10, margin + contentW / 2.0, y - timesH + 8, 0.82, 0.87, 0.90, 0.6);

                    DrawText(sb, "SALIDA DE PLANTA", midX, ty, 10.0, bold: true, 0.000, 0.341, 0.506);
                    DrawText(sb, FitText($"Fecha / Hora :  {salidaFechaHora}", contentW / 2.0 - 24, 9.8, bold: false), midX, ty - 22, 9.8, bold: false, 0.09, 0.14, 0.20);
                    DrawText(sb, FitText($"Operador     :  {salidaOperador}", contentW / 2.0 - 24, 9.5, bold: false), midX, ty - 42, 9.5, bold: false, 0.32, 0.42, 0.49);

                    ty = y - timesH - 20.0;
                }
                if (showObs)
                {
                    if (showTimes)
                    {
                        StrokeLine(sb, margin + 12, y - timesH, margin + contentW - 12, y - timesH, 0.82, 0.87, 0.90, 0.6);
                    }
                    DrawText(sb, "OBSERVACIONES:", margin + 16, ty, 9.6, bold: true, 0.30, 0.41, 0.48);
                    DrawText(sb, FitText(observaciones, contentW - 140.0, 9.8, bold: false), margin + 124, ty, 9.8, bold: false, 0.09, 0.14, 0.20);
                }
            }

            // 6. Firmas y pie
            double signY = yBottomFooter + 18.0;
            double sigW = (contentW - 70.0) / 2.0;
            StrokeLine(sb, margin + 14, signY + 14, margin + 14 + sigW, signY + 14, 0.45, 0.54, 0.60, 0.7);
            DrawText(sb, "Firma / Timbre Operador Báscula", margin + 14 + (sigW - EstimateTextWidth("Firma / Timbre Operador Báscula", 8.2, false)) / 2.0, signY + 2, 8.2, bold: false, 0.40, 0.48, 0.54);

            double sig2X = margin + contentW - 14 - sigW;
            StrokeLine(sb, sig2X, signY + 14, sig2X + sigW, signY + 14, 0.45, 0.54, 0.60, 0.7);
            DrawText(sb, "Firma Conductor / Recepción Conforme", sig2X + (sigW - EstimateTextWidth("Firma Conductor / Recepción Conforme", 8.2, false)) / 2.0, signY + 2, 8.2, bold: false, 0.40, 0.48, 0.54);

            StrokeLine(sb, margin, margin + 16, margin + contentW, margin + 16, 0.78, 0.82, 0.85, 0.7);
            DrawText(sb, $"PesajeProCamion - {folioText} - Emitido {DateTime.Now:dd/MM/yyyy HH:mm}", margin, margin + 5, 8.0, bold: false, 0.42, 0.48, 0.54);
            const string footR = "Documento Estandarizado Página Completa";
            DrawText(sb, footR, margin + contentW - EstimateTextWidth(footR, 8.0, true), margin + 5, 8.0, bold: true, 0.031, 0.176, 0.271);

            WritePdfFile(filePath, pageW, pageH, [sb]);
        }
        catch (Exception ex)
        {
            AppLogger.Error("ExportPDF", $"Error generando ticket PDF '{filePath}': {ex.Message}", ex);
            throw;
        }
    }

    private static void DrawWeightColumn(
        StringBuilder sb,
        double x,
        double topY,
        double boxH,
        double w,
        string label,
        string value,
        double valueSize,
        bool highlightNeto)
    {
        double lx = x + (w - EstimateTextWidth(label, 9.8, bold: true)) / 2.0;
        double vx = x + (w - EstimateTextWidth(value, valueSize, bold: true)) / 2.0;
        DrawText(sb, label, lx, topY - 30.0, 9.8, bold: true, 0.800, 0.663, 0.412);
        double vy = topY - boxH * 0.68;
        if (highlightNeto)
        {
            DrawText(sb, value, vx, vy, valueSize, bold: true, 0.220, 0.937, 0.608);
        }
        else
        {
            DrawText(sb, value, vx, vy, valueSize, bold: true, 1, 1, 1);
        }
    }

    private static double[] ComputeColumnWidths(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows, IReadOnlyList<object?>? totalsRow, double totalWidth)
    {
        double[] weights = new double[headers.Count];
        for (int c = 0; c < headers.Count; c++)
        {
            int maxLen = Math.Max(5, headers[c].Length + 2);
            int sample = Math.Min(rows.Count, 80);
            for (int r = 0; r < sample; r++)
            {
                if (c < rows[r].Count && rows[r][c] is { } v)
                {
                    int len = (Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty).Length;
                    maxLen = Math.Max(maxLen, Math.Min(len + 2, 32));
                }
            }
            if (totalsRow is not null && c < totalsRow.Count && totalsRow[c] is { } tv)
            {
                int tlen = (Convert.ToString(tv, CultureInfo.InvariantCulture) ?? string.Empty).Length;
                maxLen = Math.Max(maxLen, Math.Min(tlen + 2, 32));
            }
            weights[c] = maxLen;
        }
        double sum = Math.Max(1.0, weights.Sum());
        return weights.Select(w => totalWidth * (w / sum)).ToArray();
    }

    private static double EstimateTextWidth(string text, double fontSize, bool bold)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int upper = text.Count(c => char.IsUpper(c));
        double avgFactor = (bold ? 0.59 : 0.53) + ((double)upper / text.Length) * 0.09;
        return text.Length * fontSize * avgFactor;
    }

    private static string FitText(string text, double maxWidth, double fontSize, bool bold)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        string clean = text.Replace('\r', ' ').Replace('\n', ' ').Replace("…", "...", StringComparison.Ordinal).Replace("—", "-", StringComparison.Ordinal).Trim();
        int upper = clean.Count(c => char.IsUpper(c));
        double factor = (bold ? 0.59 : 0.53) + ((double)upper / Math.Max(1, clean.Length)) * 0.09;
        int maxChars = Math.Max(3, (int)Math.Floor(maxWidth / (fontSize * factor)));
        return clean.Length <= maxChars ? clean : clean[..(maxChars - 2)] + "..";
    }

    private static void FillRect(StringBuilder sb, double x, double y, double w, double h, double r, double g, double b)
        => sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F3} {1:F3} {2:F3} rg {3:F2} {4:F2} {5:F2} {6:F2} re f\n", r, g, b, x, y, w, h);

    private static void StrokeRect(StringBuilder sb, double x, double y, double w, double h, double r, double g, double b, double lw)
        => sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F2} w {1:F3} {2:F3} {3:F3} RG {4:F2} {5:F2} {6:F2} {7:F2} re S\n", lw, r, g, b, x, y, w, h);

    private static void StrokeLine(StringBuilder sb, double x1, double y1, double x2, double y2, double r, double g, double b, double lw)
        => sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F2} w {1:F3} {2:F3} {3:F3} RG {4:F2} {5:F2} m {6:F2} {7:F2} l S\n", lw, r, g, b, x1, y1, x2, y2);

    private static void DrawText(StringBuilder sb, string text, double x, double y, double size, bool bold, double r, double g, double b)
    {
        string fontKey = bold ? "/F2" : "/F1";
        sb.AppendFormat(CultureInfo.InvariantCulture, "BT {0} {1:F2} Tf {2:F3} {3:F3} {4:F3} rg {5:F2} {6:F2} Td ({7}) Tj ET\n",
            fontKey, size, r, g, b, x, y, EncodePdfString(text));
    }

    private static string EncodePdfString(string text)
    {
        string normalized = text.Replace("…", "...", StringComparison.Ordinal).Replace("—", "-", StringComparison.Ordinal).Replace("–", "-", StringComparison.Ordinal);
        byte[] bytes = WinAnsi.GetBytes(normalized);
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte ch in bytes)
        {
            if (ch is (byte)'(' or (byte)')' or (byte)'\\')
            {
                sb.Append('\\').Append((char)ch);
            }
            else if (ch >= 32 && ch <= 126)
            {
                sb.Append((char)ch);
            }
            else
            {
                sb.Append('\\').Append(Convert.ToString(ch, 8).PadLeft(3, '0'));
            }
        }
        return sb.ToString();
    }

    private static void WritePdfFile(string filePath, double pageW, double pageH, List<StringBuilder> pageStreams)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        using var ms = new MemoryStream();
        void WriteAscii(string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            ms.Write(b, 0, b.Length);
        }

        WriteAscii("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        var offsets = new List<long> { 0 };

        int pageCount = pageStreams.Count;
        int NextPageObjId(int idx) => 5 + idx * 2;
        int NextStreamObjId(int idx) => 6 + idx * 2;

        offsets.Add(ms.Position);
        WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets.Add(ms.Position);
        string kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{NextPageObjId(i)} 0 R"));
        WriteAscii($"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>\nendobj\n");

        offsets.Add(ms.Position);
        WriteAscii("3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

        offsets.Add(ms.Position);
        WriteAscii("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

        for (int i = 0; i < pageCount; i++)
        {
            int pObj = NextPageObjId(i);
            int sObj = NextStreamObjId(i);
            byte[] streamBytes = Encoding.ASCII.GetBytes(pageStreams[i].ToString());

            offsets.Add(ms.Position);
            WriteAscii(FormattableString.Invariant($"{pObj} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageW:F0} {pageH:F0}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {sObj} 0 R >>\nendobj\n"));

            offsets.Add(ms.Position);
            WriteAscii($"{sObj} 0 obj\n<< /Length {streamBytes.Length} >>\nstream\n");
            ms.Write(streamBytes, 0, streamBytes.Length);
            WriteAscii("\nendstream\nendobj\n");
        }

        long xrefPos = ms.Position;
        WriteAscii($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        for (int i = 1; i < offsets.Count; i++)
        {
            WriteAscii(offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }
        WriteAscii($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n");

        File.WriteAllBytes(filePath, ms.ToArray());
    }
}
