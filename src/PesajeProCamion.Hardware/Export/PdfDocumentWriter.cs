using System.Globalization;
using System.Text;

namespace PesajeProCamion.Hardware.Export;

/// <summary>
/// Generador nativo PDF 1.4 vectorial (zero dependencias externas) con paleta corporativa
/// Petrol Teal (#082D45 / #005781) y Dorado (#CCA969) para informes KPI y tickets comerciales.
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
        IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        const double pageW = 842.0; // A4 Landscape
        const double pageH = 595.0;
        const double margin = 28.0;
        double contentW = pageW - 2 * margin;

        int colCount = Math.Max(1, headers.Count);
        double[] colWidths = ComputeColumnWidths(headers, rows, contentW);
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
            DrawText(sb, title, margin + 12, y - 18, 13.0, bold: true, 1, 1, 1);
            DrawText(sb, subtitle, margin + 12, y - 33, 8.2, bold: false, 0.86, 0.90, 0.92);
            y -= 52;

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
                    DrawText(sb, FitText(kpis[k].Label, cardW - 10, 7.2), cx + 6, y - 14, 7.2, bold: true, 0.35, 0.43, 0.49);
                    DrawText(sb, FitText(kpis[k].Value, cardW - 10, 9.8), cx + 6, y - 29, 9.8, bold: true, 0.031, 0.176, 0.271);
                }
                y -= 46;
            }

            // Table Header
            FillRect(sb, margin, y - 20, contentW, 20, 0.024, 0.224, 0.325);
            double x = margin;
            for (int c = 0; c < headers.Count; c++)
            {
                DrawText(sb, FitText(headers[c], colWidths[c] - 6, 7.8), x + 4, y - 13, 7.8, bold: true, 1, 1, 1);
                x += colWidths[c];
            }
            y -= 20;

            double minY = margin + 24;
            bool alt = false;
            while (rowIndex < rows.Count && y - 16 >= minY)
            {
                IReadOnlyList<object?> r = rows[rowIndex];
                if (alt)
                {
                    FillRect(sb, margin, y - 16, contentW, 16, 0.94, 0.965, 0.97);
                }
                StrokeLine(sb, margin, y - 16, margin + contentW, y - 16, 0.85, 0.89, 0.91, 0.4);

                x = margin;
                for (int c = 0; c < headers.Count; c++)
                {
                    object? val = c < r.Count ? r[c] : null;
                    string txt = val switch
                    {
                        int iv => iv.ToString("N0", CultureInfo.InvariantCulture),
                        long lv => lv.ToString("N0", CultureInfo.InvariantCulture),
                        double dv => dv.ToString("N2", CultureInfo.InvariantCulture),
                        _ => Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty
                    };
                    bool isNumeric = val is int or long or short or double or decimal;
                    string fitted = FitText(txt, colWidths[c] - 6, 7.6);
                    if (isNumeric)
                    {
                        double tw = EstimateTextWidth(fitted, 7.6);
                        double drawX = Math.Max(x + 3, x + colWidths[c] - 4 - tw);
                        DrawText(sb, fitted, drawX, y - 11.5, 7.6, bold: false, 0.09, 0.14, 0.20);
                    }
                    else
                    {
                        DrawText(sb, fitted, x + 4, y - 11.5, 7.6, bold: false, 0.09, 0.14, 0.20);
                    }
                    x += colWidths[c];
                }

                y -= 16;
                rowIndex++;
                alt = !alt;
            }

            if (rows.Count == 0 && pageNum == 1)
            {
                DrawText(sb, "No existen registros para los filtros seleccionados.", margin + 8, y - 18, 9.0, bold: false, 0.4, 0.45, 0.5);
            }

            pages.Add(sb);
        } while (rowIndex < rows.Count);

        for (int i = 0; i < pages.Count; i++)
        {
            StringBuilder sb = pages[i];
            StrokeLine(sb, margin, margin + 16, margin + contentW, margin + 16, 0.78, 0.82, 0.85, 0.6);
            DrawText(sb, $"PesajeProCamion — Generado {DateTime.Now:dd/MM/yyyy HH:mm}", margin, margin + 5, 7.5, bold: false, 0.42, 0.48, 0.54);
            string pgLabel = $"Página {i + 1} de {pages.Count}";
            DrawText(sb, pgLabel, pageW - margin - EstimateTextWidth(pgLabel, 7.5), margin + 5, 7.5, bold: true, 0.031, 0.176, 0.271);
        }

        WritePdfFile(filePath, pageW, pageH, pages);
    }

    public static void WriteSingleTicketPdf(
        string filePath,
        string companyName,
        string companySub,
        string ticketTitle,
        string statusBadge,
        IReadOnlyList<(string Label, string Value)> details,
        int brutoKg,
        int taraKg,
        int netoKg,
        string entradaInfo,
        string salidaInfo,
        string observaciones)
    {
        const double pageW = 595.0; // A4 Portrait
        const double pageH = 842.0;
        const double margin = 42.0;
        double contentW = pageW - 2 * margin;

        var sb = new StringBuilder();
        double y = pageH - margin;

        // Corporate Petrol Teal Banner
        FillRect(sb, margin, y - 62, contentW, 62, 0.031, 0.176, 0.271);
        FillRect(sb, margin, y - 65, contentW, 3.0, 0.800, 0.663, 0.412);
        DrawText(sb, companyName, margin + 14, y - 22, 13.5, bold: true, 1, 1, 1);
        DrawText(sb, companySub, margin + 14, y - 37, 8.8, bold: false, 0.800, 0.663, 0.412);
        DrawText(sb, ticketTitle, margin + 14, y - 54, 11.0, bold: true, 1, 1, 1);
        if (!string.IsNullOrWhiteSpace(statusBadge))
        {
            double bw = EstimateTextWidth(statusBadge, 8.8) + 16;
            FillRect(sb, margin + contentW - bw - 12, y - 28, bw, 18, 0.800, 0.663, 0.412);
            DrawText(sb, statusBadge, margin + contentW - bw - 4, y - 22, 8.5, bold: true, 0.031, 0.176, 0.271);
        }
        y -= 80;

        // Details Box
        double detailsH = Math.Max(140, details.Count * 22 + 22);
        FillRect(sb, margin, y - detailsH, contentW, detailsH, 0.98, 0.99, 0.99);
        StrokeRect(sb, margin, y - detailsH, contentW, detailsH, 0.75, 0.82, 0.85, 0.9);
        double dy = y - 22;
        foreach ((string lbl, string val) in details)
        {
            DrawText(sb, lbl, margin + 14, dy, 9.2, bold: true, 0.35, 0.43, 0.49);
            DrawText(sb, FitText(val, contentW - 170, 9.5), margin + 150, dy, 9.5, bold: false, 0.09, 0.14, 0.20);
            StrokeLine(sb, margin + 12, dy - 6, margin + contentW - 12, dy - 6, 0.90, 0.93, 0.94, 0.4);
            dy -= 22;
        }
        y -= detailsH + 18;

        // Weights Card
        FillRect(sb, margin, y - 92, contentW, 92, 0.031, 0.176, 0.271);
        StrokeRect(sb, margin, y - 92, contentW, 92, 0.800, 0.663, 0.412, 1.2);
        double colW = contentW / 3.0;
        DrawWeightColumn(sb, margin, y, colW, "PESO BRUTO", $"{brutoKg:N0} kg", highlightGold: false);
        DrawWeightColumn(sb, margin + colW, y, colW, "PESO TARA", $"{taraKg:N0} kg", highlightGold: false);
        DrawWeightColumn(sb, margin + colW * 2, y, colW, "PESO NETO", $"{netoKg:N0} kg", highlightGold: true);
        y -= 112;

        // Times Box
        FillRect(sb, margin, y - 72, contentW, 72, 0.96, 0.97, 0.98);
        StrokeRect(sb, margin, y - 72, contentW, 72, 0.75, 0.82, 0.85, 0.8);
        DrawText(sb, "ENTRADA:", margin + 14, y - 22, 9.0, bold: true, 0.000, 0.341, 0.506);
        DrawText(sb, FitText(entradaInfo, contentW - 120, 9.0), margin + 96, y - 22, 9.0, bold: false, 0.1, 0.15, 0.2);
        DrawText(sb, "SALIDA:", margin + 14, y - 44, 9.0, bold: true, 0.000, 0.341, 0.506);
        DrawText(sb, FitText(salidaInfo, contentW - 120, 9.0), margin + 96, y - 44, 9.0, bold: false, 0.1, 0.15, 0.2);
        DrawText(sb, "OBS.:", margin + 14, y - 62, 8.8, bold: true, 0.35, 0.43, 0.49);
        DrawText(sb, FitText(observaciones, contentW - 120, 8.8), margin + 96, y - 62, 8.8, bold: false, 0.1, 0.15, 0.2);

        StrokeLine(sb, margin, margin + 18, margin + contentW, margin + 18, 0.78, 0.82, 0.85, 0.6);
        DrawText(sb, $"PesajeProCamion — Ticket emitido {DateTime.Now:dd/MM/yyyy HH:mm}", margin, margin + 6, 8.0, bold: false, 0.42, 0.48, 0.54);

        WritePdfFile(filePath, pageW, pageH, [sb]);
    }

    private static void DrawWeightColumn(StringBuilder sb, double x, double topY, double w, string label, string value, bool highlightGold)
    {
        double lx = x + (w - EstimateTextWidth(label, 9.0)) / 2.0;
        double vx = x + (w - EstimateTextWidth(value, 15.0)) / 2.0;
        DrawText(sb, label, lx, topY - 26, 9.0, bold: true, 0.800, 0.663, 0.412);
        if (highlightGold)
        {
            DrawText(sb, value, vx, topY - 62, 15.0, bold: true, 0.220, 0.937, 0.608);
        }
        else
        {
            DrawText(sb, value, vx, topY - 62, 15.0, bold: true, 1, 1, 1);
        }
    }

    private static double[] ComputeColumnWidths(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows, double totalWidth)
    {
        double[] weights = new double[headers.Count];
        for (int c = 0; c < headers.Count; c++)
        {
            int maxLen = Math.Max(4, headers[c].Length + 2);
            int sample = Math.Min(rows.Count, 80);
            for (int r = 0; r < sample; r++)
            {
                if (c < rows[r].Count && rows[r][c] is { } v)
                {
                    int len = (Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty).Length;
                    maxLen = Math.Max(maxLen, Math.Min(len + 1, 28));
                }
            }
            weights[c] = maxLen;
        }
        double sum = weights.Sum();
        return weights.Select(w => totalWidth * (w / sum)).ToArray();
    }

    private static double EstimateTextWidth(string text, double fontSize) => text.Length * fontSize * 0.52;

    private static string FitText(string text, double maxWidth, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        string clean = text.Replace('\r', ' ').Replace('\n', ' ').Replace("…", "...", StringComparison.Ordinal).Replace("—", "-", StringComparison.Ordinal).Trim();
        int maxChars = Math.Max(3, (int)Math.Floor(maxWidth / (fontSize * 0.52)));
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
        var offsets = new List<long> { 0 }; // object 0 unused

        int pageCount = pageStreams.Count;
        // Objects: 1=Catalog, 2=Pages, 3=Font F1, 4=Font F2, then pairs (PageObj, StreamObj) starting at 5
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
