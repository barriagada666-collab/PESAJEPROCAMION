using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using PesajeProCamion.Domain.Entities;

namespace PesajeProCamion.Hardware.Export;

/// <summary>Exportación limpia a Excel (.xlsx nativo OpenXML multi-hoja), PDF 1.4 vectorial, CSV UTF-8 BOM y dBASE III (.dbf).</summary>
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

    public static void ExportAnalyticsWorkbookToXlsx(
        string filePath,
        ReportFilterCriteria filter,
        AnalyticsResult analytics,
        IReadOnlyList<Pesaje> rows)
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
            (
                "Detalle_Pesajes",
                ["Folio", "Fecha", "Hora", "Patente", "Acoplado", "Tipo", "Cliente", "Proveedor/Origen", "Transportista", "Producto", "Bruto (kg)", "Tara (kg)", "Neto (kg)", "Estado"],
                rows.Select(p => (IReadOnlyList<object?>)
                [
                    p.FolioPesaje, p.FechaSalida.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), p.HoraSalida,
                    p.Patente, p.Acoplado, p.TipoPesaje.ToString(), p.NombreCliente, p.NombreOrigen,
                    p.NombreTransportista, p.NombreProducto, p.Bruto, p.Tara, p.Neto, p.Anulado ? "ANULADO" : "ACTIVO"
                ]).ToList()
            ),
            BuildSegmentSheet("Por_Cliente", "Cliente", analytics.PorCliente),
            BuildSegmentSheet("Por_Proveedor_Origen", "Proveedor / Origen", analytics.PorProveedorOrigen),
            BuildSegmentSheet("Por_Producto", "Producto", analytics.PorProducto),
            BuildSegmentSheet("Por_Patente", "Patente", analytics.PorPatente)
        };

        ExportMultiSheetXlsx(filePath, sheets);
    }

    private static (string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows) BuildSegmentSheet(
        string sheetName, string dimHeader, IReadOnlyList<SegmentSummaryRow> segs)
    {
        return (
            sheetName,
            ["Código", dimHeader, "Tickets", "Bruto (kg)", "Tara (kg)", "Neto (kg)", "Toneladas", "Promedio Neto (kg)", "% Participación"],
            segs.Select(s => (IReadOnlyList<object?>)
            [
                s.Clave, s.Nombre, s.Tickets, s.TotalBrutoKg, s.TotalTaraKg, s.TotalNetoKg, s.ToneladasNetas, s.PromedioNetoKg, s.PorcentajeNeto
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

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        var ctOverrides = new StringBuilder();
        var rels = new StringBuilder();
        var wbSheets = new StringBuilder();

        for (int i = 0; i < sheets.Count; i++)
        {
            int idx = i + 1;
            string rawName = string.IsNullOrWhiteSpace(sheets[i].SheetName) ? $"Hoja{idx}" : sheets[i].SheetName;
            string safeName = rawName.Length > 31 ? rawName[..31] : rawName;
            ctOverrides.Append(CultureInfo.InvariantCulture, $"  <Override PartName=\"/xl/worksheets/sheet{idx}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>\n");
            rels.Append(CultureInfo.InvariantCulture, $"  <Relationship Id=\"rId{idx}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{idx}.xml\"/>\n");
            wbSheets.Append(CultureInfo.InvariantCulture, $"<sheet name=\"{SecurityElement.Escape(safeName)}\" sheetId=\"{idx}\" r:id=\"rId{idx}\"/>");

            var sheetSb = new StringBuilder();
            sheetSb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
            AppendXmlRow(sheetSb, 1, sheets[i].Headers.Cast<object?>().ToList());
            for (int r = 0; r < sheets[i].Rows.Count; r++)
            {
                AppendXmlRow(sheetSb, r + 2, sheets[i].Rows[r]);
            }
            sheetSb.Append("</sheetData></worksheet>");
            WriteZipEntry(zip, $"xl/worksheets/sheet{idx}.xml", sheetSb.ToString());
        }

        WriteZipEntry(zip, "[Content_Types].xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-officedocument.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
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
              <sheets>{wbSheets}</sheets>
            </workbook>
            """);
    }
    public static void ExportToCsv(string filePath, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(";", headers.Select(EscapeCsv)));
        foreach (IReadOnlyList<string?> row in rows)
        {
            sb.AppendLine(string.Join(";", row.Select(EscapeCsv)));
        }
        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static void ExportToXlsx(string filePath, string sheetName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        string safeSheet = string.IsNullOrWhiteSpace(sheetName) ? "Datos" : sheetName[..Math.Min(31, sheetName.Length)];
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        WriteZipEntry(zip, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """);

        WriteZipEntry(zip, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);

        WriteZipEntry(zip, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            </Relationships>
            """);

        WriteZipEntry(zip, "xl/workbook.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="{SecurityElement.Escape(safeSheet)}" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """);

        var sheetSb = new StringBuilder();
        sheetSb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
        AppendXmlRow(sheetSb, 1, headers.Cast<object?>().ToList());
        for (int r = 0; r < rows.Count; r++)
        {
            AppendXmlRow(sheetSb, r + 2, rows[r]);
        }
        sheetSb.Append("</sheetData></worksheet>");
        WriteZipEntry(zip, "xl/worksheets/sheet1.xml", sheetSb.ToString());
    }

    /// <summary>Genera archivo dBASE III (.DBF) estándar sin rutas hardcodeadas.</summary>
    public static void ExportToDbf(string filePath, IReadOnlyList<(string Name, int Width)> columns, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp1252 = Encoding.GetEncoding(1252);

        short headerSize = checked((short)(32 + columns.Count * 32 + 1));
        short recordSize = checked((short)(1 + columns.Sum(c => Math.Clamp(c.Width, 1, 250))));

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        byte[] header = new byte[32];
        DateTime now = DateTime.Today;
        header[0] = 0x03; // dBASE III
        header[1] = checked((byte)(now.Year - 1900));
        header[2] = checked((byte)now.Month);
        header[3] = checked((byte)now.Day);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), rows.Count);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(8, 2), headerSize);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(10, 2), recordSize);
        header[29] = 0x03; // Windows ANSI LDID
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

    private static void AppendXmlRow(StringBuilder sb, int rowNum, IReadOnlyList<object?> values)
    {
        sb.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowNum}\">");
        for (int c = 0; c < values.Count; c++)
        {
            string cellRef = $"{ColumnLetters(c)}{rowNum}";
            object? val = values[c];
            if (val is int or long or short or double or decimal)
            {
                string numStr = Convert.ToString(val, CultureInfo.InvariantCulture) ?? "0";
                sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{cellRef}\"><v>{numStr}</v></c>");
            }
            else
            {
                string txt = SecurityElement.Escape(Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty) ?? string.Empty;
                sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t>{txt}</t></is></c>");
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
