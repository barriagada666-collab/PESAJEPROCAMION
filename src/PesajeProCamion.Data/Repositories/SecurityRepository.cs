using FirebirdSql.Data.FirebirdClient;
using PesajeProCamion.Data.Infrastructure;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;
using static PesajeProCamion.Data.Infrastructure.DatabaseSessionFactory;

namespace PesajeProCamion.Data.Repositories;

/// <summary>Gestión de usuarios, autenticación PBKDF2 con rehash transparente y registros de acceso/auditoría.</summary>
public sealed class SecurityRepository
{
    private readonly DatabaseSessionFactory _db;

    public SecurityRepository(DatabaseSessionFactory db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task<List<Usuario>> GetUsuariosAsync(bool soloActivos = false, CancellationToken ct = default)
    {
        var list = new List<Usuario>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        string sql = soloActivos
            ? "SELECT * FROM DBUSUARIOS WHERE ACTIVO = '1' ORDER BY NOMBRE"
            : "SELECT * FROM DBUSUARIOS ORDER BY NOMBRE";
        await using var cmd = new FbCommand(sql, conn);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(MapUsuario(r));
        }
        return list;
    }

    public async Task<Usuario?> AuthenticateAsync(short usuarioId, string password, int sistemaId, CancellationToken ct = default)
    {
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("SELECT * FROM DBUSUARIOS WHERE USUARIO = @ID", conn);
        AddParam(cmd, "@ID", usuarioId);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        Usuario user = MapUsuario(r);
        if (!user.Activo || !PasswordHasher.VerifyPassword(password, user.PasswordHash, out bool needsRehash))
        {
            return null;
        }

        if (needsRehash)
        {
            string upgradedHash = PasswordHasher.HashPassword(password);
            user.PasswordHash = upgradedHash;
            await _db.ExecuteInTransactionAsync(async (c2, tx2) =>
            {
                await using var upCmd = new FbCommand("UPDATE DBUSUARIOS SET PASS_WORD = @PW WHERE USUARIO = @U", c2, tx2);
                AddParam(upCmd, "@PW", upgradedHash);
                AddParam(upCmd, "@U", user.Id);
                await upCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }

        await RegistrarLogIngresoAsync(user.Id, sistemaId, accionId: 1, turno: 0, ct).ConfigureAwait(false);
        return user;
    }

    public async Task ChangePasswordAsync(short usuarioId, string newPlainPassword, short modificadoPor, CancellationToken ct = default)
    {
        string hash = PasswordHasher.HashPassword(newPlainPassword);
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand("UPDATE DBUSUARIOS SET PASS_WORD = @PW, USUAMOD = @UM WHERE USUARIO = @U", conn, tx);
            AddParam(cmd, "@PW", hash);
            AddParam(cmd, "@UM", modificadoPor);
            AddParam(cmd, "@U", usuarioId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await InsertAuditInternalAsync(conn, tx, modificadoPor, "SEGURIDAD", "DBUSUARIOS", usuarioId.ToString(System.Globalization.CultureInfo.InvariantCulture), "CAMBIO_CLAVE", "Actualización de contraseña", ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<short> SaveUsuarioAsync(Usuario u, string? newPlainPassword, short actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(u);
        return await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            bool esNuevo = u.Id <= 0;
            if (esNuevo)
            {
                await using var nextCmd = new FbCommand("SELECT COALESCE(MAX(USUARIO), 0) + 1 FROM DBUSUARIOS", conn, tx);
                short maxId = Convert.ToInt16(await nextCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                await using var genCmd = new FbCommand("SELECT NEXT VALUE FOR GEN_FLUSUARIO FROM RDB$DATABASE", conn, tx);
                short genId = Convert.ToInt16(await genCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                u.Id = Math.Max(maxId, genId);
                await using var folCmd = new FbCommand("UPDATE DBFOLIOS SET FLUSUARIO = @FU WHERE ID = 1", conn, tx);
                AddParam(folCmd, "@FU", u.Id + 1);
                await folCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            string passwordToStore = !string.IsNullOrWhiteSpace(newPlainPassword)
                ? PasswordHasher.HashPassword(newPlainPassword)
                : (!string.IsNullOrWhiteSpace(u.PasswordHash) ? u.PasswordHash : PasswordHasher.HashPassword("12345"));

            u.PasswordHash = passwordToStore;

            await using var cmd = new FbCommand("""
                UPDATE OR INSERT INTO DBUSUARIOS (
                    USUARIO, NOMBRE, CARGO, PASS_WORD, ACTIVO, MODIFICAR, AGREGAR, ELIMINAR, IMPRIMIR,
                    CAMBIOPW, INGRESOPJ, INGRESOGT, SUPSISTEMA, PERFIL, JTURNO, USUAING, USUAMOD)
                VALUES (
                    @U, @N, @C, @PW, @AC, @MO, @AG, @EL, @IM, @CP, @IP, @IG, @SS, @PE, @JT, @UI, @UM)
                MATCHING (USUARIO)
                """, conn, tx);
            AddParam(cmd, "@U", u.Id);
            AddParam(cmd, "@N", u.Nombre.Trim().ToUpperInvariant());
            AddParam(cmd, "@C", u.Cargo.Trim().ToUpperInvariant());
            AddParam(cmd, "@PW", passwordToStore);
            AddParam(cmd, "@AC", u.Activo ? "1" : "0");
            AddParam(cmd, "@MO", u.PuedeModificar ? "1" : "0");
            AddParam(cmd, "@AG", u.PuedeAgregar ? "1" : "0");
            AddParam(cmd, "@EL", u.PuedeEliminar ? "1" : "0");
            AddParam(cmd, "@IM", u.PuedeImprimir ? "1" : "0");
            AddParam(cmd, "@CP", u.PuedeCambiarPassword ? "1" : "0");
            AddParam(cmd, "@IP", u.IngresoPesaje ? "1" : "0");
            AddParam(cmd, "@IG", u.IngresoGestion ? "1" : "0");
            AddParam(cmd, "@SS", u.SupervisorSistema ? "1" : "0");
            AddParam(cmd, "@PE", (short)u.Perfil);
            AddParam(cmd, "@JT", u.JefeTurno ? "1" : "0");
            AddParam(cmd, "@UI", u.UsuarioIngreso > 0 ? u.UsuarioIngreso : actorId);
            AddParam(cmd, "@UM", actorId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await InsertAuditInternalAsync(conn, tx, actorId, "SEGURIDAD", "DBUSUARIOS", u.Nombre, esNuevo ? "CREAR" : "MODIFICAR", $"Perfil={(short)u.Perfil}, Activo={u.Activo}", ct).ConfigureAwait(false);
            return u.Id;
        }, ct).ConfigureAwait(false);
    }

    public async Task DeleteUsuarioAsync(short usuarioId, short actorId, CancellationToken ct = default)
    {
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand("DELETE FROM DBUSUARIOS WHERE USUARIO = @U", conn, tx);
            AddParam(cmd, "@U", usuarioId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await InsertAuditInternalAsync(conn, tx, actorId, "SEGURIDAD", "DBUSUARIOS", usuarioId.ToString(System.Globalization.CultureInfo.InvariantCulture), "ELIMINAR", "Usuario eliminado", ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task RegistrarLogIngresoAsync(int usuarioId, int sistemaId, int accionId, int turno = 0, CancellationToken ct = default)
    {
        DateTime now = DateTime.Now;
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand("""
                INSERT INTO DBLOGSINGRESO (ID_LOG, USUARIO, SISTEMA, FECHA, HORA, TURNO, ACCION)
                VALUES (NEXT VALUE FOR GEN_LOGINGRESO, @U, @S, @F, @H, @T, @A)
                """, conn, tx);
            AddParam(cmd, "@U", usuarioId);
            AddParam(cmd, "@S", sistemaId);
            AddParam(cmd, "@F", now.Date);
            AddParam(cmd, "@H", now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            AddParam(cmd, "@T", turno);
            AddParam(cmd, "@A", accionId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<List<LogIngreso>> GetLogsIngresoAsync(DateTime desde, DateTime hasta, int? sistemaFiltro = null, CancellationToken ct = default)
    {
        var list = new List<LogIngreso>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        string sql = """
            SELECT L.ID_LOG, L.USUARIO, COALESCE(U.NOMBRE, 'USUARIO #' || CAST(L.USUARIO AS VARCHAR(10))) AS NMUSUARIO,
                   L.SISTEMA, COALESCE(S.DESCRIP, 'SISTEMA ' || CAST(L.SISTEMA AS VARCHAR(10))) AS NMSISTEMA,
                   L.FECHA, L.HORA, L.TURNO, L.ACCION, COALESCE(A.DESCRIP, 'ACCION ' || CAST(L.ACCION AS VARCHAR(10))) AS NMACCION
            FROM DBLOGSINGRESO L
            LEFT JOIN DBUSUARIOS U ON U.USUARIO = L.USUARIO
            LEFT JOIN DBSISTEMAS S ON S.SISTEMA = L.SISTEMA
            LEFT JOIN DBSISTACCION A ON A.ACCION = L.ACCION
            WHERE L.FECHA >= @D AND L.FECHA <= @H
            """;
        if (sistemaFiltro.HasValue)
        {
            sql += " AND L.SISTEMA = @SIS";
        }
        sql += " ORDER BY L.FECHA DESC, L.HORA DESC, L.ID_LOG DESC";

        await using var cmd = new FbCommand(sql, conn);
        AddParam(cmd, "@D", desde.Date);
        AddParam(cmd, "@H", hasta.Date);
        if (sistemaFiltro.HasValue)
        {
            AddParam(cmd, "@SIS", sistemaFiltro.Value);
        }
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new LogIngreso
            {
                IdLog = GetInt(r, "ID_LOG"),
                UsuarioId = GetInt(r, "USUARIO"),
                NombreUsuario = GetStr(r, "NMUSUARIO"),
                Sistema = GetInt(r, "SISTEMA"),
                DescripcionSistema = GetStr(r, "NMSISTEMA"),
                Fecha = GetDate(r, "FECHA") ?? DateTime.MinValue,
                Hora = GetStr(r, "HORA"),
                Turno = GetInt(r, "TURNO"),
                Accion = GetInt(r, "ACCION"),
                DescripcionAccion = GetStr(r, "NMACCION")
            });
        }
        return list;
    }

    public async Task<List<AuditLogEntry>> GetAuditLogsAsync(DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        var list = new List<AuditLogEntry>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("""
            SELECT A.ID_AUDIT, A.FECHA, A.HORA, A.USUARIO, COALESCE(U.NOMBRE, 'USUARIO #' || CAST(A.USUARIO AS VARCHAR(10))) AS NMUSUARIO,
                   A.MODULO, A.ENTIDAD, A.CLAVE, A.ACCION, A.DETALLE
            FROM DBAUDITLOG A
            LEFT JOIN DBUSUARIOS U ON U.USUARIO = A.USUARIO
            WHERE A.FECHA >= @D AND A.FECHA <= @H
            ORDER BY A.ID_AUDIT DESC
            """, conn);
        AddParam(cmd, "@D", desde.Date);
        AddParam(cmd, "@H", hasta.Date);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new AuditLogEntry
            {
                IdAudit = GetInt(r, "ID_AUDIT"),
                Fecha = GetDate(r, "FECHA") ?? DateTime.MinValue,
                Hora = GetStr(r, "HORA"),
                UsuarioId = GetShort(r, "USUARIO"),
                NombreUsuario = GetStr(r, "NMUSUARIO"),
                Modulo = GetStr(r, "MODULO"),
                Entidad = GetStr(r, "ENTIDAD"),
                Clave = GetStr(r, "CLAVE"),
                Accion = GetStr(r, "ACCION"),
                Detalle = GetStr(r, "DETALLE")
            });
        }
        return list;
    }

    public static async Task InsertAuditInternalAsync(
        FbConnection conn,
        FbTransaction tx,
        short usuarioId,
        string modulo,
        string entidad,
        string clave,
        string accion,
        string detalle,
        CancellationToken ct)
    {
        DateTime now = DateTime.Now;
        await using var cmd = new FbCommand("""
            INSERT INTO DBAUDITLOG (ID_AUDIT, FECHA, HORA, USUARIO, MODULO, ENTIDAD, CLAVE, ACCION, DETALLE)
            VALUES (NEXT VALUE FOR GEN_AUDITLOG, @F, @H, @U, @M, @E, @C, @A, @D)
            """, conn, tx);
        AddParam(cmd, "@F", now.Date);
        AddParam(cmd, "@H", now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        AddParam(cmd, "@U", usuarioId);
        AddParam(cmd, "@M", modulo.Length > 25 ? modulo[..25] : modulo);
        AddParam(cmd, "@E", entidad.Length > 30 ? entidad[..30] : entidad);
        AddParam(cmd, "@C", clave.Length > 30 ? clave[..30] : clave);
        AddParam(cmd, "@A", accion.Length > 20 ? accion[..20] : accion);
        AddParam(cmd, "@D", detalle.Length > 240 ? detalle[..240] : detalle);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static Usuario MapUsuario(FbDataReader r) => new()
    {
        Id = GetShort(r, "USUARIO"),
        Nombre = GetStr(r, "NOMBRE"),
        Cargo = GetStr(r, "CARGO"),
        PasswordHash = GetStr(r, "PASS_WORD"),
        Activo = GetFlag(r, "ACTIVO", true),
        PuedeModificar = GetFlag(r, "MODIFICAR"),
        PuedeAgregar = GetFlag(r, "AGREGAR"),
        PuedeEliminar = GetFlag(r, "ELIMINAR"),
        PuedeImprimir = GetFlag(r, "IMPRIMIR", true),
        PuedeCambiarPassword = GetFlag(r, "CAMBIOPW", true),
        IngresoPesaje = GetFlag(r, "INGRESOPJ", true),
        IngresoGestion = GetFlag(r, "INGRESOGT"),
        SupervisorSistema = GetFlag(r, "SUPSISTEMA"),
        Perfil = (PerfilOperacion)GetShort(r, "PERFIL"),
        JefeTurno = GetFlag(r, "JTURNO"),
        UsuarioIngreso = GetShort(r, "USUAING"),
        UsuarioModificacion = GetShort(r, "USUAMOD")
    };
}
