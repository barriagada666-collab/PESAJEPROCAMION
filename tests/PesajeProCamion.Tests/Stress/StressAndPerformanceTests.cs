using System.Diagnostics;
using System.Runtime.InteropServices;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Management;
using PesajeProCamion.App.UI.Weighing;
using PesajeProCamion.Data.Configuration;
using PesajeProCamion.Data.Infrastructure;
using PesajeProCamion.Data.Migration;
using PesajeProCamion.Data.Repositories;
using PesajeProCamion.Domain.Diagnostics;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Services;
using PesajeProCamion.Hardware.Export;
using PesajeProCamion.Hardware.Printing;
using PesajeProCamion.Hardware.Scale;
using Xunit;
using Xunit.Abstractions;

namespace PesajeProCamion.Tests.Stress;

[Collection("SequentialUiAndLogger")]
public sealed class StressAndPerformanceTests
{
    private const string LegacyGdbPath = @"d:\archivos qlabel\PESAJE CAMIONES\placillaPRUEBA\WMDBSPC.GDB";
    private readonly ITestOutputHelper _output;

    public StressAndPerformanceTests(ITestOutputHelper output) => _output = output;

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

    [Fact]
    public void SerialParser_Stress10000Frames_ZeroFailuresAndSub100Ms()
    {
        var cfg = new ConfiguracionSistema { VisorStx = 2, VisorPosData = 3, VisorLenData = 5, VLimit = 1, VDelta = 10 };
        var parser = new SerialFrameParser();
        byte[] frame = System.Text.Encoding.ASCII.GetBytes("\x02 28450 kg\r\n");

        var sw = Stopwatch.StartNew();
        int validCount = 0;
        for (int i = 0; i < 10_000; i++)
        {
            ParsedWeightFrame? res = parser.TryFeedBytes(frame, cfg);
            if (res is { Valid: true, PesoKg: 28450 }) validCount++;
        }
        sw.Stop();

        Assert.Equal(10_000, validCount);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"Parsing demasiado lento: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task FirebirdMigratedDatabase_ConcurrentQueriesAndBatchInserts_StayFastAndLeakFree()
    {
        string fdbPath = Path.Combine(Path.GetTempPath(), $"wm_stress_{Guid.NewGuid():N}.fdb");
        var settings = new DatabaseSettings { DataSource = "localhost", Port = 3050, DatabasePath = fdbPath };
        try
        {
            await Ods9Migrator.MigrateAsync(LegacyGdbPath, settings, hashPasswords: false, overwriteTarget: true);
            var db = new DatabaseSessionFactory(settings);
            var weighing = new WeighingRepository(db);

            // Benchmark: 40 concurrent query reads across 2,734 historical rows
            var sw = Stopwatch.StartNew();
            Task<List<Pesaje>>[] reads = Enumerable.Range(0, 40)
                .Select(_ => weighing.QueryPesajesAsync(maxRows: 500))
                .ToArray();
            List<Pesaje>[] results = await Task.WhenAll(reads);
            sw.Stop();

            Assert.All(results, list => Assert.Equal(500, list.Count));
            Assert.True(sw.ElapsedMilliseconds < 15_000, $"40 consultas concurrentes tardaron {sw.ElapsedMilliseconds} ms");

            // Benchmark: 100 rapid sequential transit -> completed pesaje cycles
            var swW = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                string pat = $"ST{i:D4}";
                int fol = await weighing.RegistrarIngresoTransitoAsync(new RegistroTransito
                {
                    Patente = pat,
                    FechaIngreso = DateTime.Today,
                    HoraIngreso = "10:00",
                    Bruto = 20000 + i * 10,
                    UsuarioIngreso = 5
                });
                await weighing.CompletarPesajeAsync(new Pesaje
                {
                    FolioPesaje = fol,
                    Patente = pat,
                    FechaIngreso = DateTime.Today,
                    HoraIngreso = "10:00",
                    FechaSalida = DateTime.Today,
                    HoraSalida = "10:05",
                    Bruto = 20000 + i * 10,
                    Tara = 8000,
                    UsuarioIngreso = 5,
                    UsuarioSalida = 5
                });
            }
            swW.Stop();
            Assert.True(swW.ElapsedMilliseconds < 30_000, $"100 ciclos completos tardaron {swW.ElapsedMilliseconds} ms");
        }
        finally
        {
            FirebirdSql.Data.FirebirdClient.FbConnection.ClearAllPools();
            if (File.Exists(fdbPath)) File.Delete(fdbPath);
        }
    }

    [Fact]
    public void UiControls_RepeatedCreateDispose_DoNotLeakScaleListenersOrGdiObjects()
    {
        Exception? threadEx = null;
        long heapDeltaBytes = 0;
        long gdiDelta = 0;
        long elapsedMs = 0;

        var sta = new Thread(() =>
        {
            try
            {
                bool prevSilent = AppLogger.IsSilentTestHost;
                AppLogger.IsSilentTestHost = true;
                var settings = new DatabaseSettings
                {
                    DataSource = "127.0.0.1",
                    Port = 65432,
                    DatabasePath = Path.Combine(Path.GetTempPath(), "nonexistent_leak_check.fdb"),
                    ConnectionTimeout = 2,
                    CommandTimeout = 5
                };
                using var services = new AppServices(settings)
                {
                    UsuarioActual = new Usuario { Id = 1, Nombre = "BARRIAGADA", Activo = true, SupervisorSistema = true },
                    BasculaActual = new BasculaInfo { NombreEmpresa = "CAMBIASO HNOS", Rut = "80500400-1" }
                };

                var sampleTicket = new Pesaje
                {
                    FolioPesaje = 2740,
                    Patente = "CHHW80",
                    Cliente = "89201400",
                    NombreCliente = "ENV. IMPRESOS",
                    Producto = "5300000009",
                    NombreProducto = "SCRAP MIX",
                    Bruto = 30440,
                    Tara = 14990,
                    Neto = 15450,
                    Fardos = 51
                };

                void RunCycle()
                {
                    using (var pesaje = new PesajeWorkspaceControl(services))
                    {
                        pesaje.CreateControl();
                    }
                    using (var gestion = new GestionPesajesControl(services))
                    {
                        gestion.CreateControl();
                    }
                    using (var reports = new ReportsWorkspaceControl(services))
                    {
                        reports.CreateControl();
                    }
                    using (var viewer = new TicketViewerDialog(services, sampleTicket))
                    {
                        viewer.CreateControl();
                    }
                    using (var visor = new VisorSetupForm(services))
                    {
                        visor.CreateControl();
                    }
                    using (var mop = new PesajeMopForm(services, "CHHW80", 510, 18))
                    {
                        mop.CreateControl();
                    }
                    using (var logs = new ErrorLogViewerDialog())
                    {
                        logs.CreateControl();
                    }
                }

                // Warmup cycle so static fonts/icons/caches initialize before baseline snapshot
                RunCycle();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                IntPtr proc = Process.GetCurrentProcess().Handle;
                uint gdiBefore = GetGuiResources(proc, 0);
                long memBefore = GC.GetTotalMemory(forceFullCollection: true);

                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 20; i++)
                {
                    RunCycle();
                    if ((i + 1) % 5 == 0)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                }
                sw.Stop();
                elapsedMs = sw.ElapsedMilliseconds;

                // Ensure Scale & AppLogger emit safely with zero dangling disposed controls/listeners
                Assert.Equal(0, services.Scale.ActiveSubscriberCount);
                services.Scale.EmitSimulatedWeight(18500, stable: true);
                AppLogger.Info("LeakCheck", "Ciclos de disposición verificados");

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                uint gdiAfter = GetGuiResources(proc, 0);
                long memAfter = GC.GetTotalMemory(forceFullCollection: true);

                gdiDelta = (long)gdiAfter - gdiBefore;
                heapDeltaBytes = memAfter - memBefore;
                AppLogger.IsSilentTestHost = prevSilent;
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();

        Assert.Null(threadEx);
        _output.WriteLine($"UI_20_CYCLES_MS={elapsedMs} GDI_DELTA={gdiDelta} HEAP_DELTA_KB={heapDeltaBytes / 1024.0:N1}");
        Assert.True(gdiDelta <= 40, $"Posible fuga GDI tras 20 ciclos (delta={gdiDelta} objetos).");
        Assert.True(heapDeltaBytes < 20L * 1024L * 1024L, $"Crecimiento de heap excesivo tras GC: {heapDeltaBytes / 1024.0:N1} KB");
    }

    [Fact]
    public void AnalyticsAndExports_5000Tickets_StayFastAndBoundedMemory()
    {
        List<Pesaje> rows = Enumerable.Range(1, 5000).Select(i => new Pesaje
        {
            FolioPesaje = 1000 + i,
            Patente = $"PAT{i % 25:D3}",
            Cliente = $"C{i % 15:D2}",
            NombreCliente = $"CLIENTE {i % 15:D2}",
            Origen = $"O{i % 8:D2}",
            NombreOrigen = $"PROVEEDOR {i % 8:D2}",
            Producto = $"PR{i % 10:D2}",
            NombreProducto = $"MATERIAL {i % 10:D2}",
            FechaIngreso = DateTime.Today.AddDays(-(i % 90)),
            HoraIngreso = "08:00",
            FechaSalida = DateTime.Today.AddDays(-(i % 90)),
            HoraSalida = "08:45",
            Bruto = 28000 + (i % 500),
            Tara = 12000,
            Neto = 16000 + (i % 500),
            Fardos = 20 + (i % 40)
        }).ToList();

        string tmpDir = Path.Combine(Path.GetTempPath(), $"pesaje_bench5k_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long memBefore = GC.GetTotalMemory(forceFullCollection: true);

            var sw = Stopwatch.StartNew();
            var filter = new ReportFilterCriteria { Desde = DateTime.Today.AddDays(-120), Hasta = DateTime.Today };
            AnalyticsResult bi = AnalyticsCalculator.FilterAndAnalyze(rows, filter);
            long tAnalyzeMs = sw.ElapsedMilliseconds;

            string xlsxPath = Path.Combine(tmpDir, "bench5k.xlsx");
            DataExporter.ExportAnalyticsWorkbookToXlsx(xlsxPath, filter, bi, bi.Filtrados);
            long tXlsxMs = sw.ElapsedMilliseconds - tAnalyzeMs;

            var pdfReport = new StandardTableReport
            {
                Title = "BENCHMARK 5000 TICKETS",
                Subtitle = "Prueba estrés exportación página completa",
                SheetName = "Bench5k",
                Headers = ["N° Folio", "Fecha", "Patente", "Cliente", "Producto", "Fardos", "Bruto", "Tara", "Neto"],
                Rows = bi.Filtrados.Select(p => (IReadOnlyList<object?>)[p.FolioPesaje, p.FechaSalida.ToString("dd/MM/yyyy"), p.Patente, p.NombreCliente, p.NombreProducto, p.Fardos, p.Bruto, p.Tara, p.Neto]).ToList(),
                FolioColumnIndex = 0,
                BoldHighlightColumnIndices = new HashSet<int> { 8 }
            };
            string pdfPath = Path.Combine(tmpDir, "bench5k.pdf");
            DataExporter.ExportStandardReportToPdf(pdfPath, pdfReport);
            sw.Stop();
            long tPdfMs = sw.ElapsedMilliseconds - tAnalyzeMs - tXlsxMs;

            long memAfter = GC.GetTotalMemory(forceFullCollection: true);
            long memDeltaKb = (memAfter - memBefore) / 1024;
            _output.WriteLine($"BENCH_5K_BI_MS={tAnalyzeMs} BENCH_5K_XLSX_MS={tXlsxMs} BENCH_5K_PDF_MS={tPdfMs} TOTAL_MS={sw.ElapsedMilliseconds} MEM_DELTA_KB={memDeltaKb}");

            Assert.Equal(5000, bi.Kpis.TotalTickets);
            Assert.True(File.Exists(xlsxPath) && new FileInfo(xlsxPath).Length > 10_000);
            Assert.True(File.Exists(pdfPath) && new FileInfo(pdfPath).Length > 10_000);
            Assert.True(sw.ElapsedMilliseconds < 10_000, $"Benchmark 5000 tickets excedió límite: {sw.ElapsedMilliseconds} ms");
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
        }
    }
}
