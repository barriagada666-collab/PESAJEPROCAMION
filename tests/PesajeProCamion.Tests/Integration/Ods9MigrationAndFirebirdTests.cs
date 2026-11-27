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
}
