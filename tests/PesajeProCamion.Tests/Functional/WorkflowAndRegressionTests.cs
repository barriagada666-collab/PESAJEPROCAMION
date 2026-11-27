using PesajeProCamion.Data.Configuration;
using PesajeProCamion.Data.Infrastructure;
using PesajeProCamion.Data.Repositories;
using PesajeProCamion.Data.Schema;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Hardware.Scale;
using Xunit;

namespace PesajeProCamion.Tests.Functional;

public sealed class WorkflowAndRegressionTests
{
    [Fact]
    public async Task EndToEnd_CommercialAndMopWeighing_And_RegressionBugs_WorkCleanly()
    {
        string fdbPath = Path.Combine(Path.GetTempPath(), $"wm_func_{Guid.NewGuid():N}.fdb");
        var settings = new DatabaseSettings { DataSource = "localhost", Port = 3050, DatabasePath = fdbPath };
        try
        {
            await FirebirdSchemaManager.EnsureDatabaseInitializedAsync(settings);
            var db = new DatabaseSessionFactory(settings);
            var catalogs = new CatalogRepository(db);
            var security = new SecurityRepository(db);
            var weighing = new WeighingRepository(db);
            var mopRepo = new MopRepository(db);

            // Seed catalogs + norms
            await catalogs.SaveClienteAsync(new Cliente { Rut = "76543210", Digito = "K", Nombre = "CLIENTE PRUEBA" }, 1);
            await catalogs.SaveProductoAsync(new Producto { Codigo = "P001", Descripcion = "CARGA GENERAL" }, 1);

            // 1. Primer Pesaje -> Tránsito (Despacho donde entrada < salida)
            int folio = await weighing.RegistrarIngresoTransitoAsync(new RegistroTransito
            {
                Patente = " ab cd 12 ",
                Cliente = "76543210",
                Producto = "P001",
                FechaIngreso = DateTime.Today,
                HoraIngreso = "08:00",
                Bruto = 14000,
                Tara = 0,
                TipoPesaje = TipoPesaje.Despacho,
                UsuarioIngreso = 1
            });
            Assert.True(folio >= 1);
            List<RegistroTransito> transito = await weighing.GetTransitoAsync();
            Assert.Single(transito);
            Assert.Equal("ABCD12", transito[0].Patente);

            // 2. Segundo Pesaje -> DBPESAJES + DBCAMIONES
            await weighing.CompletarPesajeAsync(new Pesaje
            {
                FolioPesaje = folio,
                Patente = "ABCD12",
                Cliente = "76543210",
                Producto = "P001",
                FechaIngreso = DateTime.Today,
                HoraIngreso = "08:00",
                FechaSalida = DateTime.Today,
                HoraSalida = "08:30",
                Bruto = 14000,
                Tara = 29500,
                TipoPesaje = TipoPesaje.Despacho,
                UsuarioIngreso = 1,
                UsuarioSalida = 1
            });

            Assert.Empty(await weighing.GetTransitoAsync());
            Pesaje? p1 = await weighing.GetPesajePorFolioAsync(folio);
            Assert.NotNull(p1);
            Assert.Equal(15500, p1.Neto);

            // 3. Second pesaje to verify ToggleAnularPesaje DOES NOT touch other folios (regression for legacy PA_DEL_PESAJES bug!)
            int folio2 = await weighing.CompletarPesajeAsync(new Pesaje
            {
                Patente = "EFGH34",
                Cliente = "76543210",
                Producto = "P001",
                FechaIngreso = DateTime.Today,
                HoraIngreso = "09:00",
                FechaSalida = DateTime.Today,
                HoraSalida = "09:20",
                Bruto = 25000,
                Tara = 10000,
                TipoPesaje = TipoPesaje.Recepcion,
                UsuarioIngreso = 1,
                UsuarioSalida = 1
            });

            await weighing.ToggleAnularPesajeAsync(folio, 1);
            Pesaje? p1Anul = await weighing.GetPesajePorFolioAsync(folio);
            Pesaje? p2Untouched = await weighing.GetPesajePorFolioAsync(folio2);
            Assert.True(p1Anul!.Anulado);
            Assert.Equal(0, p1Anul.Neto);
            Assert.False(p2Untouched!.Anulado);
            Assert.Equal(15000, p2Untouched.Neto);

            // Reactivate p1: verifies ABS(BRUTO - TARA) = +15500 (not -15500!)
            await weighing.ToggleAnularPesajeAsync(folio, 1);
            Pesaje? p1React = await weighing.GetPesajePorFolioAsync(folio);
            Assert.False(p1React!.Anulado);
            Assert.Equal(15500, p1React.Neto);

            // 4. Eliminar Tránsito archives into DBTRAN_ANUL
            int folioTrAnul = await weighing.RegistrarIngresoTransitoAsync(new RegistroTransito
            {
                Patente = "ANUL99",
                FechaIngreso = DateTime.Today,
                HoraIngreso = "10:00",
                Bruto = 10000,
                UsuarioIngreso = 1
            });
            await weighing.EliminarTransitoAsync(folioTrAnul, 1, "Camión retirado sin descarga");
            Assert.Empty(await weighing.GetTransitoAsync());
            Assert.Single(await weighing.GetTransitoAnuladoAsync(DateTime.Today, DateTime.Today));

            // 5. Tonelaje por turno & Audit Log populated cleanly
            List<ResumenTurno> turnos = await weighing.GetTonelajePorTurnoAsync(DateTime.Today, DateTime.Today);
            Assert.Equal(2, turnos.Sum(t => t.CantidadPesajes));
            Assert.NotEmpty(await security.GetAuditLogsAsync(DateTime.Today, DateTime.Today));

            // 6. ScaleReader simulation check
            using var scale = new ScaleReaderService();
            int seen = -1;
            scale.ReadingReceived += (_, e) => seen = e.WeightKg;
            scale.EnableSimulation(await catalogs.GetConfigAsync(), 22340);
            Assert.Equal(22340, seen);
            Assert.Empty(await mopRepo.GetPesajesMopAsync(DateTime.Today, DateTime.Today));
        }
        finally
        {
            FirebirdSql.Data.FirebirdClient.FbConnection.ClearAllPools();
            if (File.Exists(fdbPath)) File.Delete(fdbPath);
        }
    }
}
