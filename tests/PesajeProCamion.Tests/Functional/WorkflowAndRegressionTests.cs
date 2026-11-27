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
    [Trait("Requires", "LocalFixture")]
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

    [Fact]
    [Trait("Requires", "LocalFixture")]
    public async Task AdminBarriagada_UserCrud_And_TicketFolioConflictProtection_WorkCleanly()
    {
        string fdbPath = Path.Combine(Path.GetTempPath(), $"wm_crud_{Guid.NewGuid():N}.fdb");
        var settings = new DatabaseSettings { DataSource = "localhost", Port = 3050, DatabasePath = fdbPath };
        try
        {
            await FirebirdSchemaManager.EnsureDatabaseInitializedAsync(settings);
            var db = new DatabaseSessionFactory(settings);
            var security = new SecurityRepository(db);
            var weighing = new WeighingRepository(db);

            // 1. Verificar cuenta administradora barriagada / Cambias0
            Usuario? admin = await security.AuthenticateByLoginAsync(null, "barriagada", "Cambias0", 2);
            Assert.NotNull(admin);
            Assert.Equal("BARRIAGADA", admin.Nombre);
            Assert.Equal(PerfilOperacion.Administracion, admin.Perfil);

            // 2. CRUD Usuarios + Reset Password + Protección último administrador
            await Assert.ThrowsAsync<InvalidOperationException>(() => security.DeleteUsuarioAsync(admin.Id, admin.Id));

            short opId = await security.SaveUsuarioAsync(new Usuario
            {
                Nombre = "OPERADOR UNO",
                Cargo = "BASCULA",
                Perfil = PerfilOperacion.OperacionPesaje,
                Activo = true,
                PuedeAgregar = true,
                PuedeImprimir = true,
                IngresoPesaje = true
            }, "ClaveInicial1", admin.Id);
            Assert.True(opId > 0);
            Assert.NotNull(await security.AuthenticateByLoginAsync(null, "OPERADOR UNO", "ClaveInicial1", 2));

            await security.ResetPasswordAsync(opId, "Nueva999", admin.Id);
            Assert.Null(await security.AuthenticateByLoginAsync(null, "OPERADOR UNO", "ClaveInicial1", 2));
            Assert.NotNull(await security.AuthenticateByLoginAsync(null, "OPERADOR UNO", "Nueva999", 2));

            await security.SetUsuarioActivoAsync(opId, false, admin.Id);
            Assert.Null(await security.AuthenticateByLoginAsync(null, "OPERADOR UNO", "Nueva999", 2));

            await security.DeleteUsuarioAsync(opId, admin.Id);

            // 3. Crear y Modificar Tickets respetando En Proceso sin conflicto de folio
            int inProcessFolio = await weighing.RegistrarIngresoTransitoAsync(new RegistroTransito
            {
                Patente = "PROC11",
                FechaIngreso = DateTime.Today,
                HoraIngreso = "10:00",
                Bruto = 14000,
                UsuarioIngreso = admin.Id
            });

            FolioAvailabilityResult chkInProcess = await weighing.CheckFolioAvailabilityAsync(inProcessFolio);
            Assert.False(chkInProcess.Disponible);
            Assert.Equal(FolioEstadoCheck.EnProcesoTransito, chkInProcess.Estado);

            // Intentar crear ticket manual usando el folio EN PROCESO debe ser rechazado
            await Assert.ThrowsAsync<InvalidOperationException>(() => weighing.CreateManualTicketAsync(new Pesaje
            {
                FolioPesaje = inProcessFolio,
                Patente = "CHOC99",
                Bruto = 20000,
                Tara = 10000,
                FechaIngreso = DateTime.Today,
                HoraIngreso = "10:15",
                FechaSalida = DateTime.Today,
                HoraSalida = "10:25"
            }, crearEnProceso: false, admin.Id));

            // Modificar vehículo En Proceso manteniendo exactamente su folio sin colisión
            RegistroTransito? trLoaded = await weighing.GetTransitoPorPatenteAsync("PROC11");
            Assert.NotNull(trLoaded);
            trLoaded.Observacion1 = "EDITADO EN PROCESO";
            await weighing.ModificarTransitoAsync(trLoaded, admin.Id);
            Assert.Equal("EDITADO EN PROCESO", (await weighing.GetTransitoPorPatenteAsync("PROC11"))!.Observacion1);

            // Crear ticket manual automático obtiene folio siguiente libre (> inProcessFolio)
            int manualFolio = await weighing.CreateManualTicketAsync(new Pesaje
            {
                Patente = "MANU22",
                Bruto = 32000,
                Tara = 12000,
                FechaIngreso = DateTime.Today,
                HoraIngreso = "11:00",
                FechaSalida = DateTime.Today,
                HoraSalida = "11:20"
            }, crearEnProceso: false, admin.Id);
            Assert.True(manualFolio > inProcessFolio);

            Pesaje? manualLoaded = await weighing.GetPesajePorFolioAsync(manualFolio);
            Assert.NotNull(manualLoaded);
            Assert.Equal(20000, manualLoaded.Neto);
            manualLoaded.Tara = 14000;
            await weighing.ModificarPesajeAsync(manualLoaded, admin.Id);
            Assert.Equal(18000, (await weighing.GetPesajePorFolioAsync(manualFolio))!.Neto);

            // 4. Ingreso manual independiente Bruto/Tara/Neto + División en 2 respetando En Proceso
            int splitSourceFolio = await weighing.CreateManualTicketAsync(new Pesaje
            {
                Patente = "DIV100",
                Cliente = "C1",
                Producto = "P1",
                Bruto = 25000,
                Tara = 10000,
                Neto = 10, // manual override (Neto = 10)
                FechaIngreso = DateTime.Today,
                HoraIngreso = "12:00",
                FechaSalida = DateTime.Today,
                HoraSalida = "12:15",
                TipoPesaje = TipoPesaje.Despacho
            }, crearEnProceso: false, admin.Id, preserveManualNeto: true);

            Pesaje? preSplit = await weighing.GetPesajePorFolioAsync(splitSourceFolio);
            Assert.NotNull(preSplit);
            Assert.Equal(10, preSplit.Neto);

            // Insertar un registro en tránsito justo después para confirmar que SplitTicketAsync salta el folio En Proceso
            int blockingTransitoFolio = await weighing.RegistrarIngresoTransitoAsync(new RegistroTransito
            {
                Patente = "BLOK88",
                FechaIngreso = DateTime.Today,
                HoraIngreso = "12:20",
                Bruto = 18000,
                UsuarioIngreso = admin.Id
            });

            (int b1, int t1, int n1, int b2, int t2, int n2) = TicketCustomizationHelper.ComputeSplitWeights(20, 10, 10, 6);
            TicketSplitResult splitRes = await weighing.SplitTicketAsync(new TicketSplitRequest
            {
                OriginalFolio = splitSourceFolio,
                NetoParte1 = n1,
                NetoParte2 = n2,
                BrutoParte1 = b1,
                TaraParte1 = t1,
                BrutoParte2 = b2,
                TaraParte2 = t2
            }, admin.Id);

            Assert.Equal(splitSourceFolio, splitRes.OriginalFolio);
            Assert.True(splitRes.NewFolio > blockingTransitoFolio);
            Assert.NotNull(await weighing.GetTransitoPorPatenteAsync("BLOK88")); // folio en proceso intacto

            Pesaje? pOrigAfter = await weighing.GetPesajePorFolioAsync(splitRes.OriginalFolio);
            Pesaje? pNewAfter = await weighing.GetPesajePorFolioAsync(splitRes.NewFolio);
            Assert.NotNull(pOrigAfter);
            Assert.NotNull(pNewAfter);
            Assert.Equal(6, pOrigAfter.Neto);
            Assert.Equal(4, pNewAfter.Neto);
            Assert.Equal("DIV100", pNewAfter.Patente);
            Assert.Equal(TipoPesaje.Despacho, pNewAfter.TipoPesaje);
        }
        finally
        {
            FirebirdSql.Data.FirebirdClient.FbConnection.ClearAllPools();
            if (File.Exists(fdbPath)) File.Delete(fdbPath);
        }
    }
}
