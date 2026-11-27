using System.IO.Compression;
using System.Text;
using PesajeProCamion.Data.Configuration;
using PesajeProCamion.Domain.Diagnostics;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Hardware.Export;
using PesajeProCamion.Hardware.Printing;
using PesajeProCamion.Hardware.Scale;
using Xunit;

namespace PesajeProCamion.Tests.Unit;

public sealed class HardwareAndExportTests
{
    [Fact]
    public void SerialFrameParser_ParsesFragmentedFramesAndStability()
    {
        var cfg = new ConfiguracionSistema { VisorStx = 2, VisorPosData = 3, VisorLenData = 5, VLimit = 3, VDelta = 10 };
        var parser = new SerialFrameParser();

        byte[] chunk1 = [0x99, 0x02, (byte)' ', (byte)'2', (byte)'4'];
        byte[] chunk2 = [(byte)'5', (byte)'8', (byte)'0', (byte)' ', (byte)'k', (byte)'g', (byte)'\r', (byte)'\n'];

        Assert.Null(parser.TryFeedBytes(chunk1, cfg));
        ParsedWeightFrame? frame = parser.TryFeedBytes(chunk2, cfg);
        Assert.NotNull(frame);
        Assert.True(frame.Valid);
        Assert.Equal(24580, frame.PesoKg);

        Assert.False(parser.UpdateStability(24580, cfg));
        Assert.False(parser.UpdateStability(24585, cfg));
        Assert.True(parser.UpdateStability(24580, cfg));
    }

    [Fact]
    public void DynamicAxleProcessor_AppliesTrimmedMeanAndCalibration()
    {
        var cfg = new ConfiguracionSistema { VmopUnder = 350, VmopOver = 350, VmopCutLeft = 20, VmopCutRight = 20 };
        cfg.Calibraciones[0] = 100;
        cfg.Offsets[0] = -50;
        var proc = new DynamicAxleProcessor();

        double[] samples = [100, 6200, 6500, 6520, 6480, 6500, 6510, 6490, 6500, 6500, 9999, 200];
        int? captured = null;
        foreach (double s in samples)
        {
            int? res = proc.FeedSample(s, cfg);
            if (res.HasValue) captured = res.Value;
        }

        Assert.NotNull(captured);
        Assert.InRange(captured.Value, 6500, 6650); // ~6500 + 100 - 50 = 6550
    }

    [Fact]
    public void TicketPrinter_FixesTyposAndFormatsCommercialAndMopTickets()
    {
        var basc = new BasculaInfo { NombreEmpresa = "CAMBIASO HNOS S.A.C", Descripcion = "BASCULA 1", Direccion = "PLACILLA" };
        var p = new Pesaje { FolioPesaje = 123, Patente = "ABCD12", Bruto = 30000, Tara = 12000, Neto = 18000, NombreCliente = "ACME", NombreProducto = "RESINA" };
        string txt = TicketPrinterService.BuildRawTicketText(p, basc);
        Assert.Contains("000123", txt, StringComparison.Ordinal);
        Assert.Contains("18,000", txt.Replace(".", ",", StringComparison.Ordinal), StringComparison.Ordinal);

        var mop = new PesajeMop { FolioMop = 5, Patente = "ABCD12", TipoMop = 510, Largo = 18, Bruto = 42000, Maximo = 45000, Estado = EstadoNormaMop.DentroDeNorma };
        string mopTxt = TicketPrinterService.BuildRawMopBoletoText(mop, basc, "TRANS", "CHOFER");
        Assert.Contains("CONTROL CAMINERO M.O.P.", mopTxt, StringComparison.Ordinal);
        Assert.DoesNotContain("CAMIMERO", mopTxt, StringComparison.Ordinal);
    }

    [Fact]
    public void DataExporter_ProducesValidXlsxCsvAndDbfFiles()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), $"pesaje_export_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            string xlsx = Path.Combine(tmpDir, "test.xlsx");
            DataExporter.ExportToXlsx(xlsx, "Pesajes", ["Folio", "Cliente & Co"], [[1, "ACME <Test>"]]);
            using (ZipArchive zip = ZipFile.OpenRead(xlsx))
            {
                Assert.NotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));
                using var sr = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
                string xml = sr.ReadToEnd();
                Assert.Contains("ACME &lt;Test&gt;", xml, StringComparison.Ordinal);
            }

            string dbf = Path.Combine(tmpDir, "test.dbf");
            DataExporter.ExportToDbf(dbf, [("FLPESAJE", 10), ("PATENTE", 8)], [["101", "ABCD12"]]);
            byte[] bytes = File.ReadAllBytes(dbf);
            Assert.Equal(0x03, bytes[0]);
            Assert.True(bytes.Length > 80);

            string csv = Path.Combine(tmpDir, "test.csv");
            DataExporter.ExportToCsv(csv, ["Col1", "Col2"], [["A;B", "C"]]);
            string csvText = File.ReadAllText(csv, Encoding.UTF8);
            Assert.Contains("\"A;B\";C", csvText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void LineDelimitedSerialParser_And_RemoteUncPathResolver_And_AppLogger_WorkCorrectly()
    {
        // 1. Parser RS-232 sin STX (modo línea CR/LF) y FeedRawAsciiTestFrame
        var cfgNoStx = new ConfiguracionSistema { VisorStx = 0, VisorPosData = 1, VisorLenData = 8, VLimit = 1, VDelta = 10 };
        using var scale = new ScaleReaderService();
        scale.UpdateConfig(cfgNoStx);
        int seenKg = 0;
        scale.ReadingReceived += (_, e) => seenKg = e.WeightKg;
        scale.FeedRawAsciiTestFrame("ST,GS,+ 18450 kg\r\n");
        Assert.Equal(18450, seenKg);

        // 2. Resolución automática de ruta compartida de red UNC -> Host + candidatos Firebird
        (string? host, IReadOnlyList<string> candidates) = RemoteDatabaseResolver.BuildCandidates("localhost", @"\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB");
        Assert.Equal("192.168.2.56", host);
        Assert.Contains("/bdwm/placilla2/WMDBSPC.GDB", candidates, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(@"C:\bdwm\placilla2\WMDBSPC.GDB", candidates, StringComparer.OrdinalIgnoreCase);

        // 3. AppLogger registra y exporta errores
        AppLogger.Info("UnitTest", "Evento de prueba");
        AppLogger.ErrorDetail("UnitTest", "Error de prueba controlado", "Detalle de pila");
        Assert.Contains(AppLogger.GetEntries(), e => e.Message == "Error de prueba controlado");
    }
}
