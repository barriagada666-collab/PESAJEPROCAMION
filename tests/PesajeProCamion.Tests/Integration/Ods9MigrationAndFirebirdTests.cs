using PesajeProCamion.Data.Configuration;
using PesajeProCamion.Data.Infrastructure;
using PesajeProCamion.Data.Migration;
using PesajeProCamion.Data.Repositories;
using PesajeProCamion.Domain.Entities;
using Xunit;

namespace PesajeProCamion.Tests.Integration;

public sealed class Ods9MigrationAndFirebirdTests
{
    private const string LegacyGdbPath = @"d:\archivos qlabel\PESAJE CAMIONES\placillaPRUEBA\WMDBSPC.GDB";

    [Fact]
    public void Ods9BinaryReader_ReadsAllTablesFromRealLegacyDatabase()
    {
        Assert.True(File.Exists(LegacyGdbPath), $"No se encontró el archivo legacy esperado: {LegacyGdbPath}");
        var reader = new Ods9BinaryReader(LegacyGdbPath);
        Assert.Equal(9, reader.OdsMajor);
        Assert.Equal(1, reader.OdsMinor);

        Dictionary<string, List<Dictionary<string, object?>>> tables = reader.ReadAllUserTables();
        Assert.Equal(2735, tables["DBPESAJES"].Count);
        Assert.Equal(2725, tables["DBCAMIONES"].Count);
        Assert.Equal(263, tables["DBCLIENTES"].Count);
        Assert.Equal(48, tables["DBPRODUCTOS"].Count);
        Assert.Equal(11481, tables["DBLOGSINGRESO"].Count);
        Assert.Equal(4, tables["DBUSUARIOS"].Count);
        Assert.Equal(449, tables["DEMO1"].Count);
        Assert.Equal(64, tables["NM_CAMIONES"].Count);
    }

    [Fact]
    public async Task Ods9Migrator_MigratesAndCleansAllDataIntoRealFirebird25()
    {
        string fdbPath = Path.Combine(Path.GetTempPath(), $"wm_migr_{Guid.NewGuid():N}.fdb");
        var settings = new DatabaseSettings { DataSource = "localhost", Port = 3050, DatabasePath = fdbPath };
        try
        {
            MigrationAuditReport rep = await Ods9Migrator.MigrateAsync(LegacyGdbPath, settings, hashPasswords: true, overwriteTarget: true);
            Assert.Equal("9.1", rep.SourceOdsVersion);
            Assert.Equal(1, rep.NullPesajesQuarantined);
            Assert.Equal(2734, rep.TableRowCounts["DBPESAJES"]);
            Assert.True(rep.TableRowCounts["DBCAMIONES"] is >= 2700 and <= 2705, $"Cantidad inesperada: {rep.TableRowCounts["DBCAMIONES"]}");
            Assert.True(rep.CamionesDeduplicated >= 20);
            Assert.Equal(2725, rep.TableRowCounts["DBCAMIONES"] + rep.CamionesDeduplicated);
            Assert.Equal(6, rep.OrphanMopDetailsCleaned);
            Assert.Equal(4, rep.PasswordsHashed);

            var factory = new DatabaseSessionFactory(settings);
            var security = new SecurityRepository(factory);
            var weighing = new WeighingRepository(factory);
            var catalogs = new CatalogRepository(factory);

            Usuario? admin = await security.AuthenticateAsync(5, "B1986", 2);
            Assert.NotNull(admin);
            Assert.Equal("BORIS ARRIAGADA", admin.Nombre);
            Assert.StartsWith("PBKDF2$", admin.PasswordHash, StringComparison.Ordinal);

            BasculaInfo basc = await catalogs.GetBasculaAsync(1);
            Assert.Equal("CAMBIASO HNOS S.A.C", basc.NombreEmpresa);

            List<Pesaje> recent = await weighing.QueryPesajesAsync(folioExacto: 2740);
            Assert.Single(recent);
            Assert.Equal("CHHW80", recent[0].Patente);
            Assert.Equal(15450, recent[0].Neto);
        }
        finally
        {
            FirebirdSql.Data.FirebirdClient.FbConnection.ClearAllPools();
            if (File.Exists(fdbPath)) File.Delete(fdbPath);
        }
    }

    [Fact]
    public async Task RemoteNetworkUncDatabase_ResolvesAndQueriesSafelyInReadOnlyMode()
    {
        bool reachable = await RemoteDatabaseResolver.CheckTcpPortAsync("192.168.2.56", 3050, timeoutMs: 1200);
        if (!reachable)
        {
            // Entorno aislado sin acceso LAN al servidor 192.168.2.56
            return;
        }

        var rawSettings = new DatabaseSettings
        {
            DataSource = "localhost",
            Port = 3050,
            DatabasePath = @"\\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB"
        };

        DatabaseProbeResult probe = await RemoteDatabaseResolver.ResolveAndProbeAsync(rawSettings, rawSettings.DatabasePath, autoDetectDialectAndCharset: true);
        Assert.True(probe.Success, probe.ErrorMessage);
        Assert.Equal("192.168.2.56", probe.ResolvedSettings.DataSource);
        Assert.Equal("/bdwm/placilla2/WMDBSPC.GDB", probe.ResolvedSettings.DatabasePath, ignoreCase: true);
        Assert.Equal(1, probe.ResolvedSettings.Dialect);
        Assert.True(probe.IsLegacySchema);
        Assert.True(probe.TotalPesajes >= 2700);

        // Verificar lectura de repositorios sobre el esquema remoto ODS 9.1 / Dialect 1
        var factory = new DatabaseSessionFactory(probe.ResolvedSettings);
        var catalogs = new CatalogRepository(factory);
        var security = new SecurityRepository(factory);
        var weighing = new WeighingRepository(factory);

        BasculaInfo basc = await catalogs.GetBasculaAsync(1);
        Assert.False(string.IsNullOrWhiteSpace(basc.NombreEmpresa));
        ConfiguracionSistema cfg = await catalogs.GetConfigAsync();
        Assert.True(cfg.VisorLenData > 0);
        FoliosSistema fol = await catalogs.GetFoliosAsync();
        Assert.True(fol.FolioPesaje > 0);

        List<Usuario> users = await security.GetUsuariosAsync(soloActivos: true);
        Assert.NotEmpty(users);

        List<Pesaje> recent = await weighing.QueryPesajesAsync(maxRows: 10);
        Assert.Equal(10, recent.Count);
        Assert.All(recent, p => Assert.True(p.FolioPesaje > 0));

        // Verificar que la auditoría en BD remota sin DBAUDITLOG no lanza excepción
        List<AuditLogEntry> remoteAudit = await security.GetAuditLogsAsync(DateTime.Today.AddDays(-7), DateTime.Today);
        Assert.Empty(remoteAudit);
    }
}
