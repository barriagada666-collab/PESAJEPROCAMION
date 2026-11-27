using System.Globalization;
using FirebirdSql.Data.FirebirdClient;
using PesajeProCamion.Data.Infrastructure;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Services;
using PesajeProCamion.Domain.Validation;
using static PesajeProCamion.Data.Infrastructure.DatabaseSessionFactory;

namespace PesajeProCamion.Data.Repositories;

public sealed record ResumenTurno(
    int Turno,
    string NombreTurno,
    int CantidadPesajes,
    int TotalBrutoKg,
    int TotalTaraKg,
    int TotalNetoKg);

public sealed record TaraCamionReporte(
    string Patente,
    string Acoplado,
    string NombreTransportista,
    string NombreChofer,
    int TaraKg,
    DateTime? FechaTara,
    string HoraTara,
    bool TaraRegistrada,
    DateTime? FechaTaraRegistrada,
    short DiasValidez);

/// <summary>Repositorio transaccional de tránsito, pesajes históricos, anulación, informes y auditoría.</summary>
public sealed class WeighingRepository
{
    private readonly DatabaseSessionFactory _db;

    public WeighingRepository(DatabaseSessionFactory db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task<List<RegistroTransito>> GetTransitoAsync(CancellationToken ct = default)
    {
        var list = new List<RegistroTransito>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("""
            SELECT T.*, COALESCE(P.DESCRIP, T.PRODUCTO) AS NMPRODUCTO, COALESCE(C.NOMBRE, T.CLIENTE) AS NMCLIENTE
            FROM DBTRANSITO T
            LEFT JOIN DBPRODUCTOS P ON TRIM(P.PRODUCTO) = TRIM(T.PRODUCTO)
            LEFT JOIN DBCLIENTES C ON TRIM(C.RUT) = TRIM(T.CLIENTE)
            ORDER BY T.FECHAING DESC, T.HORAING DESC, T.FLPESAJE DESC
            """, conn);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new RegistroTransito
            {
                FolioPesaje = GetInt(r, "FLPESAJE"),
                Patente = GetStr(r, "PATENTE"),
                Acoplado = GetStr(r, "ACOPLADO"),
                Producto = GetStr(r, "PRODUCTO"),
                NombreProducto = GetStr(r, "NMPRODUCTO"),
                Cliente = GetStr(r, "CLIENTE"),
                NombreCliente = GetStr(r, "NMCLIENTE"),
                Origen = GetStr(r, "ORIGEN"),
                Destino = GetStr(r, "DESTINO"),
                GuiaDespacho = GetStr(r, "GDESPACHO"),
                NetoInformado = GetInt(r, "NETOINF"),
                FechaIngreso = GetDate(r, "FECHAING") ?? DateTime.Today,
                HoraIngreso = GetStr(r, "HORAING"),
                Bruto = GetInt(r, "BRUTO"),
                Tara = GetInt(r, "TARA"),
                TipoMop = GetShort(r, "TMOP"),
                Largo = GetShort(r, "LARGO"),
                MaxBruto = GetInt(r, "MAXBRUTO"),
                Chofer = GetStr(r, "CHOFER"),
                Observacion1 = GetStr(r, "OBSERVA1"),
                Transportista = GetStr(r, "TRANSP"),
                TipoPesaje = (TipoPesaje)GetShort(r, "TPESAJE", 1),
                SavedToServer = GetNullableStr(r, "SAVEDTOSERVER"),
                UsuarioIngreso = GetShort(r, "USUAING"),
                Traspaso = GetNullableStr(r, "TRASPASO")
            });
        }
        return list;
    }

    public async Task<RegistroTransito?> GetTransitoPorPatenteAsync(string patente, CancellationToken ct = default)
    {
        string pat = ChileanValidators.NormalizarPatente(patente);
        if (pat.Length == 0)
        {
            return null;
        }
        List<RegistroTransito> all = await GetTransitoAsync(ct).ConfigureAwait(false);
        return all.FirstOrDefault(t => string.Equals(t.Patente, pat, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<int> RegistrarIngresoTransitoAsync(RegistroTransito tr, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tr);
        string pat = ChileanValidators.NormalizarPatente(tr.Patente);
        return await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            if (tr.FolioPesaje <= 0)
            {
                tr.FolioPesaje = await AllocateNextPesajeFolioAsync(conn, tx, ct).ConfigureAwait(false);
            }

            await using var cmd = new FbCommand("""
                UPDATE OR INSERT INTO DBTRANSITO (
                    FLPESAJE, PATENTE, ACOPLADO, PRODUCTO, CLIENTE, ORIGEN, DESTINO, GDESPACHO,
                    NETOINF, FECHAING, HORAING, BRUTO, TARA, TMOP, LARGO, MAXBRUTO, CHOFER,
                    OBSERVA1, TRANSP, TPESAJE, SAVEDTOSERVER, USUAING, TRASPASO)
                VALUES (
                    @FP, @PAT, @ACO, @PR, @CL, @OR, @DE, @GD,
                    @NI, @FI, @HI, @BR, @TA, @TM, @LA, @MB, @CH,
                    @OB1, @TR, @TP, @SV, @UI, @TRS)
                MATCHING (FLPESAJE)
                """, conn, tx);
            AddParam(cmd, "@FP", tr.FolioPesaje);
            AddParam(cmd, "@PAT", pat);
            AddParam(cmd, "@ACO", ChileanValidators.NormalizarPatente(tr.Acoplado));
            AddParam(cmd, "@PR", tr.Producto.Trim());
            AddParam(cmd, "@CL", tr.Cliente.Trim());
            AddParam(cmd, "@OR", tr.Origen.Trim());
            AddParam(cmd, "@DE", tr.Destino.Trim());
            AddParam(cmd, "@GD", tr.GuiaDespacho.Trim());
            AddParam(cmd, "@NI", tr.NetoInformado);
            AddParam(cmd, "@FI", tr.FechaIngreso.Date);
            AddParam(cmd, "@HI", tr.HoraIngreso);
            AddParam(cmd, "@BR", tr.Bruto);
            AddParam(cmd, "@TA", tr.Tara);
            AddParam(cmd, "@TM", tr.TipoMop);
            AddParam(cmd, "@LA", tr.Largo);
            AddParam(cmd, "@MB", tr.MaxBruto);
            AddParam(cmd, "@CH", tr.Chofer.Trim());
            AddParam(cmd, "@OB1", tr.Observacion1.Trim());
            AddParam(cmd, "@TR", tr.Transportista.Trim());
            AddParam(cmd, "@TP", (short)tr.TipoPesaje);
            AddParam(cmd, "@SV", tr.SavedToServer);
            AddParam(cmd, "@UI", tr.UsuarioIngreso);
            AddParam(cmd, "@TRS", tr.Traspaso);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await SecurityRepository.InsertAuditInternalAsync(conn, tx, tr.UsuarioIngreso, "PESAJE", "DBTRANSITO", tr.FolioPesaje.ToString(CultureInfo.InvariantCulture), "INGRESO", $"Patente={pat}, Bruto={tr.Bruto}, Tara={tr.Tara}", ct).ConfigureAwait(false);
            return tr.FolioPesaje;
        }, ct).ConfigureAwait(false);
    }

    public async Task<int> CompletarPesajeAsync(Pesaje p, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(p);
        string pat = ChileanValidators.NormalizarPatente(p.Patente);
        p.Patente = pat;
        p.Neto = WeighingCalculator.CalcularNeto(p.Bruto, p.Tara);

        return await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            if (p.FolioPesaje <= 0)
            {
                p.FolioPesaje = await AllocateNextPesajeFolioAsync(conn, tx, ct).ConfigureAwait(false);
            }

            await using var insCmd = new FbCommand("""
                INSERT INTO DBPESAJES (
                    FLPESAJE, PATENTE, ACOPLADO, FLPESAJEMOP, PRODUCTO, CLIENTE, ORIGEN, DESTINO, GDESPACHO,
                    NETOINF, FECHAING, HORAING, FECHASAL, HORASAL, FECHAMOD, HORAMOD,
                    BRUTO, TARA, NETO, TMOP, LARGO, MAXBRUTO, ESTADOMOP, CHOFER, TRASPASO,
                    OBSERVA1, OBSERVA2, TRANSP, TARAREG, TPESAJE, SAVEDTOSERVER,
                    USUAING, USUASAL, USUAMOD, USUADEL, ANULADO)
                VALUES (
                    @FP, @PAT, @ACO, @FM, @PR, @CL, @OR, @DE, @GD,
                    @NI, @FI, @HI, @FS, @HS, @FMOD, @HMOD,
                    @BR, @TA, @NE, @TM, @LA, @MB, @EM, @CH, @TRS,
                    @OB1, @OB2, @TR, @TRG, @TP, @SV,
                    @UI, @US, @UM, @UD, @AN)
                """, conn, tx);
            AddParam(insCmd, "@FP", p.FolioPesaje);
            AddParam(insCmd, "@PAT", pat);
            AddParam(insCmd, "@ACO", ChileanValidators.NormalizarPatente(p.Acoplado));
            AddParam(insCmd, "@FM", p.FolioPesajeMop);
            AddParam(insCmd, "@PR", p.Producto.Trim());
            AddParam(insCmd, "@CL", p.Cliente.Trim());
            AddParam(insCmd, "@OR", p.Origen.Trim());
            AddParam(insCmd, "@DE", p.Destino.Trim());
            AddParam(insCmd, "@GD", p.GuiaDespacho.Trim());
            AddParam(insCmd, "@NI", p.NetoInformado);
            AddParam(insCmd, "@FI", p.FechaIngreso.Date);
            AddParam(insCmd, "@HI", p.HoraIngreso);
            AddParam(insCmd, "@FS", p.FechaSalida.Date);
            AddParam(insCmd, "@HS", p.HoraSalida);
            AddParam(insCmd, "@FMOD", p.FechaModificacion);
            AddParam(insCmd, "@HMOD", p.HoraModificacion);
            AddParam(insCmd, "@BR", p.Bruto);
            AddParam(insCmd, "@TA", p.Tara);
            AddParam(insCmd, "@NE", p.Neto);
            AddParam(insCmd, "@TM", p.TipoMop);
            AddParam(insCmd, "@LA", p.Largo);
            AddParam(insCmd, "@MB", p.MaxBruto);
            AddParam(insCmd, "@EM", ((short)p.EstadoMop).ToString(CultureInfo.InvariantCulture));
            AddParam(insCmd, "@CH", p.Chofer.Trim());
            AddParam(insCmd, "@TRS", p.Traspaso);
            AddParam(insCmd, "@OB1", p.Observacion1.Trim());
            AddParam(insCmd, "@OB2", p.Observacion2.Trim());
            AddParam(insCmd, "@TR", p.Transportista.Trim());
            AddParam(insCmd, "@TRG", p.TaraRegistrada ? "1" : "0");
            AddParam(insCmd, "@TP", (short)p.TipoPesaje);
            AddParam(insCmd, "@SV", p.SavedToServer);
            AddParam(insCmd, "@UI", p.UsuarioIngreso);
            AddParam(insCmd, "@US", p.UsuarioSalida);
            AddParam(insCmd, "@UM", p.UsuarioModificacion);
            AddParam(insCmd, "@UD", p.UsuarioEliminacion);
            AddParam(insCmd, "@AN", p.Anulado ? "1" : "0");
            await insCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await using var delTr = new FbCommand("DELETE FROM DBTRANSITO WHERE FLPESAJE = @FP", conn, tx);
            AddParam(delTr, "@FP", p.FolioPesaje);
            await delTr.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            // Actualizar tara histórica del camión en catálogo si hay tara válida
            int taraCatalogo = Math.Min(p.Bruto, p.Tara);
            if (taraCatalogo > 0 && !p.TaraRegistrada)
            {
                await using var camCmd = new FbCommand("""
                    UPDATE OR INSERT INTO DBCAMIONES (PATENTE, ACOPLADO, TRANSP, CHOFER, TMOP, LARGO, TARA, FTARA, HTARA, USUAING, USUAMOD)
                    VALUES (@PAT, @ACO, @TR, @CH, @TM, @LA, @TA, @FT, @HT, @UI, @US)
                    MATCHING (PATENTE)
                    """, conn, tx);
                AddParam(camCmd, "@PAT", pat);
                AddParam(camCmd, "@ACO", ChileanValidators.NormalizarPatente(p.Acoplado));
                AddParam(camCmd, "@TR", p.Transportista.Trim());
                AddParam(camCmd, "@CH", p.Chofer.Trim());
                AddParam(camCmd, "@TM", p.TipoMop);
                AddParam(camCmd, "@LA", p.Largo);
                AddParam(camCmd, "@TA", taraCatalogo);
                AddParam(camCmd, "@FT", p.FechaSalida.Date);
                AddParam(camCmd, "@HT", p.HoraSalida);
                AddParam(camCmd, "@UI", p.UsuarioIngreso);
                AddParam(camCmd, "@US", p.UsuarioSalida);
                await camCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await SecurityRepository.InsertAuditInternalAsync(conn, tx, p.UsuarioSalida, "PESAJE", "DBPESAJES", p.FolioPesaje.ToString(CultureInfo.InvariantCulture), "SALIDA", $"Patente={pat}, Bruto={p.Bruto}, Tara={p.Tara}, Neto={p.Neto}", ct).ConfigureAwait(false);
            return p.FolioPesaje;
        }, ct).ConfigureAwait(false);
    }

    public async Task EliminarTransitoAsync(int folioPesaje, short usuarioId, string motivo, CancellationToken ct = default)
    {
        DateTime now = DateTime.Now;
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var archCmd = new FbCommand("""
                INSERT INTO DBTRAN_ANUL (
                    FLPESAJE, PATENTE, ACOPLADO, PRODUCTO, CLIENTE, DESTINO, GDESPACHO, NETOINF,
                    FECHAING, HORAING, BRUTO, TARA, TMOP, LARGO, MAXBRUTO, OBSERVA, CHOFER, TRANSP,
                    USUAING, FECHAANUL, HORAANUL, USUAANUL, MOTIVO)
                SELECT FLPESAJE, PATENTE, ACOPLADO, PRODUCTO, CLIENTE, DESTINO, GDESPACHO, NETOINF,
                       FECHAING, HORAING, BRUTO, TARA, TMOP, LARGO, MAXBRUTO, OBSERVA1, CHOFER, TRANSP,
                       USUAING, @FA, @HA, @UA, @MO
                FROM DBTRANSITO WHERE FLPESAJE = @FP
                """, conn, tx);
            AddParam(archCmd, "@FA", now.Date);
            AddParam(archCmd, "@HA", now.ToString("HH:mm", CultureInfo.InvariantCulture));
            AddParam(archCmd, "@UA", usuarioId);
            AddParam(archCmd, "@MO", motivo.Length > 120 ? motivo[..120] : motivo);
            AddParam(archCmd, "@FP", folioPesaje);
            await archCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await using var delCmd = new FbCommand("DELETE FROM DBTRANSITO WHERE FLPESAJE = @FP", conn, tx);
            AddParam(delCmd, "@FP", folioPesaje);
            await delCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await SecurityRepository.InsertAuditInternalAsync(conn, tx, usuarioId, "TRANSITO", "DBTRANSITO", folioPesaje.ToString(CultureInfo.InvariantCulture), "ELIMINAR", motivo, ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<List<RegistroTransitoAnulado>> GetTransitoAnuladoAsync(DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        var list = new List<RegistroTransitoAnulado>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("""
            SELECT T.*, COALESCE(U.NOMBRE, '') AS NMUSUAANUL
            FROM DBTRAN_ANUL T
            LEFT JOIN DBUSUARIOS U ON U.USUARIO = T.USUAANUL
            WHERE T.FECHAANUL >= @D AND T.FECHAANUL <= @H
            ORDER BY T.FECHAANUL DESC, T.FLPESAJE DESC
            """, conn);
        AddParam(cmd, "@D", desde.Date);
        AddParam(cmd, "@H", hasta.Date);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new RegistroTransitoAnulado
            {
                FolioPesaje = GetInt(r, "FLPESAJE"),
                Patente = GetStr(r, "PATENTE"),
                Producto = GetStr(r, "PRODUCTO"),
                Cliente = GetStr(r, "CLIENTE"),
                FechaIngreso = GetDate(r, "FECHAING") ?? DateTime.MinValue,
                HoraIngreso = GetStr(r, "HORAING"),
                Bruto = GetInt(r, "BRUTO"),
                Tara = GetInt(r, "TARA"),
                Observacion = GetStr(r, "OBSERVA"),
                FechaAnulacion = GetDate(r, "FECHAANUL") ?? DateTime.MinValue,
                HoraAnulacion = GetStr(r, "HORAANUL"),
                NombreUsuarioAnulacion = GetStr(r, "NMUSUAANUL"),
                Motivo = GetStr(r, "MOTIVO")
            });
        }
        return list;
    }

    public async Task<List<Pesaje>> QueryPesajesAsync(
        DateTime? desde = null,
        DateTime? hasta = null,
        TipoPesaje? tipoPesaje = null,
        string? clienteRut = null,
        string? patente = null,
        string? productoCod = null,
        string? transpRut = null,
        int? folioExacto = null,
        bool incluirAnulados = true,
        int maxRows = 5000,
        CancellationToken ct = default)
    {
        var list = new List<Pesaje>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        var sql = $"""
            SELECT FIRST {Math.Clamp(maxRows, 1, 50000)}
                P.*,
                COALESCE(PR.DESCRIP, P.PRODUCTO) AS NMPRODUCTO,
                COALESCE(CL.NOMBRE, P.CLIENTE) AS NMCLIENTE,
                COALESCE(OR1.DESCRIP, P.ORIGEN) AS NMORIGEN,
                COALESCE(DE.DESCRIP, P.DESTINO) AS NMDESTINO,
                COALESCE(TR.NOMBRE, P.TRANSP) AS NMTRANSP,
                COALESCE(CH.NOMBRE, P.CHOFER) AS NMCHOFER,
                COALESCE(U1.NOMBRE, '') AS NMUSUAING,
                COALESCE(U2.NOMBRE, '') AS NMUSUASAL,
                COALESCE(U3.NOMBRE, '') AS NMUSUAMOD,
                COALESCE(U4.NOMBRE, '') AS NMUSUADEL
            FROM DBPESAJES P
            LEFT JOIN DBPRODUCTOS PR ON TRIM(PR.PRODUCTO) = TRIM(P.PRODUCTO)
            LEFT JOIN DBCLIENTES CL ON TRIM(CL.RUT) = TRIM(P.CLIENTE)
            LEFT JOIN DBORIGEN OR1 ON TRIM(OR1.ORIGEN) = TRIM(P.ORIGEN)
            LEFT JOIN DBDESTINOS DE ON TRIM(DE.DESTINO) = TRIM(P.DESTINO)
            LEFT JOIN DBTRANSP TR ON TRIM(TR.TRANSP) = TRIM(P.TRANSP)
            LEFT JOIN DBCHOFERES CH ON TRIM(CH.CHOFER) = TRIM(P.CHOFER)
            LEFT JOIN DBUSUARIOS U1 ON U1.USUARIO = P.USUAING
            LEFT JOIN DBUSUARIOS U2 ON U2.USUARIO = P.USUASAL
            LEFT JOIN DBUSUARIOS U3 ON U3.USUARIO = P.USUAMOD
            LEFT JOIN DBUSUARIOS U4 ON U4.USUARIO = P.USUADEL
            WHERE 1 = 1
            """;

        using var cmd = new FbCommand { Connection = conn };
        if (folioExacto.HasValue)
        {
            sql += " AND P.FLPESAJE = @FP";
            AddParam(cmd, "@FP", folioExacto.Value);
        }
        else
        {
            if (desde.HasValue)
            {
                sql += " AND COALESCE(P.FECHASAL, P.FECHAING) >= @D";
                AddParam(cmd, "@D", desde.Value.Date);
            }
            if (hasta.HasValue)
            {
                sql += " AND COALESCE(P.FECHASAL, P.FECHAING) <= @H";
                AddParam(cmd, "@H", hasta.Value.Date);
            }
            if (tipoPesaje.HasValue)
            {
                sql += " AND P.TPESAJE = @TP";
                AddParam(cmd, "@TP", (short)tipoPesaje.Value);
            }
            if (!string.IsNullOrWhiteSpace(clienteRut))
            {
                sql += " AND TRIM(P.CLIENTE) = @CL";
                AddParam(cmd, "@CL", clienteRut.Trim());
            }
            if (!string.IsNullOrWhiteSpace(patente))
            {
                sql += " AND UPPER(TRIM(P.PATENTE)) = @PAT";
                AddParam(cmd, "@PAT", ChileanValidators.NormalizarPatente(patente));
            }
            if (!string.IsNullOrWhiteSpace(productoCod))
            {
                sql += " AND TRIM(P.PRODUCTO) = @PR";
                AddParam(cmd, "@PR", productoCod.Trim());
            }
            if (!string.IsNullOrWhiteSpace(transpRut))
            {
                sql += " AND TRIM(P.TRANSP) = @TR";
                AddParam(cmd, "@TR", transpRut.Trim());
            }
            if (!incluirAnulados)
            {
                sql += " AND P.ANULADO <> '1'";
            }
        }

        sql += " ORDER BY P.FLPESAJE DESC";
        cmd.CommandText = sql;

        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(MapPesaje(r));
        }
        return list;
    }

    public Task<Pesaje?> GetPesajePorFolioAsync(int folio, CancellationToken ct = default)
        => QueryPesajesAsync(folioExacto: folio, maxRows: 1, ct: ct).ContinueWith(t => t.Result.FirstOrDefault(), ct);

    public async Task ModificarPesajeAsync(Pesaje p, short usuarioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(p);
        DateTime now = DateTime.Now;
        p.Neto = p.Anulado ? 0 : WeighingCalculator.CalcularNeto(p.Bruto, p.Tara);
        p.FechaModificacion = now.Date;
        p.HoraModificacion = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        p.UsuarioModificacion = usuarioId;

        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand("""
                UPDATE DBPESAJES SET
                    PATENTE = @PAT, ACOPLADO = @ACO, PRODUCTO = @PR, CLIENTE = @CL,
                    ORIGEN = @OR, DESTINO = @DE, GDESPACHO = @GD, NETOINF = @NI,
                    FECHAMOD = @FMOD, HORAMOD = @HMOD, BRUTO = @BR, TARA = @TA, NETO = @NE,
                    CHOFER = @CH, OBSERVA1 = @OB1, OBSERVA2 = @OB2, TRANSP = @TR,
                    TPESAJE = @TP, USUAMOD = @UM
                WHERE FLPESAJE = @FP
                """, conn, tx);
            AddParam(cmd, "@PAT", ChileanValidators.NormalizarPatente(p.Patente));
            AddParam(cmd, "@ACO", ChileanValidators.NormalizarPatente(p.Acoplado));
            AddParam(cmd, "@PR", p.Producto.Trim());
            AddParam(cmd, "@CL", p.Cliente.Trim());
            AddParam(cmd, "@OR", p.Origen.Trim());
            AddParam(cmd, "@DE", p.Destino.Trim());
            AddParam(cmd, "@GD", p.GuiaDespacho.Trim());
            AddParam(cmd, "@NI", p.NetoInformado);
            AddParam(cmd, "@FMOD", p.FechaModificacion);
            AddParam(cmd, "@HMOD", p.HoraModificacion);
            AddParam(cmd, "@BR", p.Bruto);
            AddParam(cmd, "@TA", p.Tara);
            AddParam(cmd, "@NE", p.Neto);
            AddParam(cmd, "@CH", p.Chofer.Trim());
            AddParam(cmd, "@OB1", p.Observacion1.Trim());
            AddParam(cmd, "@OB2", p.Observacion2.Trim());
            AddParam(cmd, "@TR", p.Transportista.Trim());
            AddParam(cmd, "@TP", (short)p.TipoPesaje);
            AddParam(cmd, "@UM", usuarioId);
            AddParam(cmd, "@FP", p.FolioPesaje);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await SecurityRepository.InsertAuditInternalAsync(conn, tx, usuarioId, "GESTION", "DBPESAJES", p.FolioPesaje.ToString(CultureInfo.InvariantCulture), "MODIFICAR", $"Patente={p.Patente}, Bruto={p.Bruto}, Tara={p.Tara}, Neto={p.Neto}", ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Anula o reactiva un pesaje específico por su folio (corrige bug legacy de PA_DEL_PESAJES sin filtro).</summary>
    public async Task ToggleAnularPesajeAsync(int folioPesaje, short usuarioId, CancellationToken ct = default)
    {
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var sel = new FbCommand("SELECT ANULADO, BRUTO, TARA FROM DBPESAJES WHERE FLPESAJE = @FP", conn, tx);
            AddParam(sel, "@FP", folioPesaje);
            await using FbDataReader r = await sel.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await r.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"No existe el folio de pesaje {folioPesaje}.");
            }
            bool anuladoActual = GetFlag(r, "ANULADO");
            int bruto = GetInt(r, "BRUTO");
            int tara = GetInt(r, "TARA");
            await r.CloseAsync().ConfigureAwait(false);

            bool nuevoAnulado = !anuladoActual;
            int nuevoNeto = nuevoAnulado ? 0 : WeighingCalculator.CalcularNeto(bruto, tara);

            await using var upd = new FbCommand("""
                UPDATE DBPESAJES
                SET ANULADO = @AN, NETO = @NE, USUADEL = @UD, FECHAMOD = @FM, HORAMOD = @HM, USUAMOD = @UD
                WHERE FLPESAJE = @FP
                """, conn, tx);
            DateTime now = DateTime.Now;
            AddParam(upd, "@AN", nuevoAnulado ? "1" : "0");
            AddParam(upd, "@NE", nuevoNeto);
            AddParam(upd, "@UD", nuevoAnulado ? usuarioId : (short)0);
            AddParam(upd, "@FM", now.Date);
            AddParam(upd, "@HM", now.ToString("HH:mm", CultureInfo.InvariantCulture));
            AddParam(upd, "@FP", folioPesaje);
            await upd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await SecurityRepository.InsertAuditInternalAsync(conn, tx, usuarioId, "GESTION", "DBPESAJES", folioPesaje.ToString(CultureInfo.InvariantCulture), nuevoAnulado ? "ANULAR" : "REACTIVAR", $"Neto={nuevoNeto}", ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<List<ResumenTurno>> GetTonelajePorTurnoAsync(DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        List<Pesaje> rows = await QueryPesajesAsync(desde, hasta, incluirAnulados: false, maxRows: 50000, ct: ct).ConfigureAwait(false);
        var turnos = new Dictionary<int, (string Nombre, List<Pesaje> Items)>
        {
            [1] = ("Turno 1 (Mañana 07:00 - 15:00)", []),
            [2] = ("Turno 2 (Tarde 15:00 - 23:00)", []),
            [3] = ("Turno 3 (Noche 23:00 - 07:00)", [])
        };

        foreach (Pesaje p in rows)
        {
            int hora = 8;
            if (!string.IsNullOrEmpty(p.HoraSalida) && p.HoraSalida.Length >= 2 &&
                int.TryParse(p.HoraSalida[..2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hParsed))
            {
                hora = hParsed;
            }
            int turno = hora is >= 7 and < 15 ? 1 : (hora is >= 15 and < 23 ? 2 : 3);
            turnos[turno].Items.Add(p);
        }

        return turnos.Select(kv => new ResumenTurno(
            kv.Key,
            kv.Value.Nombre,
            kv.Value.Items.Count,
            kv.Value.Items.Sum(x => x.Bruto),
            kv.Value.Items.Sum(x => x.Tara),
            kv.Value.Items.Sum(x => x.Neto))).ToList();
    }

    public async Task<List<TaraCamionReporte>> GetTarasCamionesReporteAsync(bool soloTarareg = false, CancellationToken ct = default)
    {
        var list = new List<TaraCamionReporte>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        var sql = """
            SELECT C.*, COALESCE(T.NOMBRE, C.TRANSP) AS NMTRANSP, COALESCE(CH.NOMBRE, C.CHOFER) AS NMCHOFER
            FROM DBCAMIONES C
            LEFT JOIN DBTRANSP T ON TRIM(T.TRANSP) = TRIM(C.TRANSP)
            LEFT JOIN DBCHOFERES CH ON TRIM(CH.CHOFER) = TRIM(C.CHOFER)
            WHERE C.TARA > 0
            """;
        if (soloTarareg)
        {
            sql += " AND C.TARAREG = '1'";
        }
        sql += " ORDER BY C.FTARA DESC, C.PATENTE";
        await using var cmd = new FbCommand(sql, conn);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new TaraCamionReporte(
                GetStr(r, "PATENTE"),
                GetStr(r, "ACOPLADO"),
                GetStr(r, "NMTRANSP"),
                GetStr(r, "NMCHOFER"),
                GetInt(r, "TARA"),
                GetDate(r, "FTARA"),
                GetStr(r, "HTARA"),
                GetFlag(r, "TARAREG"),
                GetDate(r, "FTARAREG"),
                GetShort(r, "NDIAS")));
        }
        return list;
    }

    private static async Task<int> AllocateNextPesajeFolioAsync(FbConnection conn, FbTransaction tx, CancellationToken ct)
    {
        await using var genCmd = new FbCommand("SELECT NEXT VALUE FOR GEN_FLPESAJE FROM RDB$DATABASE", conn, tx);
        int folio = Convert.ToInt32(await genCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);

        await using var folCmd = new FbCommand("UPDATE DBFOLIOS SET FLPESAJE = @NEXT WHERE ID = 1", conn, tx);
        AddParam(folCmd, "@NEXT", folio + 1);
        await folCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return folio;
    }

    private static Pesaje MapPesaje(FbDataReader r) => new()
    {
        FolioPesaje = GetInt(r, "FLPESAJE"),
        Patente = GetStr(r, "PATENTE"),
        Acoplado = GetStr(r, "ACOPLADO"),
        FolioPesajeMop = GetInt(r, "FLPESAJEMOP"),
        Producto = GetStr(r, "PRODUCTO"),
        NombreProducto = GetStr(r, "NMPRODUCTO"),
        Cliente = GetStr(r, "CLIENTE"),
        NombreCliente = GetStr(r, "NMCLIENTE"),
        Origen = GetStr(r, "ORIGEN"),
        NombreOrigen = GetStr(r, "NMORIGEN"),
        Destino = GetStr(r, "DESTINO"),
        NombreDestino = GetStr(r, "NMDESTINO"),
        GuiaDespacho = GetStr(r, "GDESPACHO"),
        NetoInformado = GetInt(r, "NETOINF"),
        FechaIngreso = GetDate(r, "FECHAING") ?? DateTime.Today,
        HoraIngreso = GetStr(r, "HORAING"),
        FechaSalida = GetDate(r, "FECHASAL") ?? (GetDate(r, "FECHAING") ?? DateTime.Today),
        HoraSalida = GetStr(r, "HORASAL"),
        FechaModificacion = GetDate(r, "FECHAMOD"),
        HoraModificacion = GetNullableStr(r, "HORAMOD"),
        Bruto = GetInt(r, "BRUTO"),
        Tara = GetInt(r, "TARA"),
        Neto = GetInt(r, "NETO"),
        TipoMop = GetShort(r, "TMOP"),
        Largo = GetShort(r, "LARGO"),
        MaxBruto = GetInt(r, "MAXBRUTO"),
        EstadoMop = short.TryParse(GetStr(r, "ESTADOMOP"), out short em) ? (EstadoNormaMop)em : EstadoNormaMop.NoAplica,
        Chofer = GetStr(r, "CHOFER"),
        NombreChofer = GetStr(r, "NMCHOFER"),
        Traspaso = GetNullableStr(r, "TRASPASO"),
        Observacion1 = GetStr(r, "OBSERVA1"),
        Observacion2 = GetStr(r, "OBSERVA2"),
        Transportista = GetStr(r, "TRANSP"),
        NombreTransportista = GetStr(r, "NMTRANSP"),
        TaraRegistrada = GetFlag(r, "TARAREG"),
        TipoPesaje = (TipoPesaje)GetShort(r, "TPESAJE", 1),
        SavedToServer = GetNullableStr(r, "SAVEDTOSERVER"),
        UsuarioIngreso = GetShort(r, "USUAING"),
        NombreUsuarioIngreso = GetStr(r, "NMUSUAING"),
        UsuarioSalida = GetShort(r, "USUASAL"),
        NombreUsuarioSalida = GetStr(r, "NMUSUASAL"),
        UsuarioModificacion = GetShort(r, "USUAMOD"),
        NombreUsuarioModificacion = GetStr(r, "NMUSUAMOD"),
        UsuarioEliminacion = GetShort(r, "USUADEL"),
        NombreUsuarioEliminacion = GetStr(r, "NMUSUADEL"),
        Anulado = GetFlag(r, "ANULADO")
    };
}
