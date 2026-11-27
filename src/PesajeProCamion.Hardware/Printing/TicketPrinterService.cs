using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Validation;
using PesajeProCamion.Hardware.Export;

namespace PesajeProCamion.Hardware.Printing;

/// <summary>Motor estandarizado de generación e impresión de tickets comerciales y boletos MOP (Vista Previa/Impresión, PDF y Excel página completa, más RAW).</summary>
public static partial class TicketPrinterService
{
    /// <summary>Construye la lista ordenada de campos visibles del ticket comercial según preferencias activas.</summary>
    public static IReadOnlyList<(string Label, string Value)> BuildVisibleFieldRows(Pesaje p, TicketFieldVisibility? visibility = null)
    {
        ArgumentNullException.ThrowIfNull(p);
        TicketFieldVisibility v = visibility ?? TicketFieldVisibility.Default;
        var rows = new List<(string, string)>(10);

        if (v.MostrarPatente)
        {
            string pat = string.IsNullOrWhiteSpace(p.Acoplado) ? p.Patente : $"{p.Patente}   (Acoplado: {p.Acoplado})";
            rows.Add(("PATENTE / ACOPLADO", string.IsNullOrWhiteSpace(pat) ? "-" : pat));
        }
        if (v.MostrarCliente)
        {
            string val = $"{p.Cliente} - {p.NombreCliente}".Trim(' ', '-');
            rows.Add(("CLIENTE", string.IsNullOrWhiteSpace(val) ? "-" : val));
        }
        if (v.MostrarProducto)
        {
            string val = $"{p.Producto} - {p.NombreProducto}".Trim(' ', '-');
            rows.Add(("PRODUCTO", string.IsNullOrWhiteSpace(val) ? "-" : val));
        }
        if (v.MostrarOrigen)
        {
            string val = $"{p.Origen} - {p.NombreOrigen}".Trim(' ', '-');
            rows.Add(("ORIGEN / PROVEEDOR", string.IsNullOrWhiteSpace(val) ? "-" : val));
        }
        if (v.MostrarDestino)
        {
            string val = $"{p.Destino} - {p.NombreDestino}".Trim(' ', '-');
            rows.Add(("DESTINO", string.IsNullOrWhiteSpace(val) ? "-" : val));
        }
        if (v.MostrarTransportista)
        {
            string val = $"{p.Transportista} - {p.NombreTransportista}".Trim(' ', '-');
            rows.Add(("TRANSPORTISTA", string.IsNullOrWhiteSpace(val) ? "-" : val));
        }
        if (v.MostrarChofer)
        {
            string val = $"{p.Chofer} - {p.NombreChofer}".Trim(' ', '-');
            rows.Add(("CHOFER", string.IsNullOrWhiteSpace(val) ? "-" : val));
        }
        if (v.MostrarGuiaDespacho)
        {
            rows.Add(("GUÍA DE DESPACHO", string.IsNullOrWhiteSpace(p.GuiaDespacho) ? "-" : p.GuiaDespacho));
        }
        if (v.MostrarFardos)
        {
            rows.Add(("CANTIDAD DE FARDOS", p.Fardos > 0 ? p.Fardos.ToString("N0", CultureInfo.InvariantCulture) : "0"));
        }

        return rows;
    }

    private static (
        string Company,
        string CompanySub,
        string TipoBadge,
        string FolioText,
        string? EstadoText,
        IReadOnlyList<(string Label, string Value)> Details,
        string EntradaFechaHora,
        string EntradaOp,
        string SalidaFechaHora,
        string SalidaOp,
        string Observaciones,
        string BadgeTone) BuildStandardTicketPayload(
        Pesaje p,
        BasculaInfo bascula,
        ConfiguracionSistema? cfg,
        bool isEnProceso,
        TicketFieldVisibility? visibility)
    {
        TicketFieldVisibility v = visibility ?? TicketFieldVisibility.Default;
        string company = string.IsNullOrWhiteSpace(bascula.NombreEmpresa) ? (cfg?.Titulo1 ?? "PESAJE DE CAMIONES") : bascula.NombreEmpresa;
        string companySub = string.IsNullOrWhiteSpace(bascula.Rut) ? bascula.Direccion : $"{bascula.Direccion}  |  RUT: {bascula.Rut}";
        string tipoBadge = TicketCustomizationHelper.FormatTipoPesajeBadge(p.TipoPesaje, p.Anulado, isEnProceso);
        string folioText = TicketCustomizationHelper.FormatFolioHeader(p.FolioPesaje);
        string? estadoText = v.MostrarEstado ? TicketCustomizationHelper.FormatEstadoTicket(p.Anulado, isEnProceso) : null;

        var details = new List<(string Label, string Value)>();
        foreach ((string lbl, string val) in BuildVisibleFieldRows(p, v))
        {
            details.Add((lbl, val));
        }

        string entFh = $"{p.FechaIngreso:dd-MM-yyyy} {p.HoraIngreso}".Trim();
        string entOp = string.IsNullOrWhiteSpace(p.NombreUsuarioIngreso) ? "-" : p.NombreUsuarioIngreso;
        string salFh = isEnProceso ? "EN TRÁNSITO (sin salida)" : $"{p.FechaSalida:dd-MM-yyyy} {p.HoraSalida}".Trim();
        string salOp = isEnProceso || string.IsNullOrWhiteSpace(p.NombreUsuarioSalida) ? "-" : p.NombreUsuarioSalida;
        string obsRaw = $"{p.Observacion1} {p.Observacion2}".Trim();
        string obs = string.IsNullOrWhiteSpace(obsRaw) ? "-" : obsRaw;
        string badgeTone = p.Anulado ? "DANGER" : (isEnProceso ? "AMBER" : "EMERALD");

        return (company, companySub, tipoBadge, folioText, estadoText, details, entFh, entOp, salFh, salOp, obs, badgeTone);
    }

    public static void ExportCommercialTicketToPdf(
        string filePath,
        Pesaje p,
        BasculaInfo bascula,
        ConfiguracionSistema? cfg = null,
        bool isEnProceso = false,
        TicketFieldVisibility? visibility = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(bascula);

        TicketFieldVisibility v = visibility ?? TicketFieldVisibility.Default;
        var pay = BuildStandardTicketPayload(p, bascula, cfg, isEnProceso, v);
        var pdfDetails = pay.Details.Select(d => ($"{d.Label}:", d.Value)).ToList();

        PdfDocumentWriter.WriteSingleTicketPdf(
            filePath,
            pay.Company,
            pay.CompanySub,
            "TICKET DE PESAJE COMERCIAL",
            pay.TipoBadge,
            pay.FolioText,
            pay.EstadoText,
            pdfDetails,
            p.Bruto,
            p.Tara,
            isEnProceso ? 0 : p.Neto,
            isEnProceso,
            v.MostrarTiemposOperador,
            pay.EntradaFechaHora,
            pay.EntradaOp,
            pay.SalidaFechaHora,
            pay.SalidaOp,
            v.MostrarObservaciones,
            pay.Observaciones,
            pay.BadgeTone);
    }

    public static void ExportCommercialTicketToXlsx(
        string filePath,
        Pesaje p,
        BasculaInfo bascula,
        ConfiguracionSistema? cfg = null,
        bool isEnProceso = false,
        TicketFieldVisibility? visibility = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(bascula);

        TicketFieldVisibility v = visibility ?? TicketFieldVisibility.Default;
        var pay = BuildStandardTicketPayload(p, bascula, cfg, isEnProceso, v);

        DataExporter.ExportSingleTicketToXlsx(
            filePath,
            pay.Company,
            pay.CompanySub,
            "TICKET DE PESAJE COMERCIAL",
            pay.TipoBadge,
            pay.FolioText,
            pay.EstadoText,
            pay.Details,
            p.Bruto,
            p.Tara,
            isEnProceso ? 0 : p.Neto,
            isEnProceso,
            v.MostrarTiemposOperador,
            pay.EntradaFechaHora,
            pay.EntradaOp,
            pay.SalidaFechaHora,
            pay.SalidaOp,
            v.MostrarObservaciones,
            pay.Observaciones);
    }

    public static string BuildRawTicketText(
        Pesaje p,
        BasculaInfo bascula,
        ConfiguracionSistema? cfg = null,
        TicketFieldVisibility? visibility = null,
        bool isEnProceso = false)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(bascula);

        TicketFieldVisibility v = visibility ?? TicketFieldVisibility.Default;
        var sb = new StringBuilder();
        string company = string.IsNullOrWhiteSpace(bascula.NombreEmpresa) ? (cfg?.Titulo1 ?? "PESAJE DE CAMIONES") : bascula.NombreEmpresa;
        sb.AppendLine("============================================================");
        sb.AppendLine($"  {company}");
        if (!string.IsNullOrWhiteSpace(bascula.Direccion))
        {
            sb.AppendLine($"  {bascula.Direccion}");
        }
        sb.AppendLine($"  TICKET DE PESAJE COMERCIAL  [{TicketCustomizationHelper.FormatTipoPesajeBadge(p.TipoPesaje, p.Anulado, isEnProceso)}]");
        sb.AppendLine("============================================================");
        string folioLine = v.MostrarEstado
            ? $"{TicketCustomizationHelper.FormatFolioHeader(p.FolioPesaje)}       ESTADO: {TicketCustomizationHelper.FormatEstadoTicket(p.Anulado, isEnProceso)}"
            : TicketCustomizationHelper.FormatFolioHeader(p.FolioPesaje);
        sb.AppendLine(folioLine);
        foreach ((string lbl, string val) in BuildVisibleFieldRows(p, v))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"{lbl,-20}: {val}");
        }
        if (v.MostrarTiemposOperador)
        {
            sb.AppendLine("------------------------------------------------------------");
            sb.AppendLine(CultureInfo.InvariantCulture, $"ENTRADA             : {p.FechaIngreso:dd/MM/yyyy} {p.HoraIngreso}   OP: {p.NombreUsuarioIngreso}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"SALIDA              : {(isEnProceso ? "EN TRÁNSITO" : $"{p.FechaSalida:dd/MM/yyyy} {p.HoraSalida}")}   OP: {(isEnProceso ? "-" : p.NombreUsuarioSalida)}");
        }
        sb.AppendLine("------------------------------------------------------------");
        sb.AppendLine(CultureInfo.InvariantCulture, $"PESO BRUTO          : {p.Bruto,10:N0} Kg");
        sb.AppendLine(CultureInfo.InvariantCulture, $"PESO TARA           : {p.Tara,10:N0} Kg {(p.TaraRegistrada ? "(TARA REG.)" : string.Empty)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"PESO NETO           : {(isEnProceso ? 0 : p.Neto),10:N0} Kg");
        if (v.MostrarObservaciones && (!string.IsNullOrWhiteSpace(p.Observacion1) || !string.IsNullOrWhiteSpace(p.Observacion2)))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"OBSERVACIONES       : {$"{p.Observacion1} {p.Observacion2}".Trim()}");
        }
        sb.AppendLine("============================================================");
        return sb.ToString();
    }

    public static string BuildRawMopBoletoText(PesajeMop m, BasculaInfo bascula, string nombreTransp, string nombreChofer)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(bascula);

        var sb = new StringBuilder();
        sb.AppendLine("============================================================");
        sb.AppendLine("        BOLETO OFICIAL PARA CONTROL CAMINERO M.O.P.         ");
        sb.AppendLine("          Ley N° 19.171/92 - Decreto MOP N° 18/93           ");
        sb.AppendLine("------------------------------------------------------------");
        sb.AppendLine(CultureInfo.InvariantCulture, $"RAZON SOCIAL : {bascula.NombreEmpresa}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"BASCULA      : {bascula.Descripcion}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"FOLIO MOP    : {m.FolioMop:D6}     FECHA: {m.Fecha:dd/MM/yyyy} {m.Hora}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"PATENTE      : {m.Patente,-10} ACOPLADO: {m.Acoplado}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"TRANSPORT.   : {nombreTransp}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"CHOFER       : {nombreChofer}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"TIPO MOP     : {m.TipoMop,-6}     LARGO   : {m.Largo} m");
        sb.AppendLine("------------------------------------------------------------");
        sb.AppendLine(" EJE   TIPO    PESO KG   LIMITE KG   TANDEM   PESO TD  LIM.");
        foreach (PesajeMopDetalle d in m.Detalles.OrderBy(x => x.EjeNumero))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {d.EjeNumero,2}   {d.EjeTipo,2}      {d.EjePeso,7:N0}    {d.EjeLimite,7:N0}     {d.TandemNumero,2}     {d.TandemPeso,7:N0}  {d.TandemLimite,7:N0}");
        }
        sb.AppendLine("------------------------------------------------------------");
        sb.AppendLine(CultureInfo.InvariantCulture, $"PESO BRUTO   : {m.Bruto,10:N0} Kg   MAXIMO : {m.Maximo,10:N0} Kg");
        sb.AppendLine(CultureInfo.InvariantCulture, $"SOBREPESO    : {m.Sobrepeso,10:N0} Kg");
        sb.AppendLine(CultureInfo.InvariantCulture, $"ESTADO       : {(m.Estado == EstadoNormaMop.FueraDeNorma ? "FUERA DE NORMA" : "DENTRO DE NORMA")}");
        sb.AppendLine("============================================================");
        return sb.ToString();
    }

    public static PrintDocument CreateCommercialTicketDocument(
        Pesaje p,
        BasculaInfo bascula,
        ConfiguracionSistema? cfg = null,
        short copies = 1,
        Image? corporateLogo = null,
        TicketFieldVisibility? visibility = null,
        bool isEnProceso = false)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(bascula);

        var doc = new PrintDocument { DocumentName = $"Ticket_Pesaje_{p.FolioPesaje}" };
        doc.DefaultPageSettings.Margins = new Margins(36, 36, 36, 36);
        doc.PrinterSettings.Copies = Math.Clamp(copies, (short)1, (short)20);
        doc.PrintPage += (_, e) => RenderTicketPage(e, p, bascula, cfg, corporateLogo, visibility, isEnProceso);
        return doc;
    }

    public static PrintDocument CreateStandardReportDocument(StandardTableReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var doc = new PrintDocument { DocumentName = report.SheetName };
        doc.DefaultPageSettings.Landscape = report.Landscape;
        doc.DefaultPageSettings.Margins = new Margins(28, 28, 30, 30);

        int rowIndex = 0;
        int pageNum = 0;
        bool totalsDrawn = report.TotalsRow is null;

        doc.PrintPage += (_, e) =>
        {
            Graphics? g = e.Graphics;
            if (g is null) return;
            pageNum++;
            RenderStandardReportPage(e, g, report, ref rowIndex, ref totalsDrawn, pageNum);
        };
        return doc;
    }

    private static void RenderStandardReportPage(
        PrintPageEventArgs e,
        Graphics g,
        StandardTableReport report,
        ref int rowIndex,
        ref bool totalsDrawn,
        int pageNum)
    {
        using var fTitle = new Font("Segoe UI", 13f, FontStyle.Bold);
        using var fSub = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        using var fKpiLbl = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        using var fKpiVal = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var fHdr = new Font("Segoe UI", 8.25f, FontStyle.Bold);
        using var fCell = new Font("Segoe UI", 8f, FontStyle.Regular);
        using var fCellBold = new Font("Segoe UI", 8.25f, FontStyle.Bold);
        using var fNetoBold = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        using var fFoot = new Font("Segoe UI", 7.75f, FontStyle.Regular);

        using var navyDark = new SolidBrush(Color.FromArgb(8, 45, 69));
        using var slateHdr = new SolidBrush(Color.FromArgb(6, 57, 83));
        using var goldBrush = new SolidBrush(Color.FromArgb(204, 169, 105));
        using var kpiBg = new SolidBrush(Color.FromArgb(245, 248, 250));
        using var altBg = new SolidBrush(Color.FromArgb(240, 246, 247));
        using var totBg = new SolidBrush(Color.FromArgb(230, 241, 245));
        using var netoBrush = new SolidBrush(Color.FromArgb(6, 95, 70));
        using var linePen = new Pen(Color.FromArgb(204, 218, 224), 0.7f);
        using var boxPen = new Pen(Color.FromArgb(148, 172, 184), 1f);

        float left = e.MarginBounds.Left;
        float top = e.MarginBounds.Top;
        float width = e.MarginBounds.Width;
        float bottom = e.MarginBounds.Bottom;
        float y = top;

        // 1. Header Banner
        g.FillRectangle(navyDark, left, y, width, 44f);
        g.FillRectangle(goldBrush, left, y + 41.5f, width, 2.5f);
        g.DrawString(report.Title, fTitle, Brushes.White, new RectangleF(left + 10f, y + 4f, width - 20f, 22f));
        g.DrawString(report.Subtitle, fSub, Brushes.LightSteelBlue, new RectangleF(left + 10f, y + 25f, width - 20f, 16f));
        y += 50f;

        // 2. KPI Strip page 1
        if (pageNum == 1 && report.Kpis.Count > 0)
        {
            int kCount = Math.Min(report.Kpis.Count, 5);
            float gap = 8f;
            float cw = (width - gap * (kCount - 1)) / kCount;
            for (int k = 0; k < kCount; k++)
            {
                float cx = left + k * (cw + gap);
                g.FillRectangle(kpiBg, cx, y, cw, 38f);
                g.DrawRectangle(boxPen, cx, y, cw, 38f);
                g.DrawString(report.Kpis[k].Label, fKpiLbl, Brushes.SlateGray, new RectangleF(cx + 6f, y + 4f, cw - 12f, 14f));
                g.DrawString(report.Kpis[k].Value, fKpiVal, navyDark, new RectangleF(cx + 6f, y + 18f, cw - 12f, 18f));
            }
            y += 46f;
        }

        // Column widths proportional across page width
        int colCount = Math.Max(1, report.Headers.Count);
        float[] colWidths = ComputePrintColWidths(report.Headers, report.Rows, report.TotalsRow, width);
        var boldSet = new HashSet<int>();
        var netoSet = report.BoldHighlightColumnIndices is not null ? new HashSet<int>(report.BoldHighlightColumnIndices) : new HashSet<int>();
        for (int c = 0; c < report.Headers.Count; c++)
        {
            if (report.Headers[c].Contains("Bruto", StringComparison.OrdinalIgnoreCase) || report.Headers[c].Contains("Tara", StringComparison.OrdinalIgnoreCase))
                boldSet.Add(c);
            else if (report.Headers[c].Contains("Neto", StringComparison.OrdinalIgnoreCase) && !report.Headers[c].Contains("%", StringComparison.Ordinal))
                netoSet.Add(c);
        }

        // 3. Repeating Table Header
        g.FillRectangle(slateHdr, left, y, width, 22f);
        float hx = left;
        using var hdrFmt = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        for (int c = 0; c < colCount; c++)
        {
            g.DrawString(report.Headers[c], fHdr, Brushes.White, new RectangleF(hx + 3f, y, colWidths[c] - 6f, 22f), hdrFmt);
            hx += colWidths[c];
        }
        y += 22f;

        float minBottom = bottom - 22f;
        const float rowH = 19f;
        bool alt = false;
        while (rowIndex < report.Rows.Count && y + rowH <= minBottom)
        {
            if (alt) g.FillRectangle(altBg, left, y, width, rowH);
            g.DrawLine(linePen, left, y + rowH, left + width, y + rowH);
            DrawPrintTableRow(g, report.Rows[rowIndex], colCount, left, y, rowH, colWidths, isTotals: false, report.FolioColumnIndex, boldSet, netoSet, fCell, fCellBold, fNetoBold, navyDark, netoBrush);
            y += rowH;
            rowIndex++;
            alt = !alt;
        }

        if (rowIndex >= report.Rows.Count && !totalsDrawn && report.TotalsRow is not null && y + 23f <= minBottom)
        {
            g.FillRectangle(totBg, left, y, width, 23f);
            g.DrawRectangle(boxPen, left, y, width, 23f);
            DrawPrintTableRow(g, report.TotalsRow, colCount, left, y, 23f, colWidths, isTotals: true, folioColIdx: null, boldSet, netoSet, fCellBold, fCellBold, fNetoBold, navyDark, netoBrush);
            totalsDrawn = true;
        }

        g.DrawLine(boxPen, left, bottom - 16f, left + width, bottom - 16f);
        g.DrawString($"PesajeProCamion — Generado {DateTime.Now:dd/MM/yyyy HH:mm}", fFoot, Brushes.DimGray, left, bottom - 13f);
        using var rFootFmt = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString($"Página {pageNum}", fFoot, navyDark, new RectangleF(left, bottom - 13f, width, 15f), rFootFmt);

        e.HasMorePages = rowIndex < report.Rows.Count || !totalsDrawn;
    }

    private static void DrawPrintTableRow(
        Graphics g,
        IReadOnlyList<object?> row,
        int colCount,
        float startX,
        float y,
        float rowH,
        float[] colWidths,
        bool isTotals,
        int? folioColIdx,
        HashSet<int> boldSet,
        HashSet<int> netoSet,
        Font fCell,
        Font fBold,
        Font fNeto,
        Brush navyBrush,
        Brush netoBrush)
    {
        float x = startX;
        using var lFmt = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var rFmt = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };

        for (int c = 0; c < colCount; c++)
        {
            object? val = c < row.Count ? row[c] : null;
            bool isFolio = !isTotals && folioColIdx == c && val is int;
            string txt = isFolio
                ? Convert.ToInt32(val, CultureInfo.InvariantCulture).ToString("D6", CultureInfo.InvariantCulture)
                : val switch
                {
                    int iv => iv.ToString("N0", CultureInfo.InvariantCulture),
                    long lv => lv.ToString("N0", CultureInfo.InvariantCulture),
                    double dv => dv.ToString("N2", CultureInfo.InvariantCulture),
                    _ => Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty
                };

            bool isNeto = netoSet.Contains(c);
            bool isBold = isTotals || isFolio || isNeto || boldSet.Contains(c);
            Font fUse = isNeto ? fNeto : (isBold ? fBold : fCell);
            Brush bUse = isFolio ? navyBrush : (isNeto ? netoBrush : Brushes.Black);
            bool isNumeric = !isFolio && val is int or long or short or double or decimal;

            g.DrawString(txt, fUse, bUse, new RectangleF(x + 3f, y, colWidths[c] - 6f, rowH), isNumeric ? rFmt : lFmt);
            x += colWidths[c];
        }
    }

    private static float[] ComputePrintColWidths(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows, IReadOnlyList<object?>? totals, float totalWidth)
    {
        float[] weights = new float[headers.Count];
        for (int c = 0; c < headers.Count; c++)
        {
            int maxLen = Math.Max(5, headers[c].Length + 2);
            int sample = Math.Min(rows.Count, 80);
            for (int r = 0; r < sample; r++)
            {
                if (c < rows[r].Count && rows[r][c] is { } v)
                    maxLen = Math.Max(maxLen, Math.Min((Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty).Length + 2, 30));
            }
            if (totals is not null && c < totals.Count && totals[c] is { } tv)
                maxLen = Math.Max(maxLen, Math.Min((Convert.ToString(tv, CultureInfo.InvariantCulture) ?? string.Empty).Length + 2, 30));
            weights[c] = maxLen;
        }
        float sum = Math.Max(1f, weights.Sum());
        return weights.Select(w => totalWidth * (w / sum)).ToArray();
    }

    public static PrintDocument CreateMopBoletoDocument(PesajeMop m, BasculaInfo bascula, string nombreTransp, string nombreChofer)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(bascula);

        var doc = new PrintDocument { DocumentName = $"Boleto_MOP_{m.FolioMop}" };
        doc.PrintPage += (_, e) =>
        {
            Graphics? g = e.Graphics;
            if (g is null)
            {
                return;
            }
            string raw = BuildRawMopBoletoText(m, bascula, nombreTransp, nombreChofer);
            using var font = new Font("Consolas", 10.5f, FontStyle.Regular);
            g.DrawString(raw, font, Brushes.Black, new RectangleF(40, 40, e.MarginBounds.Width, e.MarginBounds.Height));
            e.HasMorePages = false;
        };
        return doc;
    }

    /// <summary>
    /// Renderiza ticket a página completa idéntico en estructura y estilo a PDF/Excel:
    /// Banner Petrol Teal + Tipo Pesaje destacado sin recortes + N° Folio en negrita + Estado opcional +
    /// tabla proporcional + Bruto/Tara grandes en negrita y NETO aún mayor en negrita + firmas y pie.
    /// </summary>
    private static void RenderTicketPage(
        PrintPageEventArgs e,
        Pesaje p,
        BasculaInfo bascula,
        ConfiguracionSistema? cfg,
        Image? corporateLogo,
        TicketFieldVisibility? visibility = null,
        bool isEnProceso = false)
    {
        Graphics? g = e.Graphics;
        if (g is null) return;

        TicketFieldVisibility v = visibility ?? TicketFieldVisibility.Default;
        var pay = BuildStandardTicketPayload(p, bascula, cfg, isEnProceso, v);

        using var fBannerTitle = new Font("Segoe UI", 14.5f, FontStyle.Bold);
        using var fComp = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var fSub = new Font("Segoe UI", 9f, FontStyle.Regular);
        using var fBadge = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var fFolio = new Font("Segoe UI", 16f, FontStyle.Bold);
        using var fEstado = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var fFieldLbl = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var fFieldVal = new Font("Segoe UI", 10.25f, FontStyle.Regular);
        using var fWeightLbl = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var fWeightBt = new Font("Segoe UI", 18.5f, FontStyle.Bold);
        using var fWeightNeto = new Font("Segoe UI", 23.5f, FontStyle.Bold);
        using var fSec = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var fSmall = new Font("Segoe UI", 8.25f, FontStyle.Regular);

        using var navyDark = new SolidBrush(Color.FromArgb(8, 45, 69));
        using var tealPrimary = new SolidBrush(Color.FromArgb(0, 87, 129));
        using var goldBrush = new SolidBrush(Color.FromArgb(204, 169, 105));
        using var rowAltBrush = new SolidBrush(Color.FromArgb(241, 247, 249));
        using var folioBgBrush = new SolidBrush(Color.FromArgb(238, 245, 248));
        using var neonBrush = new SolidBrush(Color.FromArgb(56, 239, 155));
        using var mutedBrush = new SolidBrush(Color.FromArgb(84, 104, 118));
        using var borderPen = new Pen(Color.FromArgb(184, 204, 212), 1.1f);
        using var linePen = new Pen(Color.FromArgb(224, 233, 237), 0.9f);
        using var goldPen = new Pen(Color.FromArgb(204, 169, 105), 2f);
        using var centerFmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        float left = e.MarginBounds.Left;
        float top = e.MarginBounds.Top;
        float width = e.MarginBounds.Width;
        float bottom = e.MarginBounds.Bottom;
        float y = top;

        // 1. Banner Corporativo Petrol Teal + Barra Dorada
        const float bannerH = 84f;
        g.FillRectangle(navyDark, left, y, width, bannerH);
        g.FillRectangle(goldBrush, left, y + bannerH - 3f, width, 3f);

        float textLeft = left + 14f;
        if (corporateLogo is not null)
        {
            g.FillRectangle(Brushes.White, left + 12f, y + 12f, 114f, 58f);
            g.DrawImage(corporateLogo, new RectangleF(left + 16f, y + 15f, 106f, 52f));
            textLeft = left + 140f;
        }

        // Badge Tipo de Pesaje superior derecho (medido exacto con margen extra para evitar recortes)
        float badgeW = Math.Clamp(g.MeasureString(pay.TipoBadge, fBadge).Width + 28f, 160f, width * 0.40f);
        Color badgeBg = p.Anulado
            ? Color.FromArgb(198, 40, 40)
            : (isEnProceso ? Color.FromArgb(217, 119, 6) : Color.FromArgb(14, 159, 110));
        using var badgeBrush = new SolidBrush(badgeBg);
        var badgeRect = new RectangleF(left + width - badgeW - 14f, y + 14f, badgeW, 30f);
        g.FillRectangle(badgeBrush, badgeRect);
        using var badgeBorderPen = new Pen(Color.FromArgb(204, 169, 105), 1.1f);
        g.DrawRectangle(badgeBorderPen, badgeRect.X, badgeRect.Y, badgeRect.Width, badgeRect.Height);
        g.DrawString(pay.TipoBadge, fBadge, Brushes.White, badgeRect, centerFmt);

        g.DrawString("TICKET DE PESAJE COMERCIAL", fBannerTitle, Brushes.White, textLeft, y + 12f);
        g.DrawString(pay.Company, fComp, goldBrush, textLeft, y + 38f);
        g.DrawString(pay.CompanySub, fSub, Brushes.LightSteelBlue, new RectangleF(textLeft, y + 58f, width - (textLeft - left) - 16f, 18f));
        y += bannerH + 12f;

        // 2. Franja N° Folio (Negrita) + Tipo Pesaje / Estado Opcional
        const float folioBarH = 44f;
        g.FillRectangle(folioBgBrush, left, y, width, folioBarH);
        g.DrawRectangle(borderPen, left, y, width, folioBarH);
        g.DrawString(pay.FolioText, fFolio, navyDark, left + 14f, y + 8f);

        if (!string.IsNullOrWhiteSpace(pay.EstadoText))
        {
            string estLabel = $"Estado: {pay.EstadoText}";
            float ew = Math.Max(136f, g.MeasureString(estLabel, fEstado).Width + 24f);
            var estRect = new RectangleF(left + width - ew - 12f, y + 9f, ew, 26f);
            Color estBg = p.Anulado ? Color.FromArgb(250, 228, 228) : Color.FromArgb(228, 246, 238);
            Color estFg = p.Anulado ? Color.FromArgb(186, 36, 36) : Color.FromArgb(10, 112, 74);
            using var eBg = new SolidBrush(estBg);
            using var eFg = new SolidBrush(estFg);
            using var ePen = new Pen(estFg, 1f);
            g.FillRectangle(eBg, estRect);
            g.DrawRectangle(ePen, estRect.X, estRect.Y, estRect.Width, estRect.Height);
            g.DrawString(estLabel, fEstado, eFg, estRect, centerFmt);
        }
        else
        {
            using var rightFmt = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            g.DrawString(pay.TipoBadge, fEstado, tealPrimary, new RectangleF(left + width * 0.45f, y, width * 0.55f - 14f, folioBarH), rightFmt);
        }
        y += folioBarH + 12f;

        // Cálculos de relleno completo vertical hasta pie de página
        const float weightsH = 114f;
        float timesObsH = (v.MostrarTiemposOperador ? 92f : 0f) + (v.MostrarObservaciones ? 48f : 0f) + (v.MostrarTiemposOperador && v.MostrarObservaciones ? 8f : 0f);
        const float signFooterH = 74f;
        float reservedBottom = weightsH + 14f + (timesObsH > 0 ? timesObsH + 14f : 0f) + signFooterH;
        float availDetailsH = Math.Max(150f, bottom - y - reservedBottom);
        int fieldRows = Math.Max(1, pay.Details.Count);
        float rowH = Math.Clamp((availDetailsH - 12f) / fieldRows, 24f, 44f);
        float detailsH = Math.Max(availDetailsH, fieldRows * rowH + 12f);

        // 3. Tabla de Campos Visibles proporcionada
        g.FillRectangle(Brushes.White, left, y, width, detailsH);
        g.DrawRectangle(borderPen, left, y, width, detailsH);
        float fy = y + 6f;
        for (int i = 0; i < pay.Details.Count; i++)
        {
            if (i % 2 == 1)
            {
                g.FillRectangle(rowAltBrush, left + 2f, fy, width - 4f, rowH);
            }
            g.DrawLine(linePen, left + 10f, fy + rowH, left + width - 10f, fy + rowH);
            using var vCenter = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            g.DrawString($"{pay.Details[i].Label}:", fFieldLbl, mutedBrush, new RectangleF(left + 16f, fy, 184f, rowH), vCenter);
            g.DrawString(pay.Details[i].Value, fFieldVal, Brushes.Black, new RectangleF(left + 204f, fy, width - 220f, rowH), vCenter);
            fy += rowH;
        }
        y += detailsH + 14f;

        // 4. Bloque de Pesos: Bruto/Tara grandes en Negrita, NETO aún más grande en Negrita
        g.FillRectangle(navyDark, left, y, width, weightsH);
        g.DrawRectangle(goldPen, left, y, width, weightsH);
        float colW = width / 3f;
        using var divPen = new Pen(Color.FromArgb(35, 92, 126), 1f);
        g.DrawLine(divPen, left + colW, y + 12f, left + colW, y + weightsH - 12f);
        g.DrawLine(divPen, left + colW * 2f, y + 12f, left + colW * 2f, y + weightsH - 12f);

        DrawPrintWeightColumn(g, new RectangleF(left, y, colW, weightsH), p.TaraRegistrada ? "PESO BRUTO" : "PESO BRUTO", $"{p.Bruto:N0} kg", fWeightLbl, fWeightBt, goldBrush, Brushes.White, centerFmt);
        DrawPrintWeightColumn(g, new RectangleF(left + colW, y, colW, weightsH), p.TaraRegistrada ? "PESO TARA (REG.)" : "PESO TARA", $"{p.Tara:N0} kg", fWeightLbl, fWeightBt, goldBrush, Brushes.White, centerFmt);
        DrawPrintWeightColumn(g, new RectangleF(left + colW * 2f, y, colW, weightsH), "PESO NETO", isEnProceso ? "PENDIENTE" : $"{p.Neto:N0} kg", fWeightLbl, fWeightNeto, goldBrush, neonBrush, centerFmt);
        y += weightsH + 14f;

        // 5. Bloque Tiempos y Observaciones
        if (timesObsH > 0)
        {
            g.FillRectangle(rowAltBrush, left, y, width, timesObsH);
            g.DrawRectangle(borderPen, left, y, width, timesObsH);
            float ty = y + 10f;
            if (v.MostrarTiemposOperador)
            {
                float halfW = width / 2f;
                g.DrawString("ENTRADA A PLANTA", fSec, tealPrimary, left + 14f, ty);
                g.DrawString($"Fecha / Hora :  {pay.EntradaFechaHora}", fFieldVal, Brushes.Black, left + 14f, ty + 22f);
                g.DrawString($"Operador     :  {pay.EntradaOp}", fFieldVal, mutedBrush, left + 14f, ty + 44f);

                g.DrawLine(linePen, left + halfW, y + 8f, left + halfW, y + 82f);

                g.DrawString("SALIDA DE PLANTA", fSec, tealPrimary, left + halfW + 14f, ty);
                g.DrawString($"Fecha / Hora :  {pay.SalidaFechaHora}", fFieldVal, Brushes.Black, left + halfW + 14f, ty + 22f);
                g.DrawString($"Operador     :  {pay.SalidaOp}", fFieldVal, mutedBrush, left + halfW + 14f, ty + 44f);
                ty = y + 92f;
            }
            if (v.MostrarObservaciones)
            {
                if (v.MostrarTiemposOperador)
                {
                    g.DrawLine(linePen, left + 12f, ty, left + width - 12f, ty);
                    ty += 8f;
                }
                g.DrawString("OBSERVACIONES:", fFieldLbl, mutedBrush, left + 14f, ty + 2f);
                g.DrawString(pay.Observaciones, fFieldVal, Brushes.Black, new RectangleF(left + 144f, ty + 2f, width - 160f, 34f));
            }
        }

        // 6. Firmas y Pie de página
        float sigY = bottom - 34f;
        float sigW = (width - 60f) / 2f;
        using var sigPen = new Pen(Color.FromArgb(115, 135, 148), 1f);
        g.DrawLine(sigPen, left + 10f, sigY, left + 10f + sigW, sigY);
        g.DrawString("Firma / Timbre Operador Báscula", fSmall, mutedBrush, new RectangleF(left + 10f, sigY + 3f, sigW, 16f), centerFmt);

        float sig2X = left + width - 10f - sigW;
        g.DrawLine(sigPen, sig2X, sigY, sig2X + sigW, sigY);
        g.DrawString("Firma Conductor / Recepción Conforme", fSmall, mutedBrush, new RectangleF(sig2X, sigY + 3f, sigW, 16f), centerFmt);

        float footY = bottom - 14f;
        g.DrawLine(borderPen, left, footY - 3f, left + width, footY - 3f);
        g.DrawString($"PesajeProCamion — {pay.FolioText} — Emitido {DateTime.Now:dd/MM/yyyy HH:mm}", fSmall, mutedBrush, left, footY);
        using var footRightFmt = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString("Documento Estandarizado Página Completa", fSmall, navyDark, new RectangleF(left, footY, width, 16f), footRightFmt);

        e.HasMorePages = false;
    }

    private static void DrawPrintWeightColumn(
        Graphics g,
        RectangleF cell,
        string title,
        string value,
        Font fTitle,
        Font fValue,
        Brush titleBrush,
        Brush valueBrush,
        StringFormat fmt)
    {
        g.DrawString(title, fTitle, titleBrush, new RectangleF(cell.X, cell.Y + 12f, cell.Width, 22f), fmt);
        g.DrawString(value, fValue, valueBrush, new RectangleF(cell.X, cell.Y + 36f, cell.Width, cell.Height - 44f), fmt);
    }

    public static bool SendRawTextToPrinter(string printerName, string content, string documentName = "TicketPesaje")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        ArgumentNullException.ThrowIfNull(content);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] bytes = Encoding.GetEncoding(1252).GetBytes(content);
        IntPtr pBytes = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pBytes, bytes.Length);
            if (!OpenPrinter(printerName, out IntPtr hPrinter, IntPtr.Zero))
            {
                return false;
            }
            try
            {
                var docInfo = new DocInfoA { pDocName = documentName, pDataType = "RAW" };
                if (!StartDocPrinterA(hPrinter, 1, docInfo))
                {
                    return false;
                }
                try
                {
                    if (!StartPagePrinter(hPrinter))
                    {
                        return false;
                    }
                    try
                    {
                        return WritePrinter(hPrinter, pBytes, bytes.Length, out int written) && written == bytes.Length;
                    }
                    finally
                    {
                        EndPagePrinter(hPrinter);
                    }
                }
                finally
                {
                    EndDocPrinter(hPrinter);
                }
            }
            finally
            {
                ClosePrinter(hPrinter);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(pBytes);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private sealed class DocInfoA
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string? pDocName;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? pDataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartDocPrinterA(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DocInfoA di);

    [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);
}
