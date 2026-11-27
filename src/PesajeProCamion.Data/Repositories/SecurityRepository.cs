using System.Globalization;
using FirebirdSql.Data.FirebirdClient;
using PesajeProCamion.Data.Infrastructure;
using PesajeProCamion.Domain.Diagnostics;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;
using static PesajeProCamion.Data.Infrastructure.DatabaseSessionFactory;

namespace PesajeProCamion.Data.Repositories;

/// <summary>Gestión de usuarios, autenticación PBKDF2 (compatible con BD remota legacy), y registros de acceso/auditoría.</summary>
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
        => await AuthenticateInternalAsync(usuarioId, null, password, sistemaId, ct).ConfigureAwait(false);

    public async Task<Usuario?> AuthenticateByLoginAsync(short? usuarioId, string? username, string password, int sistemaId, CancellationToken ct = default)
        => await AuthenticateInternalAsync(usuarioId, username, password, sistemaId, ct).ConfigureAwait(false);

    private async Task<Usuario?> AuthenticateInternalAsync(short? usuarioId, string? username, string password, int sistemaId, CancellationToken ct)
    {
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand();
        cmd.Connection = conn;
        if (usuarioId.HasValue && usuarioId.Value > 0)
        {
            cmd.CommandText = "SELECT * FROM DBUSUARIOS WHERE USUARIO = @ID";
            AddParam(cmd, "@ID", usuarioId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(username))
        {
            cmd.CommandText = "SELECT * FROM DBUSUARIOS WHERE UPPER(TRIM(NOMBRE)) = @NOM";
            AddParam(cmd, "@NOM", username.Trim().ToUpperInvariant());
        }
        else
        {
            return null;
        }

        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        Usuario user = MapUsuario(r);
        if (!user.Activo || !PasswordHasher.VerifyPassword(password, user.PasswordHash, out bool needsRehash))
        {
            AppLogger.Warn("Security", $"Intento de autenticación fallido para usuario '{user.Nombre}' (ID={user.Id}).");
            return null;
        }

        if (needsRehash && caps.PasswordMaxLength >= 80)
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
        else if (needsRehash)
        {
            AppLogger.Info("Security", $"Base remota con PASS_WORD de largo {caps.PasswordMaxLength}: se omite actualización a hash largo PBKDF2.");
        }

        try
        {
            await RegistrarLogIngresoAsync(user.Id, sistemaId, accionId: 1, turno: 0, ct).ConfigureAwait(false);
        }
        catch (FbException ex)
        {
            AppLogger.Warn("Security", $"No se pudo registrar log de ingreso para '{user.Nombre}' (se permite acceso): {ex.Message}");
        }
        AppLogger.Info("Security", $"Inicio de sesión: {user.Nombre} (ID={user.Id}, Sistema={sistemaId}).");
        return user;
    }

    public async Task ChangePasswordAsync(short usuarioId, string newPlainPassword, short modificadoPor, CancellationToken ct = default)
    {
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        string toStore = caps.PasswordMaxLength >= 80
            ? PasswordHasher.HashPassword(newPlainPassword)
            : newPlainPassword.Trim().ToUpperInvariant();
        if (toStore.Length > caps.PasswordMaxLength)
        {
            toStore = toStore[..caps.PasswordMaxLength];
        }

        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand("UPDATE DBUSUARIOS SET PASS_WORD = @PW, USUAMOD = @UM WHERE USUARIO = @U", conn, tx);
            AddParam(cmd, "@PW", toStore);
            AddParam(cmd, "@UM", modificadoPor);
            AddParam(cmd, "@U", usuarioId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await InsertAuditInternalAsync(conn, tx, caps, modificadoPor, "SEGURIDAD", "DBUSUARIOS", usuarioId.ToString(CultureInfo.InvariantCulture), "CAMBIO_CLAVE", "Actualización de contraseña", ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<short> SaveUsuarioAsync(Usuario u, string? newPlainPassword, short actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(u);
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        return await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            bool esNuevo = u.Id <= 0;
            string normName = FitLength(u.Nombre.ToUpperInvariant(), caps.UsernameMaxLength);
            string normCargo = FitLength(u.Cargo.ToUpperInvariant(), caps.UsernameMaxLength);

            await using var dupCmd = new FbCommand("SELECT COUNT(*) FROM DBUSUARIOS WHERE UPPER(TRIM(NOMBRE)) = @N AND USUARIO <> @U", conn, tx);
            AddParam(dupCmd, "@N", normName);
            AddParam(dupCmd, "@U", u.Id);
            int dupCount = Convert.ToInt32(await dupCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (dupCount > 0)
            {
                throw new InvalidOperationException($"Ya existe otro usuario registrado con el nombre '{normName}'.");
            }

            if (esNuevo)
            {
                await using var nextCmd = new FbCommand("SELECT COALESCE(MAX(USUARIO), 0) + 1 FROM DBUSUARIOS", conn, tx);
                short maxId = Convert.ToInt16(await nextCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
                short genId = maxId;
                if (caps.HasGenUsuario)
                {
                    await using var genCmd = new FbCommand("SELECT NEXT VALUE FOR GEN_FLUSUARIO FROM RDB$DATABASE", conn, tx);
                    genId = Convert.ToInt16(await genCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
                }
                u.Id = Math.Max(maxId, genId);

                string folSql = caps.HasFoliosId
                    ? "UPDATE DBFOLIOS SET FLUSUARIO = @FU WHERE ID = 1"
                    : "UPDATE DBFOLIOS SET FLUSUARIO = @FU";
                await using var folCmd = new FbCommand(folSql, conn, tx);
                AddParam(folCmd, "@FU", u.Id + 1);
                await folCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            string passwordToStore;
            if (!string.IsNullOrWhiteSpace(newPlainPassword))
            {
                passwordToStore = caps.PasswordMaxLength >= 80
                    ? PasswordHasher.HashPassword(newPlainPassword)
                    : newPlainPassword.Trim().ToUpperInvariant();
            }
            else if (!string.IsNullOrWhiteSpace(u.PasswordHash))
            {
                passwordToStore = u.PasswordHash;
            }
            else
            {
                passwordToStore = caps.PasswordMaxLength >= 80
                    ? PasswordHasher.HashPassword("12345")
                    : "12345";
            }

            if (passwordToStore.Length > caps.PasswordMaxLength)
            {
                passwordToStore = passwordToStore[..caps.PasswordMaxLength];
            }

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
            AddParam(cmd, "@N", normName);
            AddParam(cmd, "@C", normCargo);
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

            await InsertAuditInternalAsync(conn, tx, caps, actorId, "SEGURIDAD", "DBUSUARIOS", u.Nombre, esNuevo ? "CREAR" : "MODIFICAR", $"Perfil={(short)u.Perfil}, Activo={u.Activo}", ct).ConfigureAwait(false);
            return u.Id;
        }, ct).ConfigureAwait(false);
    }

    public async Task SetUsuarioActivoAsync(short usuarioId, bool activo, short actorId, CancellationToken ct = default)
    {
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            if (!activo)
            {
                await EnsureAtLeastOneOtherActiveAdminAsync(conn, tx, usuarioId, ct).ConfigureAwait(false);
            }
            await using var cmd = new FbCommand("UPDATE DBUSUARIOS SET ACTIVO = @AC, USUAMOD = @UM WHERE USUARIO = @U", conn, tx);
            AddParam(cmd, "@AC", activo ? "1" : "0");
            AddParam(cmd, "@UM", actorId);
            AddParam(cmd, "@U", usuarioId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await InsertAuditInternalAsync(conn, tx, caps, actorId, "SEGURIDAD", "DBUSUARIOS", usuarioId.ToString(CultureInfo.InvariantCulture), activo ? "ACTIVAR" : "DESACTIVAR", $"Activo={activo}", ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task ResetPasswordAsync(short usuarioId, string newPlainPassword, short adminActorId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPlainPassword);
        await ChangePasswordAsync(usuarioId, newPlainPassword, adminActorId, ct).ConfigureAwait(false);
    }

    public async Task DeleteUsuarioAsync(short usuarioId, short actorId, CancellationToken ct = default)
    {
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await EnsureAtLeastOneOtherActiveAdminAsync(conn, tx, usuarioId, ct).ConfigureAwait(false);
            await using var cmd = new FbCommand("DELETE FROM DBUSUARIOS WHERE USUARIO = @U", conn, tx);
            AddParam(cmd, "@U", usuarioId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await InsertAuditInternalAsync(conn, tx, caps, actorId, "SEGURIDAD", "DBUSUARIOS", usuarioId.ToString(CultureInfo.InvariantCulture), "ELIMINAR", "Usuario eliminado", ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private static async Task EnsureAtLeastOneOtherActiveAdminAsync(FbConnection conn, FbTransaction tx, short excludedUserId, CancellationToken ct)
    {
        await using var chkTarget = new FbCommand("SELECT PERFIL, ACTIVO, SUPSISTEMA FROM DBUSUARIOS WHERE USUARIO = @U", conn, tx);
        AddParam(chkTarget, "@U", excludedUserId);
        await using FbDataReader r = await chkTarget.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return;
        }
        bool isTargetAdmin = GetShort(r, "PERFIL") == (short)PerfilOperacion.Administracion || GetFlag(r, "SUPSISTEMA");
        bool isTargetActive = GetFlag(r, "ACTIVO", true);
        await r.CloseAsync().ConfigureAwait(false);

        if (!isTargetAdmin || !isTargetActive)
        {
            return;
        }

        await using var cntCmd = new FbCommand("SELECT COUNT(*) FROM DBUSUARIOS WHERE ACTIVO = '1' AND (PERFIL = 2 OR SUPSISTEMA = '1') AND USUARIO <> @U", conn, tx);
        AddParam(cntCmd, "@U", excludedUserId);
        int others = Convert.ToInt32(await cntCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        if (others == 0)
        {
            throw new InvalidOperationException("No se puede eliminar ni desactivar al último Administrador activo del sistema.");
        }
    }

    public async Task RegistrarLogIngresoAsync(int usuarioId, int sistemaId, int accionId, int turno = 0, CancellationToken ct = default)
    {
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        DateTime now = DateTime.Now;
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            if (caps.HasLogIngresoId)
            {
                int nextLogId;
                if (caps.HasGenLogIngreso)
                {
                    await using var gen = new FbCommand("SELECT NEXT VALUE FOR GEN_LOGINGRESO FROM RDB$DATABASE", conn, tx);
                    nextLogId = Convert.ToInt32(await gen.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
                }
                else
                {
                    await using var maxCmd = new FbCommand("SELECT COALESCE(MAX(ID_LOG), 0) + 1 FROM DBLOGSINGRESO", conn, tx);
                    nextLogId = Convert.ToInt32(await maxCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
                }

                await using var cmd = new FbCommand("""
                    INSERT INTO DBLOGSINGRESO (ID_LOG, USUARIO, SISTEMA, FECHA, HORA, TURNO, ACCION)
                    VALUES (@ID, @U, @S, @F, @H, @T, @A)
                    """, conn, tx);
                AddParam(cmd, "@ID", nextLogId);
                AddParam(cmd, "@U", usuarioId);
                AddParam(cmd, "@S", sistemaId);
                AddParam(cmd, "@F", now.Date);
                AddParam(cmd, "@H", now.ToString("HH:mm", CultureInfo.InvariantCulture));
                AddParam(cmd, "@T", turno);
                AddParam(cmd, "@A", accionId);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return;
            }

            await using var legacyCmd = new FbCommand("""
                INSERT INTO DBLOGSINGRESO (USUARIO, SISTEMA, FECHA, HORA, TURNO, ACCION)
                VALUES (@U, @S, @F, @H, @T, @A)
                """, conn, tx);
            AddParam(legacyCmd, "@U", usuarioId);
            AddParam(legacyCmd, "@S", sistemaId);
            AddParam(legacyCmd, "@F", now.Date);
            AddParam(legacyCmd, "@H", now.ToString("HH:mm", CultureInfo.InvariantCulture));
            AddParam(legacyCmd, "@T", turno);
            AddParam(legacyCmd, "@A", accionId);
            await legacyCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<List<LogIngreso>> GetLogsIngresoAsync(DateTime desde, DateTime hasta, int? sistemaFiltro = null, CancellationToken ct = default)
    {
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        var list = new List<LogIngreso>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        string idSelect = caps.HasLogIngresoId ? "L.ID_LOG" : "0 AS ID_LOG";
        string orderBy = caps.HasLogIngresoId ? "L.FECHA DESC, L.HORA DESC, L.ID_LOG DESC" : "L.FECHA DESC, L.HORA DESC";
        string sql = $"""
            SELECT {idSelect}, L.USUARIO, COALESCE(U.NOMBRE, 'USUARIO #' || CAST(L.USUARIO AS VARCHAR(10))) AS NMUSUARIO,
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
        sql += $" ORDER BY {orderBy}";

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
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        if (!caps.HasAuditLog)
        {
            return [];
        }

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
        DatabaseCapabilities caps,
        short usuarioId,
        string modulo,
        string entidad,
        string clave,
        string accion,
        string detalle,
        CancellationToken ct)
    {
        AppLogger.Info("AUDIT", $"[{modulo}/{entidad}] {accion} ({clave}) usr={usuarioId}: {detalle}");
        if (!caps.HasAuditLog)
        {
            return;
        }

        DateTime now = DateTime.Now;
        await using var cmd = new FbCommand("""
            INSERT INTO DBAUDITLOG (ID_AUDIT, FECHA, HORA, USUARIO, MODULO, ENTIDAD, CLAVE, ACCION, DETALLE)
            VALUES (NEXT VALUE FOR GEN_AUDITLOG, @F, @H, @U, @M, @E, @C, @A, @D)
            """, conn, tx);
        AddParam(cmd, "@F", now.Date);
        AddParam(cmd, "@H", now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
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
