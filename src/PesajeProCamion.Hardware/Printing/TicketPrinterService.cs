using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Validation;
using PesajeProCamion.Hardware.Export;

namespace PesajeProCamion.Hardware.Printing;

/// <summary>Motor de generación e impresión de tickets comerciales y boletos MOP (gráfico, PDF y texto RAW).</summary>
public static partial class TicketPrinterService
{
    public static void ExportCommercialTicketToPdf(
        string filePath,
        Pesaje p,
        BasculaInfo bascula,
        ConfiguracionSistema? cfg = null,
        bool isEnProceso = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(bascula);

        string company = string.IsNullOrWhiteSpace(bascula.NombreEmpresa) ? (cfg?.Titulo1 ?? "PESAJE DE CAMIONES") : bascula.NombreEmpresa;
        string companySub = string.IsNullOrWhiteSpace(bascula.Rut) ? bascula.Direccion : $"{bascula.Direccion}  |  RUT: {bascula.Rut}";
        string badge = p.Anulado ? "ANULADO" : (isEnProceso ? "EN PROCESO" : "COMPLETADO");

        (string Label, string Value)[] details =
        [
            ("Folio Pesaje:", $"#{p.FolioPesaje}"),
            ("Tipo Pesaje:", p.TipoPesaje.ToString()),
            ("Patente / Acoplado:", string.IsNullOrWhiteSpace(p.Acoplado) ? p.Patente : $"{p.Patente}  /  {p.Acoplado}"),
            ("Cliente:", $"{p.Cliente} - {p.NombreCliente}".Trim(' ', '-')),
            ("Producto:", $"{p.Producto} - {p.NombreProducto}".Trim(' ', '-')),
            ("Origen / Proveedor:", $"{p.Origen} - {p.NombreOrigen}".Trim(' ', '-')),
            ("Destino:", $"{p.Destino} - {p.NombreDestino}".Trim(' ', '-')),
            ("Transportista:", $"{p.Transportista} - {p.NombreTransportista}".Trim(' ', '-')),
            ("Chofer:", $"{p.Chofer} - {p.NombreChofer}".Trim(' ', '-')),
            ("Guía Despacho:", string.IsNullOrWhiteSpace(p.GuiaDespacho) ? "-" : p.GuiaDespacho)
        ];

        string ent = $"{p.FechaIngreso:dd/MM/yyyy} {p.HoraIngreso}  |  Operador: {p.NombreUsuarioIngreso}".Trim();
        string sal = isEnProceso
            ? "Vehículo actualmente EN TRÁNSITO (pendiente 2° pesaje)"
            : $"{p.FechaSalida:dd/MM/yyyy} {p.HoraSalida}  |  Operador: {p.NombreUsuarioSalida}".Trim();
        string obs = $"{p.Observacion1} {p.Observacion2}".Trim();

        PdfDocumentWriter.WriteSingleTicketPdf(
            filePath,
            company,
            companySub,
            "TICKET DE PESAJE COMERCIAL",
            badge,
            details,
            p.Bruto,
            p.Tara,
            isEnProceso ? 0 : p.Neto,
            ent,
            sal,
            string.IsNullOrWhiteSpace(obs) ? "-" : obs);
    }
    public static string BuildRawTicketText(Pesaje p, BasculaInfo bascula, ConfiguracionSistema? cfg = null)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(bascula);

        var sb = new StringBuilder();
        string company = string.IsNullOrWhiteSpace(bascula.NombreEmpresa) ? (cfg?.Titulo1 ?? "PESAJE DE CAMIONES") : bascula.NombreEmpresa;
        sb.AppendLine("============================================================");
        sb.AppendLine($"  {company}");
        if (!string.IsNullOrWhiteSpace(bascula.Direccion))
        {
            sb.AppendLine($"  {bascula.Direccion}");
        }
        sb.AppendLine("  TICKET DE PESAJE COMERCIAL");
        sb.AppendLine("============================================================");
        sb.AppendLine(CultureInfo.InvariantCulture, $"FOLIO       : {p.FolioPesaje:D6}       ESTADO : {(p.Anulado ? "ANULADO" : "ACTIVO")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"PATENTE     : {p.Patente,-10}   ACOPLADO : {p.Acoplado}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"CLIENTE     : {p.NombreCliente}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"PRODUCTO    : {p.NombreProducto}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"ORIGEN      : {p.NombreOrigen}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"DESTINO     : {p.NombreDestino}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"TRANSPORT.  : {p.NombreTransportista}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"CHOFER      : {p.NombreChofer}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"GUIA DESP.  : {p.GuiaDespacho}");
        sb.AppendLine("------------------------------------------------------------");
        sb.AppendLine(CultureInfo.InvariantCulture, $"ENTRADA     : {p.FechaIngreso:dd/MM/yyyy} {p.HoraIngreso}   OP: {p.NombreUsuarioIngreso}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"SALIDA      : {p.FechaSalida:dd/MM/yyyy} {p.HoraSalida}   OP: {p.NombreUsuarioSalida}");
        sb.AppendLine("------------------------------------------------------------");
        sb.AppendLine(CultureInfo.InvariantCulture, $"PESO BRUTO  : {p.Bruto,10:N0} Kg");
        sb.AppendLine(CultureInfo.InvariantCulture, $"PESO TARA   : {p.Tara,10:N0} Kg {(p.TaraRegistrada ? "(TARA REG.)" : string.Empty)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"PESO NETO   : {p.Neto,10:N0} Kg");
        if (!string.IsNullOrWhiteSpace(p.Observacion1))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"OBSERVAC.   : {p.Observacion1}");
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
        Image? corporateLogo = null)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(bascula);

        var doc = new PrintDocument { DocumentName = $"Ticket_Pesaje_{p.FolioPesaje}" };
        doc.PrinterSettings.Copies = Math.Clamp(copies, (short)1, (short)20);
        doc.PrintPage += (_, e) => RenderTicketPage(e, p, bascula, cfg, corporateLogo);
        return doc;
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

    private static void RenderTicketPage(PrintPageEventArgs e, Pesaje p, BasculaInfo bascula, ConfiguracionSistema? cfg, Image? corporateLogo)
    {
        Graphics? g = e.Graphics;
        if (g is null)
        {
            return;
        }

        using var fTitle = new Font("Segoe UI", 14.5f, FontStyle.Bold);
        using var fComp = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        using var fSub = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var fBody = new Font("Segoe UI", 9f, FontStyle.Regular);
        using var fBold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var fBig = new Font("Segoe UI", 12.5f, FontStyle.Bold);
        using var tealBrush = new SolidBrush(Color.FromArgb(6, 57, 83));
        using var pen = new Pen(Color.FromArgb(8, 45, 69), 1.4f);
        using var thinPen = new Pen(Color.FromArgb(148, 168, 178), 1f);

        float left = e.MarginBounds.Left;
        float width = e.MarginBounds.Width;
        float y = e.MarginBounds.Top;

        if (corporateLogo is not null)
        {
            g.DrawImage(corporateLogo, new RectangleF(left + width - 110, y, 105, 56));
        }

        using var centerFmt = new StringFormat { Alignment = StringAlignment.Center };
        string estado = p.Anulado ? " [ANULADO]" : string.Empty;
        g.DrawString($"TICKET DE PESAJE COMERCIAL{estado}", fTitle, tealBrush, new RectangleF(left, y, width, 28), centerFmt);
        y += 32;

        string company = string.IsNullOrWhiteSpace(bascula.NombreEmpresa) ? (cfg?.Titulo1 ?? "PESAJE DE CAMIONES") : bascula.NombreEmpresa;
        g.DrawString(company, fComp, Brushes.Black, left, y);
        y += 18;
        g.DrawString($"{bascula.Direccion}   {(string.IsNullOrWhiteSpace(bascula.Rut) ? string.Empty : "|  RUT: " + bascula.Rut)}", fBody, Brushes.DimGray, left, y);
        y += 20;
        g.DrawLine(pen, left, y, left + width, y);
        y += 10;

        float boxTop = y;
        float padX = 14;
        y += 10;

        g.DrawString($"FOLIO PESAJE :  {p.FolioPesaje}", fBold, tealBrush, left + padX, y);
        g.DrawString($"FECHA : {DateTime.Now:dd-MM-yyyy}", fBody, Brushes.Black, left + width * 0.65f, y);
        y += 20;
        g.DrawString($"TIPO PESAJE  :  {p.TipoPesaje}", fBody, Brushes.Black, left + padX, y);
        g.DrawString($"HORA  : {DateTime.Now:HH:mm}", fBody, Brushes.Black, left + width * 0.65f, y);
        y += 24;

        (string Lbl, string Val)[] details =
        [
            ("PATENTE", string.IsNullOrWhiteSpace(p.Acoplado) ? p.Patente : $"{p.Patente}   ACOPLADO: {p.Acoplado}"),
            ("CLIENTE", $"{p.Cliente} - {p.NombreCliente}".Trim(' ', '-')),
            ("PRODUCTO", $"{p.Producto} - {p.NombreProducto}".Trim(' ', '-')),
            ("CHOFER", $"{p.Chofer} {p.NombreChofer}".Trim()),
            ("GUIA DESPACHO", p.GuiaDespacho),
            ("ORIGEN / DEST.", $"{p.NombreOrigen} -> {p.NombreDestino}".Trim(' ', '-', '>'))
        ];
        foreach ((string lbl, string val) in details)
        {
            g.DrawString(lbl, fBody, Brushes.DimGray, left + padX + 16, y);
            g.DrawString(val, fBody, Brushes.Black, left + padX + 150, y);
            y += 20;
        }

        y += 8;
        g.DrawLine(thinPen, left, y, left + width, y);
        y += 14;

        // Recuadro centrado BRUTO / TARA / NETO igual que boleto clásico moderno
        float wBoxW = 280;
        float wBoxX = left + (width - wBoxW) / 2f;
        g.DrawRectangle(pen, wBoxX, y, wBoxW, 86);
        DrawWeightLine(g, fBig, wBoxX + 16, y + 10, wBoxW - 32, "BRUTO", p.Bruto);
        DrawWeightLine(g, fBig, wBoxX + 16, y + 34, wBoxW - 32, "TARA", p.Tara);
        DrawWeightLine(g, fBig, wBoxX + 16, y + 58, wBoxW - 32, "NETO", p.Neto, tealBrush);
        y += 100;

        g.DrawLine(thinPen, left, y, left + width, y);
        y += 12;

        float colMid = left + width / 2f;
        g.DrawString("ENTRADA", fSub, tealBrush, left + padX, y);
        g.DrawString("SALIDA", fSub, tealBrush, colMid, y);
        y += 20;
        g.DrawString($"FECHA     :  {p.FechaIngreso:dd-MM-yyyy}", fBody, Brushes.Black, left + padX + 12, y);
        g.DrawString($"FECHA     :  {p.FechaSalida:dd-MM-yyyy}", fBody, Brushes.Black, colMid + 12, y);
        y += 18;
        g.DrawString($"HORA      :  {p.HoraIngreso}", fBody, Brushes.Black, left + padX + 12, y);
        g.DrawString($"HORA      :  {p.HoraSalida}", fBody, Brushes.Black, colMid + 12, y);
        y += 18;
        g.DrawString($"OPERADOR  :  {p.NombreUsuarioIngreso}", fBody, Brushes.Black, left + padX + 12, y);
        g.DrawString($"OPERADOR  :  {p.NombreUsuarioSalida}", fBody, Brushes.Black, colMid + 12, y);
        y += 26;

        g.DrawString($"OBSERVACIONES :  {$"{p.Observacion1} {p.Observacion2}".Trim()}", fBody, Brushes.Black, left + padX, y);
        y += 26;

        g.DrawRectangle(pen, left, boxTop, width, y - boxTop);
        e.HasMorePages = false;
    }

    private static void DrawWeightLine(Graphics g, Font f, float x, float y, float w, string lbl, int valKg, Brush? brush = null)
    {
        using var rightFmt = new StringFormat { Alignment = StringAlignment.Far };
        Brush b = brush ?? Brushes.Black;
        g.DrawString(lbl, f, b, x, y);
        g.DrawString($"{valKg:N0} kg", f, b, new RectangleF(x, y, w, 24), rightFmt);
    }

    public static bool SendRawTextToPrinter(string printerName, string content, string documentName = "TicketPesaje")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        ArgumentNullException.ThrowIfNull(content);

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
