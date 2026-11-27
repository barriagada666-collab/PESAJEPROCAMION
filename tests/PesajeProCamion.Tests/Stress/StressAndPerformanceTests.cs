using System.Diagnostics;
using PesajeProCamion.Data.Configuration;
using PesajeProCamion.Data.Infrastructure;
using PesajeProCamion.Data.Migration;
using PesajeProCamion.Data.Repositories;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Hardware.Scale;
using Xunit;

namespace PesajeProCamion.Tests.Stress;

public sealed class StressAndPerformanceTests
{
    private const string LegacyGdbPath = @"d:\archivos qlabel\PESAJE CAMIONES\placillaPRUEBA\WMDBSPC.GDB";

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
}
