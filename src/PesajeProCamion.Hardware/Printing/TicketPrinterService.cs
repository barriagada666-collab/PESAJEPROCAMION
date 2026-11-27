using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Validation;

namespace PesajeProCamion.Hardware.Printing;

/// <summary>Motor de generación e impresión de tickets comerciales y boletos MOP (gráfico y texto RAW).</summary>
public static partial class TicketPrinterService
{
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

    public static PrintDocument CreateCommercialTicketDocument(Pesaje p, BasculaInfo bascula, ConfiguracionSistema? cfg = null)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(bascula);

        var doc = new PrintDocument { DocumentName = $"Ticket_Pesaje_{p.FolioPesaje}" };
        doc.PrintPage += (_, e) => RenderTicketPage(e, p, bascula, cfg);
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

    private static void RenderTicketPage(PrintPageEventArgs e, Pesaje p, BasculaInfo bascula, ConfiguracionSistema? cfg)
    {
        Graphics? g = e.Graphics;
        if (g is null)
        {
            return;
        }

        using var fTitle = new Font("Segoe UI", 14, FontStyle.Bold);
        using var fSub = new Font("Segoe UI", 10, FontStyle.Bold);
        using var fBody = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        using var fBold = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        using var fBig = new Font("Segoe UI", 16, FontStyle.Bold);
        using var pen = new Pen(Color.FromArgb(30, 58, 95), 1.5f);

        float left = e.MarginBounds.Left;
        float width = e.MarginBounds.Width;
        float y = e.MarginBounds.Top;

        string company = string.IsNullOrWhiteSpace(bascula.NombreEmpresa) ? (cfg?.Titulo1 ?? "PESAJE DE CAMIONES") : bascula.NombreEmpresa;
        g.DrawString(company, fTitle, Brushes.Black, left, y);
        y += 26;
        g.DrawString($"{bascula.Direccion}  |  RUT: {bascula.Rut}", fBody, Brushes.DimGray, left, y);
        y += 22;
        g.DrawLine(pen, left, y, left + width, y);
        y += 10;

        string estado = p.Anulado ? " [ANULADO]" : string.Empty;
        g.DrawString($"TICKET DE PESAJE COMERCIAL #{p.FolioPesaje:D6}{estado}", fSub, Brushes.Navy, left, y);
        y += 26;

        (string Lbl, string Val)[] rows =
        [
            ("Tipo de Pesaje", p.TipoPesaje.ToString()),
            ("Patente / Acoplado", $"{p.Patente}  {(string.IsNullOrWhiteSpace(p.Acoplado) ? string.Empty : "/ " + p.Acoplado)}"),
            ("Cliente", p.NombreCliente),
            ("Producto", p.NombreProducto),
            ("Origen / Destino", $"{p.NombreOrigen} -> {p.NombreDestino}"),
            ("Transportista / Chofer", $"{p.NombreTransportista} / {p.NombreChofer}"),
            ("Guía Despacho", p.GuiaDespacho),
            ("Ingreso", $"{p.FechaIngreso:dd/MM/yyyy} {p.HoraIngreso} ({p.NombreUsuarioIngreso})"),
            ("Salida", $"{p.FechaSalida:dd/MM/yyyy} {p.HoraSalida} ({p.NombreUsuarioSalida})"),
            ("Observaciones", $"{p.Observacion1} {p.Observacion2}".Trim())
        ];

        foreach ((string lbl, string val) in rows)
        {
            g.DrawString($"{lbl}:", fBold, Brushes.Black, left, y);
            g.DrawString(val, fBody, Brushes.Black, left + 165, y);
            y += 21;
        }

        y += 10;
        g.DrawRectangle(pen, left, y, width, 86);
        float c1 = left + 20;
        float c2 = left + width / 3f;
        float c3 = left + (width * 2f) / 3f;
        g.DrawString("PESO BRUTO", fSub, Brushes.DimGray, c1, y + 12);
        g.DrawString($"{p.Bruto:N0} kg", fBig, Brushes.Black, c1, y + 36);
        g.DrawString("PESO TARA", fSub, Brushes.DimGray, c2, y + 12);
        g.DrawString($"{p.Tara:N0} kg", fBig, Brushes.Black, c2, y + 36);
        g.DrawString("PESO NETO", fSub, Brushes.Navy, c3, y + 12);
        g.DrawString($"{p.Neto:N0} kg", fBig, Brushes.Navy, c3, y + 36);
        e.HasMorePages = false;
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
