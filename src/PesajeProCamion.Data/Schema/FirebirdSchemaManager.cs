using System.Text;
using FirebirdSql.Data.FirebirdClient;
using PesajeProCamion.Data.Configuration;

namespace PesajeProCamion.Data.Schema;

/// <summary>Crea e inicializa de manera idempotente la base de datos Firebird Dialect 3 UTF8.</summary>
public static class FirebirdSchemaManager
{
    public static async Task EnsureDatabaseInitializedAsync(DatabaseSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? dir = Path.GetDirectoryName(settings.DatabasePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string connStr = settings.BuildConnectionString();
        bool exists = await CanOpenAsync(connStr, ct).ConfigureAwait(false);
        if (!exists)
        {
            FbConnection.CreateDatabase(connStr, pageSize: 8192, forcedWrites: true, overwrite: false);
        }

        await using var conn = new FbConnection(connStr);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await EnsureSequencesAsync(conn, ct).ConfigureAwait(false);
        await EnsureTablesAsync(conn, ct).ConfigureAwait(false);
        await EnsureIndexesAsync(conn, ct).ConfigureAwait(false);
        await EnsureSeedDefaultsAsync(conn, ct).ConfigureAwait(false);
    }

    private static async Task<bool> CanOpenAsync(string connStr, CancellationToken ct)
    {
        try
        {
            await using var conn = new FbConnection(connStr);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (FbException)
        {
            return false;
        }
    }

    private static async Task EnsureSequencesAsync(FbConnection conn, CancellationToken ct)
    {
        string[] seqs = ["GEN_FLPESAJE", "GEN_FLPESAJEMOP", "GEN_FLUSUARIO", "GEN_LOGINGRESO", "GEN_AUDITLOG"];
        foreach (string seq in seqs)
        {
            await using var check = new FbCommand("SELECT COUNT(*) FROM RDB$GENERATORS WHERE TRIM(RDB$GENERATOR_NAME) = @N", conn);
            check.Parameters.AddWithValue("@N", seq);
            int count = Convert.ToInt32(await check.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            if (count == 0)
            {
                await ExecuteDdlAsync(conn, $"CREATE SEQUENCE {seq}", ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task EnsureTablesAsync(FbConnection conn, CancellationToken ct)
    {
        (string TableName, string Ddl)[] tables =
        [
            ("DBBASCULAS", """
                CREATE TABLE DBBASCULAS (
                    BASCULA SMALLINT NOT NULL PRIMARY KEY,
                    DESCRIP VARCHAR(40),
                    FLPESAJEMOP INTEGER DEFAULT 1 NOT NULL,
                    NROINST VARCHAR(10),
                    RUT VARCHAR(12),
                    NOMBRE VARCHAR(60),
                    DIRECCION VARCHAR(60),
                    FOLIO INTEGER,
                    USUAING SMALLINT DEFAULT 0,
                    USUAMOD SMALLINT DEFAULT 0
                )
                """),
            ("DBCONFIG", """
                CREATE TABLE DBCONFIG (
                    ID SMALLINT NOT NULL PRIMARY KEY,
                    FECHAINST DATE,
                    NPORCTARA INTEGER DEFAULT 10 NOT NULL,
                    NROCOPTCK INTEGER DEFAULT 1 NOT NULL,
                    BASCENCERO CHAR(1) DEFAULT '1',
                    CEROBASC INTEGER DEFAULT 50,
                    DINACTIVO CHAR(1) DEFAULT '1',
                    STABLEESTATICO CHAR(1),
                    CONFIMPTICKET CHAR(1) DEFAULT '1',
                    SISTOP SMALLINT DEFAULT 1,
                    PRINTINDEX INTEGER DEFAULT 0,
                    PRINTINDEX2 INTEGER DEFAULT 0,
                    TITLE1 VARCHAR(40),
                    TITLE2 VARCHAR(40),
                    TITLE3 VARCHAR(40),
                    SEGACTIVA CHAR(1),
                    VMOPUNDER INTEGER DEFAULT 350,
                    VMOPOVER INTEGER DEFAULT 350,
                    VMOPPRESAMPLE INTEGER DEFAULT 800,
                    VMOPMINSAMPLE INTEGER DEFAULT 500,
                    VMOPLOWSAMPLES INTEGER DEFAULT 5,
                    VMOPHISAMPLES INTEGER DEFAULT 12,
                    VMOPINSIDESAMPLES INTEGER DEFAULT 0,
                    VMOPCUTLEFT INTEGER DEFAULT 30,
                    VMOPCUTRIGHT INTEGER DEFAULT 30,
                    VMOPMAXNODES INTEGER DEFAULT 0,
                    VLIMIT INTEGER DEFAULT 1,
                    VDELTA INTEGER DEFAULT 10,
                    VISOR_STX INTEGER DEFAULT 2,
                    VISOR_POSDATA INTEGER DEFAULT 3,
                    VISOR_LENDATA INTEGER DEFAULT 5,
                    VNRO_FILTROS INTEGER DEFAULT 80,
                    DSTDUPFACTOR DOUBLE PRECISION DEFAULT 1.0,
                    DSTDDOWNFACTOR DOUBLE PRECISION DEFAULT 1.0,
                    OFFSET1 INTEGER DEFAULT 0,
                    OFFSET2 INTEGER DEFAULT 0,
                    OFFSET3 INTEGER DEFAULT 0,
                    OFFSET4 INTEGER DEFAULT 0,
                    OFFSET5 INTEGER DEFAULT 0,
                    OFFSET6 INTEGER DEFAULT 0,
                    OFFSET7 INTEGER DEFAULT 0,
                    CALIB1 INTEGER DEFAULT 0,
                    CALIB2 INTEGER DEFAULT 0,
                    CALIB3 INTEGER DEFAULT 0,
                    CALIB4 INTEGER DEFAULT 0,
                    CALIB5 INTEGER DEFAULT 0,
                    CALIB6 INTEGER DEFAULT 0,
                    CALIB7 INTEGER DEFAULT 0
                )
                """),
            ("DBFOLIOS", """
                CREATE TABLE DBFOLIOS (
                    ID SMALLINT NOT NULL PRIMARY KEY,
                    FLPESAJE INTEGER DEFAULT 1 NOT NULL,
                    FLPESAJEMOP INTEGER DEFAULT 1 NOT NULL,
                    FLUSUARIO INTEGER DEFAULT 1 NOT NULL,
                    FLGDESPACHO INTEGER DEFAULT 1
                )
                """),
            ("DBHWSETUP", """
                CREATE TABLE DBHWSETUP (
                    DEVICE VARCHAR(15) NOT NULL PRIMARY KEY,
                    COMNUMBER SMALLINT DEFAULT 1 NOT NULL,
                    BAUDRATE INTEGER DEFAULT 0 NOT NULL,
                    PARITY INTEGER DEFAULT 0 NOT NULL,
                    DATABITS INTEGER DEFAULT 1 NOT NULL,
                    STOPBITS INTEGER DEFAULT 0,
                    PRINTER VARCHAR(80)
                )
                """),
            ("DBPERFILES", """
                CREATE TABLE DBPERFILES (
                    PERFIL SMALLINT NOT NULL PRIMARY KEY,
                    DESCRIP VARCHAR(35) NOT NULL
                )
                """),
            ("DBESTPESAJEMOP", """
                CREATE TABLE DBESTPESAJEMOP (
                    ESTADO SMALLINT NOT NULL PRIMARY KEY,
                    DESCRIP VARCHAR(30) NOT NULL
                )
                """),
            ("DBSISTACCION", """
                CREATE TABLE DBSISTACCION (
                    ACCION SMALLINT NOT NULL PRIMARY KEY,
                    DESCRIP VARCHAR(25) NOT NULL
                )
                """),
            ("DBSISTEMAS", """
                CREATE TABLE DBSISTEMAS (
                    SISTEMA SMALLINT NOT NULL PRIMARY KEY,
                    DESCRIP VARCHAR(30) NOT NULL,
                    IP VARCHAR(20)
                )
                """),
            ("DBTPESAJE", """
                CREATE TABLE DBTPESAJE (
                    TPESAJE SMALLINT NOT NULL PRIMARY KEY,
                    DESCRIP VARCHAR(30) NOT NULL
                )
                """),
            ("DBTCARGA", """
                CREATE TABLE DBTCARGA (
                    TCARGA SMALLINT NOT NULL PRIMARY KEY,
                    DESCRIP VARCHAR(60) NOT NULL,
                    TCNORMAL SMALLINT DEFAULT 0 NOT NULL,
                    USUAING SMALLINT DEFAULT 0,
                    USUAMOD SMALLINT DEFAULT 0
                )
                """),
            ("DBUSUARIOS", """
                CREATE TABLE DBUSUARIOS (
                    USUARIO SMALLINT NOT NULL PRIMARY KEY,
                    NOMBRE VARCHAR(40) NOT NULL,
                    CARGO VARCHAR(30),
                    PASS_WORD VARCHAR(128) NOT NULL,
                    ACTIVO CHAR(1) DEFAULT '1' NOT NULL,
                    MODIFICAR CHAR(1) DEFAULT '0' NOT NULL,
                    AGREGAR CHAR(1) DEFAULT '0' NOT NULL,
                    ELIMINAR CHAR(1) DEFAULT '0' NOT NULL,
                    IMPRIMIR CHAR(1) DEFAULT '1' NOT NULL,
                    CAMBIOPW CHAR(1) DEFAULT '1' NOT NULL,
                    INGRESOPJ CHAR(1) DEFAULT '1' NOT NULL,
                    INGRESOGT CHAR(1) DEFAULT '0' NOT NULL,
                    SUPSISTEMA CHAR(1) DEFAULT '0',
                    PERFIL SMALLINT DEFAULT 0 NOT NULL,
                    JTURNO CHAR(1) DEFAULT '0',
                    USUAING SMALLINT DEFAULT 0,
                    USUAMOD SMALLINT DEFAULT 0
                )
                """),
            ("DBCLIENTES", """
                CREATE TABLE DBCLIENTES (
                    RUT VARCHAR(10) NOT NULL PRIMARY KEY,
                    DIGITO VARCHAR(1),
                    NOMBRE VARCHAR(60) NOT NULL,
                    DIRECCION VARCHAR(60),
                    GIRO VARCHAR(40),
                    COMUNA VARCHAR(40),
                    CIUDAD VARCHAR(30),
                    TELEFONO VARCHAR(25),
                    FAX VARCHAR(25),
                    CASILLA VARCHAR(25),
                    USUAING SMALLINT DEFAULT 0,
                    USUAMOD SMALLINT DEFAULT 0
                )
                """),
            ("DBPRODUCTOS", """
                CREATE TABLE DBPRODUCTOS (
                    PRODUCTO VARCHAR(12) NOT NULL PRIMARY KEY,
                    DESCRIP VARCHAR(60) NOT NULL,
                    USUAING SMALLINT DEFAULT 0,
                    USUAMOD SMALLINT DEFAULT 0
                )
                """),
            ("DBORIGEN", """
                CREATE TABLE DBORIGEN (
                    ORIGEN VARCHAR(10) NOT NULL PRIMARY KEY,
                    DESCRIP VARCHAR(40) NOT NULL,
                    USUAING SMALLINT DEFAULT 0,
                    USUAMOD SMALLINT DEFAULT 0
                )
                """),
            ("DBDESTINOS", """
                CREATE TABLE DBDESTINOS (
                    DESTINO VARCHAR(10) NOT NULL PRIMARY KEY,
                    DESCRIP VARCHAR(40) NOT NULL,
                    USUAING SMALLINT DEFAULT 0,
                    USUAMOD SMALLINT DEFAULT 0
                )
                """),
            ("DBCHOFERES", """
                CREATE TABLE DBCHOFERES (
                    CHOFER VARCHAR(12) NOT NULL PRIMARY KEY,
                    DIGITO VARCHAR(1),
                    NOMBRE VARCHAR(45) NOT NULL,
                    USUAING SMALLINT DEFAULT 0,
                    USUAMOD SMALLINT DEFAULT 0
                )
                """),
            ("DBTRANSP", """
                CREATE TABLE DBTRANSP (
                    TRANSP VARCHAR(10) NOT NULL PRIMARY KEY,
                    DIGITO VARCHAR(1),
                    NOMBRE VARCHAR(45) NOT NULL,
                    DIRECCION VARCHAR(60),
                    CIUDAD VARCHAR(30),
                    TELEFONO VARCHAR(25),
                    FAX VARCHAR(25),
                    USUAING SMALLINT DEFAULT 0,
                    USUAMOD SMALLINT DEFAULT 0
                )
                """),
            ("DBCAMIONES", """
                CREATE TABLE DBCAMIONES (
                    PATENTE VARCHAR(8) NOT NULL PRIMARY KEY,
                    ACOPLADO VARCHAR(8),
                    TRANSP VARCHAR(10),
                    CHOFER VARCHAR(35),
                    TMOP SMALLINT DEFAULT 0,
                    LARGO SMALLINT DEFAULT 0,
                    TARA INTEGER DEFAULT 0,
                    FTARA DATE,
                    HTARA VARCHAR(5),
                    TARAREG CHAR(1) DEFAULT '0',
                    FTARAREG DATE,
                    NDIAS SMALLINT DEFAULT 0,
                    USUAING SMALLINT DEFAULT 0,
                    USUAMOD SMALLINT DEFAULT 0
                )
                """),
            ("DBTRANSITO", """
                CREATE TABLE DBTRANSITO (
                    FLPESAJE INTEGER NOT NULL PRIMARY KEY,
                    PATENTE VARCHAR(8) NOT NULL,
                    ACOPLADO VARCHAR(8),
                    PRODUCTO VARCHAR(12),
                    CLIENTE VARCHAR(10),
                    ORIGEN VARCHAR(10),
                    DESTINO VARCHAR(10),
                    GDESPACHO VARCHAR(12),
                    NETOINF INTEGER DEFAULT 0,
                    FECHAING DATE NOT NULL,
                    HORAING VARCHAR(5),
                    BRUTO INTEGER DEFAULT 0,
                    TARA INTEGER DEFAULT 0,
                    TMOP SMALLINT DEFAULT 0,
                    LARGO SMALLINT DEFAULT 0,
                    MAXBRUTO INTEGER DEFAULT 0,
                    CHOFER VARCHAR(35),
                    OBSERVA1 VARCHAR(120),
                    TRANSP VARCHAR(10),
                    TPESAJE SMALLINT DEFAULT 1 NOT NULL,
                    SAVEDTOSERVER CHAR(1),
                    USUAING SMALLINT DEFAULT 0,
                    TRASPASO VARCHAR(10)
                )
                """),
            ("DBTRAN_ANUL", """
                CREATE TABLE DBTRAN_ANUL (
                    FLPESAJE INTEGER NOT NULL PRIMARY KEY,
                    PATENTE VARCHAR(8),
                    ACOPLADO VARCHAR(8),
                    PRODUCTO VARCHAR(12),
                    CLIENTE VARCHAR(10),
                    DESTINO VARCHAR(10),
                    GDESPACHO VARCHAR(12),
                    NETOINF INTEGER DEFAULT 0,
                    FECHAING DATE,
                    HORAING VARCHAR(5),
                    BRUTO INTEGER DEFAULT 0,
                    TARA INTEGER DEFAULT 0,
                    TMOP SMALLINT DEFAULT 0,
                    LARGO SMALLINT DEFAULT 0,
                    MAXBRUTO INTEGER DEFAULT 0,
                    OBSERVA VARCHAR(120),
                    CHOFER VARCHAR(35),
                    TRANSP VARCHAR(10),
                    USUAING SMALLINT DEFAULT 0,
                    FECHAANUL DATE,
                    HORAANUL VARCHAR(5),
                    USUAANUL SMALLINT DEFAULT 0,
                    MOTIVO VARCHAR(120)
                )
                """),
            ("DBPESAJES", """
                CREATE TABLE DBPESAJES (
                    FLPESAJE INTEGER NOT NULL PRIMARY KEY,
                    PATENTE VARCHAR(8) NOT NULL,
                    ACOPLADO VARCHAR(8),
                    FLPESAJEMOP INTEGER DEFAULT 0,
                    PRODUCTO VARCHAR(12),
                    CLIENTE VARCHAR(10),
                    ORIGEN VARCHAR(10),
                    DESTINO VARCHAR(10),
                    GDESPACHO VARCHAR(12),
                    NETOINF INTEGER DEFAULT 0,
                    FECHAING DATE NOT NULL,
                    HORAING VARCHAR(5),
                    FECHASAL DATE,
                    HORASAL VARCHAR(5),
                    FECHAMOD DATE,
                    HORAMOD VARCHAR(5),
                    BRUTO INTEGER DEFAULT 0 NOT NULL,
                    TARA INTEGER DEFAULT 0 NOT NULL,
                    NETO INTEGER DEFAULT 0 NOT NULL,
                    TMOP SMALLINT DEFAULT 0,
                    LARGO SMALLINT DEFAULT 0,
                    MAXBRUTO INTEGER DEFAULT 0,
                    ESTADOMOP CHAR(1) DEFAULT '0',
                    CHOFER VARCHAR(35),
                    TRASPASO VARCHAR(10),
                    OBSERVA1 VARCHAR(120),
                    OBSERVA2 VARCHAR(120),
                    TRANSP VARCHAR(10),
                    TARAREG CHAR(1) DEFAULT '0',
                    TPESAJE SMALLINT DEFAULT 1 NOT NULL,
                    SAVEDTOSERVER CHAR(1),
                    USUAING SMALLINT DEFAULT 0,
                    USUASAL SMALLINT DEFAULT 0,
                    USUAMOD SMALLINT DEFAULT 0,
                    USUADEL SMALLINT DEFAULT 0,
                    ANULADO CHAR(1) DEFAULT '0' NOT NULL
                )
                """),
            ("DBMOPPJES", """
                CREATE TABLE DBMOPPJES (
                    FLMOP INTEGER NOT NULL,
                    BASCULA SMALLINT NOT NULL,
                    FLPESAJE INTEGER DEFAULT 0,
                    PATENTE VARCHAR(8) NOT NULL,
                    ACOPLADO VARCHAR(8),
                    FECHA DATE NOT NULL,
                    HORA VARCHAR(5),
                    TCARGA SMALLINT DEFAULT 7,
                    TRANSP VARCHAR(10),
                    TMOP SMALLINT DEFAULT 0,
                    LARGO SMALLINT DEFAULT 0,
                    BRUTO INTEGER DEFAULT 0,
                    MAXIMO INTEGER DEFAULT 0,
                    SOBREPESO INTEGER DEFAULT 0,
                    GDESPACHO VARCHAR(12),
                    CHOFER VARCHAR(35),
                    ESTADO CHAR(1) DEFAULT '1',
                    SAVEDTOSERVER CHAR(1),
                    USUARIO SMALLINT DEFAULT 0,
                    CONSTRAINT PK_DBMOPPJES PRIMARY KEY (FLMOP, BASCULA)
                )
                """),
            ("DBMOPDETPJES", """
                CREATE TABLE DBMOPDETPJES (
                    FLMOP INTEGER NOT NULL,
                    BASCULA SMALLINT NOT NULL,
                    EJNRO SMALLINT NOT NULL,
                    EJTPO SMALLINT DEFAULT 0,
                    EJPES INTEGER DEFAULT 0,
                    EJLIM INTEGER DEFAULT 0,
                    TDNRO SMALLINT DEFAULT 0,
                    TDTPO SMALLINT DEFAULT 0,
                    TDPES INTEGER DEFAULT 0,
                    TDLIM INTEGER DEFAULT 0,
                    SAVEDTOSERVER CHAR(1),
                    CONSTRAINT PK_DBMOPDETPJES PRIMARY KEY (FLMOP, BASCULA, EJNRO)
                )
                """),
            ("DBLOGSINGRESO", """
                CREATE TABLE DBLOGSINGRESO (
                    ID_LOG INTEGER NOT NULL PRIMARY KEY,
                    USUARIO INTEGER NOT NULL,
                    SISTEMA INTEGER NOT NULL,
                    FECHA DATE NOT NULL,
                    HORA VARCHAR(5) NOT NULL,
                    TURNO INTEGER DEFAULT 0,
                    ACCION INTEGER NOT NULL
                )
                """),
            ("DBAUDITLOG", """
                CREATE TABLE DBAUDITLOG (
                    ID_AUDIT INTEGER NOT NULL PRIMARY KEY,
                    FECHA DATE NOT NULL,
                    HORA VARCHAR(8) NOT NULL,
                    USUARIO SMALLINT NOT NULL,
                    MODULO VARCHAR(25) NOT NULL,
                    ENTIDAD VARCHAR(30) NOT NULL,
                    CLAVE VARCHAR(30) NOT NULL,
                    ACCION VARCHAR(20) NOT NULL,
                    DETALLE VARCHAR(240)
                )
                """),
            ("NM_LCAMION", """
                CREATE TABLE NM_LCAMION (
                    DESDE SMALLINT NOT NULL,
                    HASTA SMALLINT NOT NULL,
                    MAXIMO DOUBLE PRECISION NOT NULL,
                    CONSTRAINT PK_NM_LCAMION PRIMARY KEY (DESDE, HASTA)
                )
                """),
            ("NM_TANDEMS", """
                CREATE TABLE NM_TANDEMS (
                    TANDEM SMALLINT NOT NULL PRIMARY KEY,
                    NOMBRE VARCHAR(25) NOT NULL,
                    MAXIMO DOUBLE PRECISION NOT NULL,
                    TOLERANCIA INTEGER NOT NULL
                )
                """),
            ("NM_EJES", """
                CREATE TABLE NM_EJES (
                    TANDEM SMALLINT NOT NULL,
                    EJE SMALLINT NOT NULL,
                    TIPO_EJE SMALLINT NOT NULL,
                    MAXIMO DOUBLE PRECISION NOT NULL,
                    TOLERANCIA INTEGER NOT NULL,
                    NOMEJE VARCHAR(10) NOT NULL,
                    CONSTRAINT PK_NM_EJES PRIMARY KEY (TANDEM, EJE)
                )
                """),
            ("NM_CAMIONES", """
                CREATE TABLE NM_CAMIONES (
                    TIPO SMALLINT NOT NULL PRIMARY KEY,
                    TANDEM01 SMALLINT DEFAULT 0 NOT NULL,
                    TANDEM02 SMALLINT DEFAULT 0 NOT NULL,
                    TANDEM03 SMALLINT DEFAULT 0 NOT NULL,
                    TANDEM04 SMALLINT DEFAULT 0 NOT NULL,
                    MAXIMO INTEGER
                )
                """),
            ("DEMO1", """
                CREATE TABLE DEMO1 (
                    ID_MUESTRA INTEGER NOT NULL PRIMARY KEY,
                    PESO DOUBLE PRECISION NOT NULL,
                    HORA VARCHAR(10) NOT NULL
                )
                """),
            ("EXP_STGO_SAP", """
                CREATE TABLE EXP_STGO_SAP (
                    LIN_DATOS VARCHAR(120)
                )
                """),
            ("EXP_PROV_STGO", """
                CREATE TABLE EXP_PROV_STGO (
                    LINEA VARCHAR(120)
                )
                """)
        ];

        foreach ((string tableName, string ddl) in tables)
        {
            await using var check = new FbCommand("SELECT COUNT(*) FROM RDB$RELATIONS WHERE TRIM(RDB$RELATION_NAME) = @N", conn);
            check.Parameters.AddWithValue("@N", tableName);
            int exists = Convert.ToInt32(await check.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            if (exists == 0)
            {
                await ExecuteDdlAsync(conn, ddl, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task EnsureIndexesAsync(FbConnection conn, CancellationToken ct)
    {
        (string Name, string Ddl)[] indexes =
        [
            ("IDX_DBPESAJES_FECHASAL", "CREATE INDEX IDX_DBPESAJES_FECHASAL ON DBPESAJES (FECHASAL)"),
            ("IDX_DBPESAJES_PATENTE", "CREATE INDEX IDX_DBPESAJES_PATENTE ON DBPESAJES (PATENTE)"),
            ("IDX_DBPESAJES_CLIENTE", "CREATE INDEX IDX_DBPESAJES_CLIENTE ON DBPESAJES (CLIENTE)"),
            ("IDX_DBPESAJES_PRODUCTO", "CREATE INDEX IDX_DBPESAJES_PRODUCTO ON DBPESAJES (PRODUCTO)"),
            ("IDX_DBPESAJES_TRANSP", "CREATE INDEX IDX_DBPESAJES_TRANSP ON DBPESAJES (TRANSP)"),
            ("IDX_DBPESAJES_TPESAJE", "CREATE INDEX IDX_DBPESAJES_TPESAJE ON DBPESAJES (TPESAJE)"),
            ("IDX_DBTRANSITO_PATENTE", "CREATE INDEX IDX_DBTRANSITO_PATENTE ON DBTRANSITO (PATENTE)"),
            ("IDX_DBLOGSINGRESO_FECHA", "CREATE INDEX IDX_DBLOGSINGRESO_FECHA ON DBLOGSINGRESO (FECHA, SISTEMA)"),
            ("IDX_DBAUDITLOG_FECHA", "CREATE INDEX IDX_DBAUDITLOG_FECHA ON DBAUDITLOG (FECHA)"),
            ("IDX_DBUSUARIOS_NOMBRE", "CREATE UNIQUE INDEX IDX_DBUSUARIOS_NOMBRE ON DBUSUARIOS (NOMBRE)")
        ];

        foreach ((string name, string ddl) in indexes)
        {
            await using var check = new FbCommand("SELECT COUNT(*) FROM RDB$INDICES WHERE TRIM(RDB$INDEX_NAME) = @N", conn);
            check.Parameters.AddWithValue("@N", name);
            int exists = Convert.ToInt32(await check.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            if (exists == 0)
            {
                await ExecuteDdlAsync(conn, ddl, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task EnsureSeedDefaultsAsync(FbConnection conn, CancellationToken ct)
    {
        // Catálogos base requeridos si la base se crea desde cero
        await using var checkPerfil = new FbCommand("SELECT COUNT(*) FROM DBPERFILES", conn);
        if (Convert.ToInt32(await checkPerfil.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 0)
        {
            string[] seedSqls =
            [
                "INSERT INTO DBPERFILES (PERFIL, DESCRIP) VALUES (0, 'Operacion de Pesaje')",
                "INSERT INTO DBPERFILES (PERFIL, DESCRIP) VALUES (1, 'Consulta de Datos')",
                "INSERT INTO DBPERFILES (PERFIL, DESCRIP) VALUES (2, 'Administracion')",
                "INSERT INTO DBESTPESAJEMOP (ESTADO, DESCRIP) VALUES (1, 'Dentro de Norma')",
                "INSERT INTO DBESTPESAJEMOP (ESTADO, DESCRIP) VALUES (2, 'Fuera de Norma')",
                "INSERT INTO DBSISTACCION (ACCION, DESCRIP) VALUES (1, 'Ingreso')",
                "INSERT INTO DBSISTACCION (ACCION, DESCRIP) VALUES (2, 'Salida')",
                "INSERT INTO DBSISTACCION (ACCION, DESCRIP) VALUES (3, 'Mov. Interno')",
                "INSERT INTO DBSISTEMAS (SISTEMA, DESCRIP) VALUES (1, 'GESTION')",
                "INSERT INTO DBSISTEMAS (SISTEMA, DESCRIP) VALUES (2, 'PESAJE DE CAMIONES')",
                "INSERT INTO DBTPESAJE (TPESAJE, DESCRIP) VALUES (1, 'Recepcion de carga')",
                "INSERT INTO DBTPESAJE (TPESAJE, DESCRIP) VALUES (2, 'Despacho de Carga')",
                "INSERT INTO DBTPESAJE (TPESAJE, DESCRIP) VALUES (3, 'Movimiento Interno')",
                "INSERT INTO DBFOLIOS (ID, FLPESAJE, FLPESAJEMOP, FLUSUARIO, FLGDESPACHO) VALUES (1, 1, 1, 2, 1)",
                "INSERT INTO DBHWSETUP (DEVICE, COMNUMBER, BAUDRATE, PARITY, DATABITS, STOPBITS) VALUES ('BASCULA', 1, 0, 0, 1, 0)",
                "INSERT INTO DBHWSETUP (DEVICE, COMNUMBER, BAUDRATE, PARITY, DATABITS, STOPBITS) VALUES ('PRINTER', 1, 0, 0, 1, 0)",
                "INSERT INTO DBBASCULAS (BASCULA, DESCRIP, FLPESAJEMOP, NROINST, RUT, NOMBRE, DIRECCION, USUAING, USUAMOD) VALUES (1, 'BASCULA PRINCIPAL', 1, '1', '1-9', 'EMPRESA DEMO', 'PLANTA CENTRAL', 1, 1)",
                "INSERT INTO DBCONFIG (ID, FECHAINST, NPORCTARA, NROCOPTCK, BASCENCERO, CEROBASC, DINACTIVO, CONFIMPTICKET, SISTOP, VMOPUNDER, VMOPOVER, VMOPPRESAMPLE, VMOPMINSAMPLE, VMOPLOWSAMPLES, VMOPHISAMPLES, VMOPCUTLEFT, VMOPCUTRIGHT, VLIMIT, VDELTA, VISOR_STX, VISOR_POSDATA, VISOR_LENDATA, VNRO_FILTROS, DSTDUPFACTOR, DSTDDOWNFACTOR) VALUES (1, CURRENT_DATE, 10, 1, '1', 50, '1', '1', 1, 350, 350, 800, 500, 5, 12, 30, 30, 1, 10, 2, 3, 5, 80, 1.0, 1.0)"
            ];

            await using FbTransaction tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
            foreach (string sql in seedSqls)
            {
                await using var cmd = new FbCommand(sql, conn, tx);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }

        // Usuario administrador por defecto solo si DBUSUARIOS está vacía
        await using var checkUser = new FbCommand("SELECT COUNT(*) FROM DBUSUARIOS", conn);
        if (Convert.ToInt32(await checkUser.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 0)
        {
            string defaultHash = Domain.Security.PasswordHasher.HashPassword("ADMIN");
            await using var cmd = new FbCommand("""
                INSERT INTO DBUSUARIOS (USUARIO, NOMBRE, CARGO, PASS_WORD, ACTIVO, MODIFICAR, AGREGAR, ELIMINAR, IMPRIMIR, CAMBIOPW, INGRESOPJ, INGRESOGT, SUPSISTEMA, PERFIL, JTURNO, USUAING, USUAMOD)
                VALUES (1, 'ADMINISTRADOR', 'ADMIN SISTEMA', @PW, '1', '1', '1', '1', '1', '1', '1', '1', '1', 2, '1', 1, 1)
                """, conn);
            cmd.Parameters.AddWithValue("@PW", defaultHash);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task ExecuteDdlAsync(FbConnection conn, string ddl, CancellationToken ct)
    {
        await using var cmd = new FbCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public static async Task SynchronizeGeneratorAsync(FbConnection conn, FbTransaction tx, string generatorName, int value, CancellationToken ct = default)
    {
        // SET GENERATOR cannot be parameterized in Firebird DDL; validate identifier safely first
        if (!generatorName.All(c => char.IsAsciiLetterUpper(c) || c == '_'))
        {
            throw new ArgumentException("Nombre de generador inválido.", nameof(generatorName));
        }

        string safeSql = new StringBuilder("SET GENERATOR ").Append(generatorName).Append(" TO ").Append(Math.Max(0, value)).ToString();
        await using var cmd = new FbCommand(safeSql, conn, tx);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
