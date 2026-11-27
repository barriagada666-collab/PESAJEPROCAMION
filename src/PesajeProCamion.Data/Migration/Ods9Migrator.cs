using System.Globalization;
using FirebirdSql.Data.FirebirdClient;
using PesajeProCamion.Data.Configuration;
using PesajeProCamion.Data.Schema;
using PesajeProCamion.Domain.Security;
using PesajeProCamion.Domain.Validation;

namespace PesajeProCamion.Data.Migration;

public sealed class MigrationAuditReport
{
    public string SourceFile { get; init; } = string.Empty;
    public string TargetDatabase { get; init; } = string.Empty;
    public string SourceOdsVersion { get; init; } = string.Empty;
    public Dictionary<string, int> TableRowCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int NullPesajesQuarantined { get; set; }
    public int CamionesDeduplicated { get; set; }
    public int CamionesWhitespaceNormalized { get; set; }
    public int SentinelDatesFixed { get; set; }
    public int OrphanMopDetailsCleaned { get; set; }
    public int PasswordsHashed { get; set; }
    public List<string> Findings { get; } = [];
}

/// <summary>Migra íntegramente un archivo GDB legacy ODS 9.1 hacia una base Firebird Dialect 3 UTF8 saneada.</summary>
public static class Ods9Migrator
{
    private static readonly DateTime SentinelDate = new(1899, 12, 30);

    public static async Task<MigrationAuditReport> MigrateAsync(
        string sourceGdbPath,
        DatabaseSettings targetSettings,
        bool hashPasswords = true,
        bool overwriteTarget = true,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceGdbPath);
        ArgumentNullException.ThrowIfNull(targetSettings);

        var reader = new Ods9BinaryReader(sourceGdbPath);
        Dictionary<string, List<Dictionary<string, object?>>> data = reader.ReadAllUserTables();

        if (overwriteTarget && File.Exists(targetSettings.DatabasePath))
        {
            FbConnection.ClearAllPools();
            File.Delete(targetSettings.DatabasePath);
        }

        await FirebirdSchemaManager.EnsureDatabaseInitializedAsync(targetSettings, ct).ConfigureAwait(false);

        var report = new MigrationAuditReport
        {
            SourceFile = sourceGdbPath,
            TargetDatabase = targetSettings.DatabasePath,
            SourceOdsVersion = $"{reader.OdsMajor}.{reader.OdsMinor}"
        };

        await using var conn = new FbConnection(targetSettings.BuildConnectionString());
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using FbTransaction tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            // Limpiar datos seed antes de poblar tablas desde origen legacy
            string[] clearOrder =
            [
                "DBAUDITLOG", "DBLOGSINGRESO", "DBMOPDETPJES", "DBMOPPJES", "DBTRANSITO", "DBTRAN_ANUL",
                "DBPESAJES", "DBCAMIONES", "DBCHOFERES", "DBTRANSP", "DBCLIENTES", "DBPRODUCTOS",
                "DBORIGEN", "DBDESTINOS", "DBUSUARIOS", "DBTCARGA", "DBTPESAJE", "DBPERFILES",
                "DBESTPESAJEMOP", "DBSISTACCION", "DBSISTEMAS", "DBHWSETUP", "DBBASCULAS", "DBCONFIG", "DBFOLIOS",
                "NM_CAMIONES", "NM_EJES", "NM_TANDEMS", "NM_LCAMION", "DEMO1"
            ];
            foreach (string tbl in clearOrder)
            {
                await ExecNonQueryAsync(conn, tx, $"DELETE FROM {tbl}", null, ct).ConfigureAwait(false);
            }

            await MigrateSimpleLookupsAsync(conn, tx, data, report, ct).ConfigureAwait(false);
            await MigrateUsersAsync(conn, tx, data, hashPasswords, report, ct).ConfigureAwait(false);
            await MigrateCatalogsAsync(conn, tx, data, report, ct).ConfigureAwait(false);
            await MigrateCamionesAsync(conn, tx, data, report, ct).ConfigureAwait(false);
            await MigratePesajesAndTransitoAsync(conn, tx, data, report, ct).ConfigureAwait(false);
            await MigrateMopAndDemoAsync(conn, tx, data, report, ct).ConfigureAwait(false);
            await MigrateLogsAndFoliosAsync(conn, tx, data, report, ct).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        return report;
    }

    private static async Task MigrateSimpleLookupsAsync(
        FbConnection conn,
        FbTransaction tx,
        Dictionary<string, List<Dictionary<string, object?>>> data,
        MigrationAuditReport report,
        CancellationToken ct)
    {
        foreach (Dictionary<string, object?> r in Rows(data, "DBPERFILES"))
        {
            await ExecAsync(conn, tx, "INSERT INTO DBPERFILES (PERFIL, DESCRIP) VALUES (@P, @D)",
                [("@P", Sh(r, "PERFIL")), ("@D", Str(r, "DESCRIP", 35))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBPERFILES"] = Rows(data, "DBPERFILES").Count;

        foreach (Dictionary<string, object?> r in Rows(data, "DBESTPESAJEMOP"))
        {
            await ExecAsync(conn, tx, "INSERT INTO DBESTPESAJEMOP (ESTADO, DESCRIP) VALUES (@E, @D)",
                [("@E", Sh(r, "ESTADO")), ("@D", Str(r, "DESCRIP", 30))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBESTPESAJEMOP"] = Rows(data, "DBESTPESAJEMOP").Count;

        foreach (Dictionary<string, object?> r in Rows(data, "DBSISTACCION"))
        {
            await ExecAsync(conn, tx, "INSERT INTO DBSISTACCION (ACCION, DESCRIP) VALUES (@A, @D)",
                [("@A", Sh(r, "ACCION")), ("@D", Str(r, "DESCRIP", 25))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBSISTACCION"] = Rows(data, "DBSISTACCION").Count;

        foreach (Dictionary<string, object?> r in Rows(data, "DBSISTEMAS"))
        {
            await ExecAsync(conn, tx, "INSERT INTO DBSISTEMAS (SISTEMA, DESCRIP, IP) VALUES (@S, @D, @IP)",
                [("@S", Sh(r, "SISTEMA")), ("@D", Str(r, "DESCRIP", 30)), ("@IP", NStr(r, "IP", 20))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBSISTEMAS"] = Rows(data, "DBSISTEMAS").Count;

        foreach (Dictionary<string, object?> r in Rows(data, "DBTPESAJE"))
        {
            await ExecAsync(conn, tx, "INSERT INTO DBTPESAJE (TPESAJE, DESCRIP) VALUES (@T, @D)",
                [("@T", Sh(r, "TPESAJE")), ("@D", Str(r, "DESCRIP", 30))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBTPESAJE"] = Rows(data, "DBTPESAJE").Count;

        foreach (Dictionary<string, object?> r in Rows(data, "DBTCARGA"))
        {
            await ExecAsync(conn, tx, "INSERT INTO DBTCARGA (TCARGA, DESCRIP, TCNORMAL, USUAING, USUAMOD) VALUES (@C, @D, @N, @UI, @UM)",
                [("@C", Sh(r, "TCARGA")), ("@D", Str(r, "DESCRIP", 60)), ("@N", Sh(r, "TCNORMAL")), ("@UI", Sh(r, "USUAING")), ("@UM", Sh(r, "USUAMOD"))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBTCARGA"] = Rows(data, "DBTCARGA").Count;

        foreach (Dictionary<string, object?> r in Rows(data, "DBHWSETUP"))
        {
            await ExecAsync(conn, tx, "INSERT INTO DBHWSETUP (DEVICE, COMNUMBER, BAUDRATE, PARITY, DATABITS, STOPBITS, PRINTER) VALUES (@DEV, @COM, @BR, @PAR, @DB, @SB, @PRN)",
                [("@DEV", Str(r, "DEVICE", 15)), ("@COM", Sh(r, "COMNUMBER", 1)), ("@BR", Int(r, "BAUDRATE")), ("@PAR", Int(r, "PARITY")), ("@DB", Int(r, "DATABITS", 1)), ("@SB", NInt(r, "STOPBITS")), ("@PRN", NStr(r, "PRINTER", 80))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBHWSETUP"] = Rows(data, "DBHWSETUP").Count;

        foreach (Dictionary<string, object?> r in Rows(data, "DBBASCULAS"))
        {
            await ExecAsync(conn, tx, "INSERT INTO DBBASCULAS (BASCULA, DESCRIP, FLPESAJEMOP, NROINST, RUT, NOMBRE, DIRECCION, FOLIO, USUAING, USUAMOD) VALUES (@B, @D, @FM, @NI, @RUT, @NOM, @DIR, @FOL, @UI, @UM)",
                [("@B", Sh(r, "BASCULA", 1)), ("@D", Str(r, "DESCRIP", 40)), ("@FM", Int(r, "FLPESAJEMOP", 1)), ("@NI", Str(r, "NROINST", 10)), ("@RUT", Str(r, "RUT", 12)), ("@NOM", Str(r, "NOMBRE", 60)), ("@DIR", Str(r, "DIRECCION", 60)), ("@FOL", NInt(r, "FOLIO")), ("@UI", Sh(r, "USUAING")), ("@UM", Sh(r, "USUAMOD"))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBBASCULAS"] = Rows(data, "DBBASCULAS").Count;

        foreach (Dictionary<string, object?> r in Rows(data, "DBCONFIG"))
        {
            await ExecAsync(conn, tx, """
                INSERT INTO DBCONFIG (
                    ID, FECHAINST, NPORCTARA, NROCOPTCK, BASCENCERO, CEROBASC, DINACTIVO, STABLEESTATICO,
                    CONFIMPTICKET, SISTOP, PRINTINDEX, PRINTINDEX2, TITLE1, TITLE2, TITLE3, SEGACTIVA,
                    VMOPUNDER, VMOPOVER, VMOPPRESAMPLE, VMOPMINSAMPLE, VMOPLOWSAMPLES, VMOPHISAMPLES,
                    VMOPINSIDESAMPLES, VMOPCUTLEFT, VMOPCUTRIGHT, VMOPMAXNODES, VLIMIT, VDELTA,
                    VISOR_STX, VISOR_POSDATA, VISOR_LENDATA, VNRO_FILTROS, DSTDUPFACTOR, DSTDDOWNFACTOR,
                    OFFSET1, OFFSET2, OFFSET3, OFFSET4, OFFSET5, OFFSET6, OFFSET7,
                    CALIB1, CALIB2, CALIB3, CALIB4, CALIB5, CALIB6, CALIB7)
                VALUES (
                    1, @FI, @NPT, @NRC, @BEC, @CB, @DA, @SE, @CIT, @SO, @P1, @P2, @T1, @T2, @T3, @SA,
                    @VU, @VO, @VPS, @VMS, @VLS, @VHS, @VIS, @VCL, @VCR, @VMN, @VL, @VD,
                    @VSTX, @VPOS, @VLEN, @VNF, @DUF, @DDF,
                    @O1, @O2, @O3, @O4, @O5, @O6, @O7, @C1, @C2, @C3, @C4, @C5, @C6, @C7)
                """,
                [
                    ("@FI", Dt(r, "FECHAINST")), ("@NPT", Int(r, "NPORCTARA", 10)), ("@NRC", Int(r, "NROCOPTCK", 1)),
                    ("@BEC", Str(r, "BASCENCERO", 1, "1")), ("@CB", Int(r, "CEROBASC", 50)), ("@DA", Str(r, "DINACTIVO", 1, "1")),
                    ("@SE", NStr(r, "STABLEESTATICO", 1)), ("@CIT", Str(r, "CONFIMPTICKET", 1, "1")), ("@SO", Sh(r, "SISTOP", 1)),
                    ("@P1", Int(r, "PRINTINDEX")), ("@P2", Int(r, "PRINTINDEX2")),
                    ("@T1", NStr(r, "TITLE1", 40)), ("@T2", NStr(r, "TITLE2", 40)), ("@T3", NStr(r, "TITLE3", 40)),
                    ("@SA", NStr(r, "SEGACTIVA", 1)), ("@VU", Int(r, "VMOPUNDER", 350)), ("@VO", Int(r, "VMOPOVER", 350)),
                    ("@VPS", Int(r, "VMOPPRESAMPLE", 800)), ("@VMS", Int(r, "VMOPMINSAMPLE", 500)),
                    ("@VLS", Int(r, "VMOPLOWSAMPLES", 5)), ("@VHS", Int(r, "VMOPHISAMPLES", 12)),
                    ("@VIS", Int(r, "VMOPINSIDESAMPLES")), ("@VCL", Int(r, "VMOPCUTLEFT", 30)),
                    ("@VCR", Int(r, "VMOPCUTRIGHT", 30)), ("@VMN", Int(r, "VMOPMAXNODES")),
                    ("@VL", Int(r, "VLIMIT", 1)), ("@VD", Int(r, "VDELTA", 10)),
                    ("@VSTX", Int(r, "VISOR_STX", 2)), ("@VPOS", Int(r, "VISOR_POSDATA", 3)),
                    ("@VLEN", Int(r, "VISOR_LENDATA", 5)), ("@VNF", Int(r, "VNRO_FILTROS", 80)),
                    ("@DUF", Dbl(r, "DSTDUPFACTOR", 1.0)), ("@DDF", Dbl(r, "DSTDDOWNFACTOR", 1.0)),
                    ("@O1", Int(r, "OFFSET1")), ("@O2", Int(r, "OFFSET2")), ("@O3", Int(r, "OFFSET3")),
                    ("@O4", Int(r, "OFFSET4")), ("@O5", Int(r, "OFFSET5")), ("@O6", Int(r, "OFFSET6")), ("@O7", Int(r, "OFFSET7")),
                    ("@C1", Int(r, "CALIB1")), ("@C2", Int(r, "CALIB2")), ("@C3", Int(r, "CALIB3")),
                    ("@C4", Int(r, "CALIB4")), ("@C5", Int(r, "CALIB5")), ("@C6", Int(r, "CALIB6")), ("@C7", Int(r, "CALIB7"))
                ], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBCONFIG"] = Rows(data, "DBCONFIG").Count;
    }

    private static async Task MigrateUsersAsync(
        FbConnection conn,
        FbTransaction tx,
        Dictionary<string, List<Dictionary<string, object?>>> data,
        bool hashPasswords,
        MigrationAuditReport report,
        CancellationToken ct)
    {
        List<Dictionary<string, object?>> users = Rows(data, "DBUSUARIOS");
        foreach (Dictionary<string, object?> r in users)
        {
            string rawPw = Str(r, "PASS_WORD", 30, "12345");
            string storedPw = hashPasswords ? PasswordHasher.HashPassword(rawPw) : rawPw;
            if (hashPasswords)
            {
                report.PasswordsHashed++;
            }

            await ExecAsync(conn, tx, """
                INSERT INTO DBUSUARIOS (
                    USUARIO, NOMBRE, CARGO, PASS_WORD, ACTIVO, MODIFICAR, AGREGAR, ELIMINAR, IMPRIMIR,
                    CAMBIOPW, INGRESOPJ, INGRESOGT, SUPSISTEMA, PERFIL, JTURNO, USUAING, USUAMOD)
                VALUES (
                    @U, @N, @C, @PW, @AC, @MO, @AG, @EL, @IM, @CP, @IP, @IG, @SS, @PE, @JT, @UI, @UM)
                """,
                [
                    ("@U", Sh(r, "USUARIO")), ("@N", Str(r, "NOMBRE", 40)), ("@C", Str(r, "CARGO", 30)),
                    ("@PW", storedPw), ("@AC", Flag(r, "ACTIVO", "1")), ("@MO", Flag(r, "MODIFICAR", "0")),
                    ("@AG", Flag(r, "AGREGAR", "0")), ("@EL", Flag(r, "ELIMINAR", "0")), ("@IM", Flag(r, "IMPRIMIR", "1")),
                    ("@CP", Flag(r, "CAMBIOPW", "1")), ("@IP", Flag(r, "INGRESOPJ", "1")), ("@IG", Flag(r, "INGRESOGT", "0")),
                    ("@SS", Flag(r, "SUPSISTEMA", "0")), ("@PE", Sh(r, "PERFIL")), ("@JT", Flag(r, "JTURNO", "0")),
                    ("@UI", Sh(r, "USUAING")), ("@UM", Sh(r, "USUAMOD"))
                ], ct).ConfigureAwait(false);
        }
        int migratedCount = users.Count;
        bool hasBarriagada = users.Any(u => string.Equals(Str(u, "NOMBRE", 40).Trim(), "BARRIAGADA", StringComparison.OrdinalIgnoreCase));
        if (!hasBarriagada)
        {
            short maxUserId = users.Count > 0 ? users.Max(u => Sh(u, "USUARIO")) : (short)0;
            short barId = (short)(maxUserId + 1);
            await ExecAsync(conn, tx, """
                INSERT INTO DBUSUARIOS (
                    USUARIO, NOMBRE, CARGO, PASS_WORD, ACTIVO, MODIFICAR, AGREGAR, ELIMINAR, IMPRIMIR,
                    CAMBIOPW, INGRESOPJ, INGRESOGT, SUPSISTEMA, PERFIL, JTURNO, USUAING, USUAMOD)
                VALUES (
                    @U, 'BARRIAGADA', 'ADMINISTRADOR PRINCIPAL', @PW, '1', '1', '1', '1', '1', '1', '1', '1', '1', 2, '1', @U, @U)
                """,
                [("@U", barId), ("@PW", PasswordHasher.HashPassword("Cambias0"))], ct).ConfigureAwait(false);
            migratedCount++;
        }

        report.TableRowCounts["DBUSUARIOS"] = migratedCount;
        if (hashPasswords && users.Count > 0)
        {
            report.Findings.Add($"Se convirtieron {report.PasswordsHashed} contraseñas en texto plano a hash PBKDF2-SHA256.");
        }
    }

    private static async Task MigrateCatalogsAsync(
        FbConnection conn,
        FbTransaction tx,
        Dictionary<string, List<Dictionary<string, object?>>> data,
        MigrationAuditReport report,
        CancellationToken ct)
    {
        var seenCli = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int cliCount = 0;
        foreach (Dictionary<string, object?> r in Rows(data, "DBCLIENTES"))
        {
            string rut = Str(r, "RUT", 10).Trim();
            if (string.IsNullOrEmpty(rut) || !seenCli.Add(rut))
            {
                continue;
            }
            cliCount++;
            await ExecAsync(conn, tx, """
                INSERT INTO DBCLIENTES (RUT, DIGITO, NOMBRE, DIRECCION, GIRO, COMUNA, CIUDAD, TELEFONO, FAX, CASILLA, USUAING, USUAMOD)
                VALUES (@RUT, @DV, @NOM, @DIR, @GIR, @COM, @CIU, @TEL, @FAX, @CAS, @UI, @UM)
                """,
                [
                    ("@RUT", rut), ("@DV", Str(r, "DIGITO", 1)), ("@NOM", Str(r, "NOMBRE", 60)),
                    ("@DIR", Str(r, "DIRECCION", 60)), ("@GIR", Str(r, "GIRO", 40)), ("@COM", Str(r, "COMUNA", 40)),
                    ("@CIU", Str(r, "CIUDAD", 30)), ("@TEL", Str(r, "TELEFONO", 25)), ("@FAX", Str(r, "FAX", 25)),
                    ("@CAS", Str(r, "CASILLA", 25)), ("@UI", Sh(r, "USUAING")), ("@UM", Sh(r, "USUAMOD"))
                ], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBCLIENTES"] = cliCount;

        var seenProd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int prodCount = 0;
        foreach (Dictionary<string, object?> r in Rows(data, "DBPRODUCTOS"))
        {
            string cod = Str(r, "PRODUCTO", 12).Trim();
            if (string.IsNullOrEmpty(cod) || !seenProd.Add(cod))
            {
                continue;
            }
            prodCount++;
            await ExecAsync(conn, tx, "INSERT INTO DBPRODUCTOS (PRODUCTO, DESCRIP, USUAING, USUAMOD) VALUES (@P, @D, @UI, @UM)",
                [("@P", cod), ("@D", Str(r, "DESCRIP", 60)), ("@UI", Sh(r, "USUAING")), ("@UM", Sh(r, "USUAMOD"))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBPRODUCTOS"] = prodCount;

        var seenOrig = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int origCount = 0;
        foreach (Dictionary<string, object?> r in Rows(data, "DBORIGEN"))
        {
            string cod = Str(r, "ORIGEN", 10).Trim();
            if (string.IsNullOrEmpty(cod) || !seenOrig.Add(cod))
            {
                continue;
            }
            origCount++;
            await ExecAsync(conn, tx, "INSERT INTO DBORIGEN (ORIGEN, DESCRIP, USUAING, USUAMOD) VALUES (@O, @D, @UI, @UM)",
                [("@O", cod), ("@D", Str(r, "DESCRIP", 40)), ("@UI", Sh(r, "USUAING")), ("@UM", Sh(r, "USUAMOD"))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBORIGEN"] = origCount;

        var seenDest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int destCount = 0;
        foreach (Dictionary<string, object?> r in Rows(data, "DBDESTINOS"))
        {
            string cod = Str(r, "DESTINO", 10).Trim();
            if (string.IsNullOrEmpty(cod) || !seenDest.Add(cod))
            {
                continue;
            }
            destCount++;
            await ExecAsync(conn, tx, "INSERT INTO DBDESTINOS (DESTINO, DESCRIP, USUAING, USUAMOD) VALUES (@DE, @D, @UI, @UM)",
                [("@DE", cod), ("@D", Str(r, "DESCRIP", 40)), ("@UI", Sh(r, "USUAING")), ("@UM", Sh(r, "USUAMOD"))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBDESTINOS"] = destCount;

        var seenChof = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int chofCount = 0;
        foreach (Dictionary<string, object?> r in Rows(data, "DBCHOFERES"))
        {
            string cod = Str(r, "CHOFER", 12).Trim();
            if (string.IsNullOrEmpty(cod) || !seenChof.Add(cod))
            {
                continue;
            }
            chofCount++;
            await ExecAsync(conn, tx, "INSERT INTO DBCHOFERES (CHOFER, DIGITO, NOMBRE, USUAING, USUAMOD) VALUES (@C, @DV, @N, @UI, @UM)",
                [("@C", cod), ("@DV", Str(r, "DIGITO", 1)), ("@N", Str(r, "NOMBRE", 45)), ("@UI", Sh(r, "USUAING")), ("@UM", Sh(r, "USUAMOD"))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBCHOFERES"] = chofCount;

        var seenTr = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int trCount = 0;
        foreach (Dictionary<string, object?> r in Rows(data, "DBTRANSP"))
        {
            string cod = Str(r, "TRANSP", 10).Trim();
            if (string.IsNullOrEmpty(cod) || !seenTr.Add(cod))
            {
                continue;
            }
            trCount++;
            await ExecAsync(conn, tx, "INSERT INTO DBTRANSP (TRANSP, DIGITO, NOMBRE, DIRECCION, CIUDAD, TELEFONO, FAX, USUAING, USUAMOD) VALUES (@T, @DV, @N, @D, @C, @TE, @F, @UI, @UM)",
                [("@T", cod), ("@DV", Str(r, "DIGITO", 1)), ("@N", Str(r, "NOMBRE", 45)), ("@D", Str(r, "DIRECCION", 60)), ("@C", Str(r, "CIUDAD", 30)), ("@TE", Str(r, "TELEFONO", 25)), ("@F", Str(r, "FAX", 25)), ("@UI", Sh(r, "USUAING")), ("@UM", Sh(r, "USUAMOD"))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBTRANSP"] = trCount;
    }

    private static async Task MigrateCamionesAsync(
        FbConnection conn,
        FbTransaction tx,
        Dictionary<string, List<Dictionary<string, object?>>> data,
        MigrationAuditReport report,
        CancellationToken ct)
    {
        var merged = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        foreach (Dictionary<string, object?> r in Rows(data, "DBCAMIONES"))
        {
            string rawPat = r.TryGetValue("PATENTE", out object? pv) && pv is string ps ? ps : string.Empty;
            string pat = ChileanValidators.NormalizarPatente(rawPat);
            if (string.IsNullOrEmpty(pat))
            {
                continue;
            }
            if (!string.Equals(rawPat, pat, StringComparison.Ordinal))
            {
                report.CamionesWhitespaceNormalized++;
            }

            if (merged.TryGetValue(pat, out Dictionary<string, object?>? prev))
            {
                report.CamionesDeduplicated++;
                DateTime prevDt = DtRaw(prev, "FTARA") ?? DateTime.MinValue;
                DateTime curDt = DtRaw(r, "FTARA") ?? DateTime.MinValue;
                int prevTara = Int(prev, "TARA");
                int curTara = Int(r, "TARA");
                if (curDt > prevDt || (curDt == prevDt && curTara > 0 && prevTara == 0))
                {
                    merged[pat] = r;
                }
            }
            else
            {
                merged[pat] = r;
            }
        }

        foreach ((string pat, Dictionary<string, object?> r) in merged)
        {
            DateTime? ftara = Dt(r, "FTARA", report);
            DateTime? ftarareg = Dt(r, "FTARAREG", report);
            await ExecAsync(conn, tx, """
                INSERT INTO DBCAMIONES (PATENTE, ACOPLADO, TRANSP, CHOFER, TMOP, LARGO, TARA, FTARA, HTARA, TARAREG, FTARAREG, NDIAS, USUAING, USUAMOD)
                VALUES (@P, @AC, @TR, @CH, @TM, @LA, @TA, @FT, @HT, @TRG, @FTRG, @ND, @UI, @UM)
                """,
                [
                    ("@P", pat), ("@AC", ChileanValidators.NormalizarPatente(Str(r, "ACOPLADO", 8))),
                    ("@TR", Str(r, "TRANSP", 10)), ("@CH", Str(r, "CHOFER", 35)),
                    ("@TM", Sh(r, "TMOP")), ("@LA", Sh(r, "LARGO")), ("@TA", Int(r, "TARA")),
                    ("@FT", ftara), ("@HT", Str(r, "HTARA", 5)),
                    ("@TRG", Flag(r, "TARAREG", "0")), ("@FTRG", ftarareg), ("@ND", Sh(r, "NDIAS")),
                    ("@UI", Sh(r, "USUAING")), ("@UM", Sh(r, "USUAMOD"))
                ], ct).ConfigureAwait(false);
        }

        report.TableRowCounts["DBCAMIONES"] = merged.Count;
        if (report.CamionesDeduplicated > 0 || report.CamionesWhitespaceNormalized > 0)
        {
            report.Findings.Add(
                $"DBCAMIONES saneado: {report.CamionesWhitespaceNormalized} patentes normalizadas, {report.CamionesDeduplicated} duplicados por espacios consolidados.");
        }
    }

    private static async Task MigratePesajesAndTransitoAsync(
        FbConnection conn,
        FbTransaction tx,
        Dictionary<string, List<Dictionary<string, object?>>> data,
        MigrationAuditReport report,
        CancellationToken ct)
    {
        var seenFolios = new HashSet<int>();
        int migrated = 0;

        foreach (Dictionary<string, object?> r in Rows(data, "DBPESAJES"))
        {
            int? folio = NInt(r, "FLPESAJE");
            if (folio is null || folio <= 0 || !seenFolios.Add(folio.Value))
            {
                report.NullPesajesQuarantined++;
                continue;
            }

            migrated++;
            string pat = ChileanValidators.NormalizarPatente(Str(r, "PATENTE", 8));
            if (string.IsNullOrEmpty(pat))
            {
                pat = "SINPAT";
            }

            string obs1 = Str(r, "OBSERVA1", 120);
            string obs2 = Str(r, "OBSERVA2", 120);
            int fardos = NInt(r, "FARDOS") ?? Domain.Entities.TicketCustomizationHelper.ExtractFardosFromObservacion(obs1, obs2);

            await ExecAsync(conn, tx, """
                INSERT INTO DBPESAJES (
                    FLPESAJE, PATENTE, ACOPLADO, FLPESAJEMOP, PRODUCTO, CLIENTE, ORIGEN, DESTINO, GDESPACHO,
                    NETOINF, FECHAING, HORAING, FECHASAL, HORASAL, FECHAMOD, HORAMOD,
                    BRUTO, TARA, NETO, TMOP, LARGO, MAXBRUTO, ESTADOMOP, CHOFER, TRASPASO,
                    OBSERVA1, OBSERVA2, FARDOS, TRANSP, TARAREG, TPESAJE, SAVEDTOSERVER,
                    USUAING, USUASAL, USUAMOD, USUADEL, ANULADO)
                VALUES (
                    @FP, @PAT, @ACO, @FM, @PR, @CL, @OR, @DE, @GD,
                    @NI, @FI, @HI, @FS, @HS, @FMOD, @HMOD,
                    @BR, @TA, @NE, @TM, @LA, @MB, @EM, @CH, @TRS,
                    @OB1, @OB2, @FAR, @TR, @TRG, @TP, @SV,
                    @UI, @US, @UM, @UD, @AN)
                """,
                [
                    ("@FP", folio.Value), ("@PAT", pat), ("@ACO", ChileanValidators.NormalizarPatente(Str(r, "ACOPLADO", 8))),
                    ("@FM", Int(r, "FLPESAJEMOP")), ("@PR", Str(r, "PRODUCTO", 12)), ("@CL", Str(r, "CLIENTE", 10)),
                    ("@OR", Str(r, "ORIGEN", 10)), ("@DE", Str(r, "DESTINO", 10)), ("@GD", Str(r, "GDESPACHO", 50)),
                    ("@NI", Int(r, "NETOINF")),
                    ("@FI", Dt(r, "FECHAING") ?? DateTime.Today), ("@HI", Str(r, "HORAING", 5)),
                    ("@FS", Dt(r, "FECHASAL")), ("@HS", Str(r, "HORASAL", 5)),
                    ("@FMOD", Dt(r, "FECHAMOD")), ("@HMOD", NStr(r, "HORAMOD", 5)),
                    ("@BR", Int(r, "BRUTO")), ("@TA", Int(r, "TARA")), ("@NE", Int(r, "NETO")),
                    ("@TM", Sh(r, "TMOP")), ("@LA", Sh(r, "LARGO")), ("@MB", Int(r, "MAXBRUTO")),
                    ("@EM", Flag(r, "ESTADOMOP", "0")), ("@CH", Str(r, "CHOFER", 35)), ("@TRS", NStr(r, "TRASPASO", 10)),
                    ("@OB1", obs1), ("@OB2", obs2), ("@FAR", fardos),
                    ("@TR", Str(r, "TRANSP", 10)), ("@TRG", Flag(r, "TARAREG", "0")),
                    ("@TP", Sh(r, "TPESAJE", 1)), ("@SV", NStr(r, "SAVEDTOSERVER", 1)),
                    ("@UI", Sh(r, "USUAING")), ("@US", Sh(r, "USUASAL")), ("@UM", Sh(r, "USUAMOD")),
                    ("@UD", Sh(r, "USUADEL")), ("@AN", Flag(r, "ANULADO", "0"))
                ], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBPESAJES"] = migrated;
        if (report.NullPesajesQuarantined > 0)
        {
            report.Findings.Add($"Se excluyeron {report.NullPesajesQuarantined} fila(s) corrupta(s) en DBPESAJES con FLPESAJE nulo/duplicado.");
        }

        int trCount = 0;
        foreach (Dictionary<string, object?> r in Rows(data, "DBTRANSITO"))
        {
            int? folio = NInt(r, "FLPESAJE");
            if (folio is null || folio <= 0)
            {
                continue;
            }
            trCount++;
            string trObs1 = Str(r, "OBSERVA1", 120);
            int trFardos = NInt(r, "FARDOS") ?? Domain.Entities.TicketCustomizationHelper.ExtractFardosFromObservacion(trObs1, null);
            await ExecAsync(conn, tx, """
                INSERT INTO DBTRANSITO (
                    FLPESAJE, PATENTE, ACOPLADO, PRODUCTO, CLIENTE, ORIGEN, DESTINO, GDESPACHO,
                    NETOINF, FECHAING, HORAING, BRUTO, TARA, TMOP, LARGO, MAXBRUTO, CHOFER,
                    OBSERVA1, FARDOS, TRANSP, TPESAJE, SAVEDTOSERVER, USUAING, TRASPASO)
                VALUES (
                    @FP, @PAT, @ACO, @PR, @CL, @OR, @DE, @GD,
                    @NI, @FI, @HI, @BR, @TA, @TM, @LA, @MB, @CH,
                    @OB1, @FAR, @TR, @TP, @SV, @UI, @TRS)
                """,
                [
                    ("@FP", folio.Value), ("@PAT", ChileanValidators.NormalizarPatente(Str(r, "PATENTE", 8))),
                    ("@ACO", ChileanValidators.NormalizarPatente(Str(r, "ACOPLADO", 8))),
                    ("@PR", Str(r, "PRODUCTO", 12)), ("@CL", Str(r, "CLIENTE", 10)),
                    ("@OR", Str(r, "ORIGEN", 10)), ("@DE", Str(r, "DESTINO", 10)),
                    ("@GD", Str(r, "GDESPACHO", 50)), ("@NI", Int(r, "NETOINF")),
                    ("@FI", Dt(r, "FECHAING") ?? DateTime.Today), ("@HI", Str(r, "HORAING", 5)),
                    ("@BR", Int(r, "BRUTO")), ("@TA", Int(r, "TARA")),
                    ("@TM", Sh(r, "TMOP")), ("@LA", Sh(r, "LARGO")), ("@MB", Int(r, "MAXBRUTO")),
                    ("@CH", Str(r, "CHOFER", 35)), ("@OB1", trObs1), ("@FAR", trFardos),
                    ("@TR", Str(r, "TRANSP", 10)), ("@TP", Sh(r, "TPESAJE", 1)),
                    ("@SV", NStr(r, "SAVEDTOSERVER", 1)), ("@UI", Sh(r, "USUAING")), ("@TRS", NStr(r, "TRASPASO", 10))
                ], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBTRANSITO"] = trCount;
    }

    private static async Task MigrateMopAndDemoAsync(
        FbConnection conn,
        FbTransaction tx,
        Dictionary<string, List<Dictionary<string, object?>>> data,
        MigrationAuditReport report,
        CancellationToken ct)
    {
        foreach (Dictionary<string, object?> r in Rows(data, "NM_LCAMION"))
        {
            await ExecAsync(conn, tx, "INSERT INTO NM_LCAMION (DESDE, HASTA, MAXIMO) VALUES (@D, @H, @M)",
                [("@D", Sh(r, "DESDE")), ("@H", Sh(r, "HASTA")), ("@M", Dbl(r, "MAXIMO"))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["NM_LCAMION"] = Rows(data, "NM_LCAMION").Count;

        foreach (Dictionary<string, object?> r in Rows(data, "NM_TANDEMS"))
        {
            await ExecAsync(conn, tx, "INSERT INTO NM_TANDEMS (TANDEM, NOMBRE, MAXIMO, TOLERANCIA) VALUES (@T, @N, @M, @TO)",
                [("@T", Sh(r, "TANDEM")), ("@N", Str(r, "NOMBRE", 25)), ("@M", Dbl(r, "MAXIMO")), ("@TO", Int(r, "TOLERANCIA"))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["NM_TANDEMS"] = Rows(data, "NM_TANDEMS").Count;

        foreach (Dictionary<string, object?> r in Rows(data, "NM_EJES"))
        {
            await ExecAsync(conn, tx, "INSERT INTO NM_EJES (TANDEM, EJE, TIPO_EJE, MAXIMO, TOLERANCIA, NOMEJE) VALUES (@T, @E, @TE, @M, @TO, @NE)",
                [("@T", Sh(r, "TANDEM")), ("@E", Sh(r, "EJE")), ("@TE", Sh(r, "TIPO_EJE")), ("@M", Dbl(r, "MAXIMO")), ("@TO", Int(r, "TOLERANCIA")), ("@NE", Str(r, "NOMEJE", 10))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["NM_EJES"] = Rows(data, "NM_EJES").Count;

        foreach (Dictionary<string, object?> r in Rows(data, "NM_CAMIONES"))
        {
            await ExecAsync(conn, tx, "INSERT INTO NM_CAMIONES (TIPO, TANDEM01, TANDEM02, TANDEM03, TANDEM04, MAXIMO) VALUES (@T, @T1, @T2, @T3, @T4, @M)",
                [("@T", Sh(r, "TIPO")), ("@T1", Sh(r, "TANDEM01")), ("@T2", Sh(r, "TANDEM02")), ("@T3", Sh(r, "TANDEM03")), ("@T4", Sh(r, "TANDEM04")), ("@M", NInt(r, "MAXIMO"))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["NM_CAMIONES"] = Rows(data, "NM_CAMIONES").Count;

        var validMopKeys = new HashSet<(int, short)>();
        foreach (Dictionary<string, object?> r in Rows(data, "DBMOPPJES"))
        {
            int flmop = Int(r, "FLMOP");
            short basc = Sh(r, "BASCULA", 1);
            validMopKeys.Add((flmop, basc));
            await ExecAsync(conn, tx, """
                INSERT INTO DBMOPPJES (FLMOP, BASCULA, FLPESAJE, PATENTE, ACOPLADO, FECHA, HORA, TCARGA, TRANSP, TMOP, LARGO, BRUTO, MAXIMO, SOBREPESO, GDESPACHO, CHOFER, ESTADO, SAVEDTOSERVER, USUARIO)
                VALUES (@FM, @B, @FP, @P, @A, @F, @H, @TC, @TR, @TM, @LA, @BR, @MA, @SP, @GD, @CH, @ES, @SV, @US)
                """,
                [
                    ("@FM", flmop), ("@B", basc), ("@FP", Int(r, "FLPESAJE")),
                    ("@P", ChileanValidators.NormalizarPatente(Str(r, "PATENTE", 8))),
                    ("@A", ChileanValidators.NormalizarPatente(Str(r, "ACOPLADO", 8))),
                    ("@F", Dt(r, "FECHA") ?? DateTime.Today), ("@H", Str(r, "HORA", 5)),
                    ("@TC", Sh(r, "TCARGA", 7)), ("@TR", Str(r, "TRANSP", 10)),
                    ("@TM", Sh(r, "TMOP")), ("@LA", Sh(r, "LARGO")), ("@BR", Int(r, "BRUTO")),
                    ("@MA", Int(r, "MAXIMO")), ("@SP", Int(r, "SOBREPESO")), ("@GD", Str(r, "GDESPACHO", 12)),
                    ("@CH", Str(r, "CHOFER", 35)), ("@ES", Flag(r, "ESTADO", "1")), ("@SV", NStr(r, "SAVEDTOSERVER", 1)),
                    ("@US", Sh(r, "USUARIO"))
                ], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBMOPPJES"] = validMopKeys.Count;

        int detCount = 0;
        var seenDet = new HashSet<(int, short, short)>();
        foreach (Dictionary<string, object?> r in Rows(data, "DBMOPDETPJES"))
        {
            int flmop = Int(r, "FLMOP");
            short basc = Sh(r, "BASCULA", 1);
            short eje = Sh(r, "EJNRO", 1);
            if (!validMopKeys.Contains((flmop, basc)))
            {
                report.OrphanMopDetailsCleaned++;
                continue;
            }
            if (!seenDet.Add((flmop, basc, eje)))
            {
                continue;
            }
            detCount++;
            await ExecAsync(conn, tx, """
                INSERT INTO DBMOPDETPJES (FLMOP, BASCULA, EJNRO, EJTPO, EJPES, EJLIM, TDNRO, TDTPO, TDPES, TDLIM, SAVEDTOSERVER)
                VALUES (@FM, @B, @EN, @ET, @EP, @EL, @TN, @TT, @TP, @TL, @SV)
                """,
                [
                    ("@FM", flmop), ("@B", basc), ("@EN", eje), ("@ET", Sh(r, "EJTPO")),
                    ("@EP", Int(r, "EJPES")), ("@EL", Int(r, "EJLIM")), ("@TN", Sh(r, "TDNRO")),
                    ("@TT", Sh(r, "TDTPO")), ("@TP", Int(r, "TDPES")), ("@TL", Int(r, "TDLIM")), ("@SV", NStr(r, "SAVEDTOSERVER", 1))
                ], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBMOPDETPJES"] = detCount;
        if (report.OrphanMopDetailsCleaned > 0)
        {
            report.Findings.Add($"Se limpiaron {report.OrphanMopDetailsCleaned} filas huérfanas de DBMOPDETPJES sin cabecera DBMOPPJES.");
        }

        int demoId = 0;
        foreach (Dictionary<string, object?> r in Rows(data, "DEMO1"))
        {
            demoId++;
            await ExecAsync(conn, tx, "INSERT INTO DEMO1 (ID_MUESTRA, PESO, HORA) VALUES (@ID, @P, @H)",
                [("@ID", demoId), ("@P", Dbl(r, "PESO")), ("@H", Str(r, "HORA", 10))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DEMO1"] = demoId;
    }

    private static async Task MigrateLogsAndFoliosAsync(
        FbConnection conn,
        FbTransaction tx,
        Dictionary<string, List<Dictionary<string, object?>>> data,
        MigrationAuditReport report,
        CancellationToken ct)
    {
        int idLog = 0;
        foreach (Dictionary<string, object?> r in Rows(data, "DBLOGSINGRESO"))
        {
            idLog++;
            await ExecAsync(conn, tx, "INSERT INTO DBLOGSINGRESO (ID_LOG, USUARIO, SISTEMA, FECHA, HORA, TURNO, ACCION) VALUES (@ID, @U, @S, @F, @H, @T, @A)",
                [("@ID", idLog), ("@U", Int(r, "USUARIO")), ("@S", Int(r, "SISTEMA", 1)), ("@F", Dt(r, "FECHA") ?? DateTime.Today), ("@H", Str(r, "HORA", 5)), ("@T", Int(r, "TURNO")), ("@A", Int(r, "ACCION", 1))], ct).ConfigureAwait(false);
        }
        report.TableRowCounts["DBLOGSINGRESO"] = idLog;

        Dictionary<string, object?>? fRow = Rows(data, "DBFOLIOS").FirstOrDefault();
        int maxPesaje = Math.Max(report.TableRowCounts.GetValueOrDefault("DBPESAJES", 0), 0);
        int flPesaje = Math.Max(fRow is not null ? Int(fRow, "FLPESAJE", 1) : 1, maxPesaje + 1);
        int flMop = fRow is not null ? Int(fRow, "FLPESAJEMOP", 1) : 1;
        int flUsu = Math.Max(fRow is not null ? Int(fRow, "FLUSUARIO", 1) : 1, 1);

        await ExecAsync(conn, tx, "INSERT INTO DBFOLIOS (ID, FLPESAJE, FLPESAJEMOP, FLUSUARIO, FLGDESPACHO) VALUES (1, @FP, @FM, @FU, 1)",
            [("@FP", flPesaje), ("@FM", flMop), ("@FU", flUsu)], ct).ConfigureAwait(false);
        report.TableRowCounts["DBFOLIOS"] = 1;

        await FirebirdSchemaManager.SynchronizeGeneratorAsync(conn, tx, "GEN_FLPESAJE", Math.Max(0, flPesaje - 1), ct).ConfigureAwait(false);
        await FirebirdSchemaManager.SynchronizeGeneratorAsync(conn, tx, "GEN_FLPESAJEMOP", Math.Max(0, flMop - 1), ct).ConfigureAwait(false);
        await FirebirdSchemaManager.SynchronizeGeneratorAsync(conn, tx, "GEN_FLUSUARIO", Math.Max(0, flUsu - 1), ct).ConfigureAwait(false);
        await FirebirdSchemaManager.SynchronizeGeneratorAsync(conn, tx, "GEN_LOGINGRESO", idLog, ct).ConfigureAwait(false);
        await FirebirdSchemaManager.SynchronizeGeneratorAsync(conn, tx, "GEN_AUDITLOG", 0, ct).ConfigureAwait(false);

        if (report.SentinelDatesFixed > 0)
        {
            report.Findings.Add($"Se convirtieron {report.SentinelDatesFixed} fechas centinela (1899-12-30) a NULL.");
        }
    }

    private static List<Dictionary<string, object?>> Rows(Dictionary<string, List<Dictionary<string, object?>>> data, string name)
        => data.TryGetValue(name, out List<Dictionary<string, object?>>? list) ? list : [];

    private static async Task ExecAsync(FbConnection conn, FbTransaction tx, string sql, (string, object?)[] parameters, CancellationToken ct)
    {
        await using var cmd = new FbCommand(sql, conn, tx);
        foreach ((string p, object? v) in parameters)
        {
            cmd.Parameters.AddWithValue(p, v ?? DBNull.Value);
        }
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task ExecNonQueryAsync(FbConnection conn, FbTransaction tx, string sql, (string, object?)[]? parameters, CancellationToken ct)
    {
        await using var cmd = new FbCommand(sql, conn, tx);
        if (parameters is not null)
        {
            foreach ((string p, object? v) in parameters)
            {
                cmd.Parameters.AddWithValue(p, v ?? DBNull.Value);
            }
        }
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string Str(Dictionary<string, object?> r, string col, int maxLen, string fallback = "")
    {
        if (!r.TryGetValue(col, out object? val) || val is null)
        {
            return fallback;
        }
        string s = Convert.ToString(val, CultureInfo.InvariantCulture)?.TrimEnd() ?? fallback;
        return s.Length > maxLen ? s[..maxLen] : s;
    }

    private static string? NStr(Dictionary<string, object?> r, string col, int maxLen)
    {
        if (!r.TryGetValue(col, out object? val) || val is null)
        {
            return null;
        }
        string s = (Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty).Trim();
        if (s.Length == 0)
        {
            return null;
        }
        return s.Length > maxLen ? s[..maxLen] : s;
    }

    private static string Flag(Dictionary<string, object?> r, string col, string fallback)
    {
        string s = Str(r, col, 1, fallback).Trim();
        return string.IsNullOrEmpty(s) ? fallback : s[..1];
    }

    private static int Int(Dictionary<string, object?> r, string col, int fallback = 0)
        => r.TryGetValue(col, out object? val) && val is not null ? Convert.ToInt32(val, CultureInfo.InvariantCulture) : fallback;

    private static int? NInt(Dictionary<string, object?> r, string col)
        => r.TryGetValue(col, out object? val) && val is not null ? Convert.ToInt32(val, CultureInfo.InvariantCulture) : null;

    private static short Sh(Dictionary<string, object?> r, string col, short fallback = 0)
        => r.TryGetValue(col, out object? val) && val is not null ? Convert.ToInt16(val, CultureInfo.InvariantCulture) : fallback;

    private static double Dbl(Dictionary<string, object?> r, string col, double fallback = 0.0)
        => r.TryGetValue(col, out object? val) && val is not null ? Convert.ToDouble(val, CultureInfo.InvariantCulture) : fallback;

    private static DateTime? DtRaw(Dictionary<string, object?> r, string col)
        => r.TryGetValue(col, out object? val) && val is DateTime dt ? dt.Date : null;

    private static DateTime? Dt(Dictionary<string, object?> r, string col, MigrationAuditReport? report = null)
    {
        if (!r.TryGetValue(col, out object? val) || val is not DateTime dt)
        {
            return null;
        }
        if (dt.Date <= SentinelDate)
        {
            if (report is not null)
            {
                report.SentinelDatesFixed++;
            }
            return null;
        }
        return dt.Date;
    }
}
