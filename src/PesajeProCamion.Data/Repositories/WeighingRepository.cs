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

public enum FolioEstadoCheck
{
    Libre = 0,
    EnProcesoTransito = 1,
    HistoricoPesaje = 2,
    AnuladoTransito = 3,
    Invalido = 4
}

public sealed record FolioAvailabilityResult(
    bool Disponible,
    FolioEstadoCheck Estado,
    int FolioEvaluado,
    string? PatenteAsociada,
    int ProximoFolioSugerido,
    string Mensaje);

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
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        string pat = ChileanValidators.NormalizarPatente(tr.Patente);
        return await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            if (tr.FolioPesaje <= 0)
            {
                tr.FolioPesaje = await AllocateNextPesajeFolioAsync(conn, tx, caps, ct).ConfigureAwait(false);
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
            AddParam(cmd, "@CH", FitLength(tr.Chofer, caps.ChoferPesajeMaxLength));
            AddParam(cmd, "@OB1", FitLength(tr.Observacion1, caps.ObservacionMaxLength));
            AddParam(cmd, "@TR", FitLength(tr.Transportista, 10));
            AddParam(cmd, "@TP", (short)tr.TipoPesaje);
            AddParam(cmd, "@SV", tr.SavedToServer);
            AddParam(cmd, "@UI", tr.UsuarioIngreso);
            AddParam(cmd, "@TRS", tr.Traspaso);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await SecurityRepository.InsertAuditInternalAsync(conn, tx, caps, tr.UsuarioIngreso, "PESAJE", "DBTRANSITO", tr.FolioPesaje.ToString(CultureInfo.InvariantCulture), "INGRESO", $"Patente={pat}, Bruto={tr.Bruto}, Tara={tr.Tara}", ct).ConfigureAwait(false);
            return tr.FolioPesaje;
        }, ct).ConfigureAwait(false);
    }

    public async Task<int> CompletarPesajeAsync(Pesaje p, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(p);
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        string pat = ChileanValidators.NormalizarPatente(p.Patente);
        p.Patente = pat;
        p.Neto = WeighingCalculator.CalcularNeto(p.Bruto, p.Tara);

        int originalTransitoFolio = p.FolioPesaje;
        return await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            if (p.FolioPesaje <= 0)
            {
                p.FolioPesaje = await AllocateNextPesajeFolioAsync(conn, tx, caps, ct).ConfigureAwait(false);
            }
            else
            {
                await using var existsCmd = new FbCommand("SELECT COUNT(*) FROM DBPESAJES WHERE FLPESAJE = @FP", conn, tx);
                AddParam(existsCmd, "@FP", p.FolioPesaje);
                int exists = Convert.ToInt32(await existsCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (exists > 0)
                {
                    p.FolioPesaje = await AllocateNextPesajeFolioAsync(conn, tx, caps, ct).ConfigureAwait(false);
                }
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
            AddParam(insCmd, "@CH", FitLength(p.Chofer, caps.ChoferPesajeMaxLength));
            AddParam(insCmd, "@TRS", p.Traspaso);
            AddParam(insCmd, "@OB1", FitLength(p.Observacion1, caps.ObservacionMaxLength));
            AddParam(insCmd, "@OB2", FitLength(p.Observacion2, caps.ObservacionMaxLength));
            AddParam(insCmd, "@TR", FitLength(p.Transportista, 10));
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
            AddParam(delTr, "@FP", originalTransitoFolio > 0 ? originalTransitoFolio : p.FolioPesaje);
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

            await SecurityRepository.InsertAuditInternalAsync(conn, tx, caps, p.UsuarioSalida, "PESAJE", "DBPESAJES", p.FolioPesaje.ToString(CultureInfo.InvariantCulture), "SALIDA", $"Patente={pat}, Bruto={p.Bruto}, Tara={p.Tara}, Neto={p.Neto}", ct).ConfigureAwait(false);
            return p.FolioPesaje;
        }, ct).ConfigureAwait(false);
    }

    public async Task EliminarTransitoAsync(int folioPesaje, short usuarioId, string motivo, CancellationToken ct = default)
    {
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        DateTime now = DateTime.Now;
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            if (caps.HasTranAnulAuditColumns)
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
                AddParam(archCmd, "@MO", FitLength(motivo, 120));
                AddParam(archCmd, "@FP", folioPesaje);
                await archCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            else
            {
                await using var selTr = new FbCommand("SELECT FIRST 1 * FROM DBTRANSITO WHERE FLPESAJE = @FP", conn, tx);
                AddParam(selTr, "@FP", folioPesaje);
                await using FbDataReader trR = await selTr.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await trR.ReadAsync(ct).ConfigureAwait(false))
                {
                    string pat = GetStr(trR, "PATENTE");
                    string aco = GetStr(trR, "ACOPLADO");
                    string prod = FitLength(GetStr(trR, "PRODUCTO"), 10);
                    string cli = FitLength(GetStr(trR, "CLIENTE"), 10);
                    string dest = FitLength(GetStr(trR, "DESTINO"), 10);
                    string gdespStr = GetStr(trR, "GDESPACHO");
                    int netInf = GetInt(trR, "NETOINF");
                    DateTime fi = GetDate(trR, "FECHAING") ?? now.Date;
                    string hi = GetStr(trR, "HORAING");
                    int br = GetInt(trR, "BRUTO");
                    int ta = GetInt(trR, "TARA");
                    short tm = GetShort(trR, "TMOP");
                    short la = GetShort(trR, "LARGO");
                    int mb = GetInt(trR, "MAXBRUTO");
                    string obs = FitLength(GetStr(trR, "OBSERVA1"), caps.ObservacionMaxLength);
                    string chof = FitLength(GetStr(trR, "CHOFER"), 35);
                    string trsp = FitLength(GetStr(trR, "TRANSP"), 10);
                    short ui = GetShort(trR, "USUAING", usuarioId);
                    await trR.CloseAsync().ConfigureAwait(false);

                    await using var legCmd = new FbCommand("""
                        INSERT INTO DBTRAN_ANUL (
                            FLPESAJE, PATENTE, ACOPLADO, PRODUCTO, CLIENTE, DESTINO, GDESPACHO, NETOINF,
                            FECHAING, HORAING, BRUTO, TARA, TMOP, LARGO, MAXBRUTO, OBSERVA, CHOFER, TRANSP, USUAING)
                        VALUES (
                            @FP, @PAT, @ACO, @PR, @CL, @DE, @GD, @NI,
                            @FI, @HI, @BR, @TA, @TM, @LA, @MB, @OB, @CH, @TR, @UI)
                        """, conn, tx);
                    AddParam(legCmd, "@FP", folioPesaje);
                    AddParam(legCmd, "@PAT", pat);
                    AddParam(legCmd, "@ACO", aco);
                    AddParam(legCmd, "@PR", prod);
                    AddParam(legCmd, "@CL", cli);
                    AddParam(legCmd, "@DE", dest);
                    object gdParam = caps.TranAnulGdespachoIsInteger
                        ? (int.TryParse(gdespStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int gdInt) ? gdInt : 0)
                        : FitLength(gdespStr, 50);
                    AddParam(legCmd, "@GD", gdParam);
                    AddParam(legCmd, "@NI", netInf);
                    AddParam(legCmd, "@FI", fi);
                    AddParam(legCmd, "@HI", hi);
                    AddParam(legCmd, "@BR", br);
                    AddParam(legCmd, "@TA", ta);
                    AddParam(legCmd, "@TM", tm);
                    AddParam(legCmd, "@LA", la);
                    AddParam(legCmd, "@MB", mb);
                    AddParam(legCmd, "@OB", obs);
                    AddParam(legCmd, "@CH", chof);
                    AddParam(legCmd, "@TR", trsp);
                    AddParam(legCmd, "@UI", ui);
                    await legCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            await using var delCmd = new FbCommand("DELETE FROM DBTRANSITO WHERE FLPESAJE = @FP", conn, tx);
            AddParam(delCmd, "@FP", folioPesaje);
            await delCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await SecurityRepository.InsertAuditInternalAsync(conn, tx, caps, usuarioId, "TRANSITO", "DBTRANSITO", folioPesaje.ToString(CultureInfo.InvariantCulture), "ELIMINAR", motivo, ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<List<RegistroTransitoAnulado>> GetTransitoAnuladoAsync(DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        var list = new List<RegistroTransitoAnulado>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);

        if (caps.HasTranAnulAuditColumns)
        {
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

        await using var legCmd = new FbCommand("""
            SELECT T.*, COALESCE(U.NOMBRE, '') AS NMUSUAING
            FROM DBTRAN_ANUL T
            LEFT JOIN DBUSUARIOS U ON U.USUARIO = T.USUAING
            WHERE T.FECHAING >= @D AND T.FECHAING <= @H
            ORDER BY T.FECHAING DESC, T.FLPESAJE DESC
            """, conn);
        AddParam(legCmd, "@D", desde.Date);
        AddParam(legCmd, "@H", hasta.Date);
        await using FbDataReader lr = await legCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await lr.ReadAsync(ct).ConfigureAwait(false))
        {
            DateTime fIng = GetDate(lr, "FECHAING") ?? DateTime.MinValue;
            list.Add(new RegistroTransitoAnulado
            {
                FolioPesaje = GetInt(lr, "FLPESAJE"),
                Patente = GetStr(lr, "PATENTE"),
                Producto = GetStr(lr, "PRODUCTO"),
                Cliente = GetStr(lr, "CLIENTE"),
                FechaIngreso = fIng,
                HoraIngreso = GetStr(lr, "HORAING"),
                Bruto = GetInt(lr, "BRUTO"),
                Tara = GetInt(lr, "TARA"),
                Observacion = GetStr(lr, "OBSERVA"),
                FechaAnulacion = fIng,
                HoraAnulacion = GetStr(lr, "HORAING"),
                NombreUsuarioAnulacion = GetStr(lr, "NMUSUAING"),
                Motivo = "(BD Legacy sin motivo)"
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
            WHERE P.FLPESAJE IS NOT NULL
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

    public async Task<Pesaje?> GetPesajePorFolioAsync(int folio, CancellationToken ct = default)
    {
        List<Pesaje> rows = await QueryPesajesAsync(folioExacto: folio, maxRows: 1, ct: ct).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    public async Task<int> GetNextSuggestedFolioAsync(CancellationToken ct = default)
    {
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        return await PeekNextSafeFolioAsync(conn, null, caps, ct).ConfigureAwait(false);
    }

    public async Task<FolioAvailabilityResult> CheckFolioAvailabilityAsync(
        int folioPesaje,
        int? ignoreTransitoFolio = null,
        int? ignorePesajeFolio = null,
        CancellationToken ct = default)
    {
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        return await CheckFolioAvailabilityInternalAsync(conn, null, caps, folioPesaje, ignoreTransitoFolio, ignorePesajeFolio, ct).ConfigureAwait(false);
    }

    public async Task<int> CreateManualTicketAsync(
        Pesaje ticket,
        bool crearEnProceso,
        short usuarioId,
        bool preserveManualNeto = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        string pat = ChileanValidators.NormalizarPatente(ticket.Patente);
        if (!ChileanValidators.EsPatenteValida(pat))
        {
            throw new InvalidOperationException("La patente ingresada no es válida.");
        }

        ticket.Patente = pat;
        ticket.Acoplado = ChileanValidators.NormalizarPatente(ticket.Acoplado);

        return await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            if (ticket.FolioPesaje > 0)
            {
                FolioAvailabilityResult chk = await CheckFolioAvailabilityInternalAsync(conn, tx, caps, ticket.FolioPesaje, null, null, ct).ConfigureAwait(false);
                if (!chk.Disponible)
                {
                    throw new InvalidOperationException(chk.Mensaje);
                }
                await AdvanceFolioPointersIfNeededAsync(conn, tx, caps, ticket.FolioPesaje, ct).ConfigureAwait(false);
            }
            else
            {
                ticket.FolioPesaje = await AllocateNextPesajeFolioAsync(conn, tx, caps, ct).ConfigureAwait(false);
            }

            if (crearEnProceso)
            {
                await using var trCmd = new FbCommand("""
                    INSERT INTO DBTRANSITO (
                        FLPESAJE, PATENTE, ACOPLADO, PRODUCTO, CLIENTE, ORIGEN, DESTINO, GDESPACHO,
                        NETOINF, FECHAING, HORAING, BRUTO, TARA, TMOP, LARGO, MAXBRUTO, CHOFER,
                        OBSERVA1, TRANSP, TPESAJE, SAVEDTOSERVER, USUAING, TRASPASO)
                    VALUES (
                        @FP, @PAT, @ACO, @PR, @CL, @OR, @DE, @GD,
                        @NI, @FI, @HI, @BR, @TA, @TM, @LA, @MB, @CH,
                        @OB1, @TR, @TP, @SV, @UI, @TRS)
                    """, conn, tx);
                AddParam(trCmd, "@FP", ticket.FolioPesaje);
                AddParam(trCmd, "@PAT", ticket.Patente);
                AddParam(trCmd, "@ACO", ticket.Acoplado);
                AddParam(trCmd, "@PR", ticket.Producto.Trim());
                AddParam(trCmd, "@CL", ticket.Cliente.Trim());
                AddParam(trCmd, "@OR", ticket.Origen.Trim());
                AddParam(trCmd, "@DE", ticket.Destino.Trim());
                AddParam(trCmd, "@GD", ticket.GuiaDespacho.Trim());
                AddParam(trCmd, "@NI", ticket.NetoInformado);
                AddParam(trCmd, "@FI", ticket.FechaIngreso.Date);
                AddParam(trCmd, "@HI", ticket.HoraIngreso);
                AddParam(trCmd, "@BR", ticket.Bruto);
                AddParam(trCmd, "@TA", ticket.Tara);
                AddParam(trCmd, "@TM", ticket.TipoMop);
                AddParam(trCmd, "@LA", ticket.Largo);
                AddParam(trCmd, "@MB", ticket.MaxBruto);
                AddParam(trCmd, "@CH", FitLength(ticket.Chofer, caps.ChoferPesajeMaxLength));
                AddParam(trCmd, "@OB1", FitLength(ticket.Observacion1, caps.ObservacionMaxLength));
                AddParam(trCmd, "@TR", FitLength(ticket.Transportista, 10));
                AddParam(trCmd, "@TP", (short)ticket.TipoPesaje);
                AddParam(trCmd, "@SV", ticket.SavedToServer);
                AddParam(trCmd, "@UI", usuarioId);
                AddParam(trCmd, "@TRS", ticket.Traspaso);
                await trCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                await SecurityRepository.InsertAuditInternalAsync(conn, tx, caps, usuarioId, "GESTION", "DBTRANSITO", ticket.FolioPesaje.ToString(CultureInfo.InvariantCulture), "CREAR_EN_PROCESO", $"Patente={pat}, Bruto={ticket.Bruto}, Tara={ticket.Tara}", ct).ConfigureAwait(false);
                return ticket.FolioPesaje;
            }

            ticket.Neto = ticket.Anulado
                ? 0
                : (preserveManualNeto ? Math.Max(0, ticket.Neto) : WeighingCalculator.CalcularNeto(ticket.Bruto, ticket.Tara));
            await using var insCmd = CreateInsertPesajeCommand(conn, tx, caps, ticket, usuarioId);
            await insCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await SecurityRepository.InsertAuditInternalAsync(conn, tx, caps, usuarioId, "GESTION", "DBPESAJES", ticket.FolioPesaje.ToString(CultureInfo.InvariantCulture), "CREAR_TICKET", $"Patente={pat}, Bruto={ticket.Bruto}, Tara={ticket.Tara}, Neto={ticket.Neto}", ct).ConfigureAwait(false);
            return ticket.FolioPesaje;
        }, ct).ConfigureAwait(false);
    }

    private static FbCommand CreateInsertPesajeCommand(FbConnection conn, FbTransaction tx, DatabaseCapabilities caps, Pesaje ticket, short usuarioId)
    {
        var insCmd = new FbCommand("""
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
        AddParam(insCmd, "@FP", ticket.FolioPesaje);
        AddParam(insCmd, "@PAT", ticket.Patente);
        AddParam(insCmd, "@ACO", ticket.Acoplado);
        AddParam(insCmd, "@FM", ticket.FolioPesajeMop);
        AddParam(insCmd, "@PR", FitLength(ticket.Producto, 10));
        AddParam(insCmd, "@CL", FitLength(ticket.Cliente, 10));
        AddParam(insCmd, "@OR", FitLength(ticket.Origen, 10));
        AddParam(insCmd, "@DE", FitLength(ticket.Destino, 10));
        AddParam(insCmd, "@GD", FitLength(ticket.GuiaDespacho, 50));
        AddParam(insCmd, "@NI", ticket.NetoInformado);
        AddParam(insCmd, "@FI", ticket.FechaIngreso.Date);
        AddParam(insCmd, "@HI", ticket.HoraIngreso);
        AddParam(insCmd, "@FS", ticket.FechaSalida.Date);
        AddParam(insCmd, "@HS", ticket.HoraSalida);
        AddParam(insCmd, "@FMOD", DateTime.Today);
        AddParam(insCmd, "@HMOD", DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture));
        AddParam(insCmd, "@BR", ticket.Bruto);
        AddParam(insCmd, "@TA", ticket.Tara);
        AddParam(insCmd, "@NE", ticket.Neto);
        AddParam(insCmd, "@TM", ticket.TipoMop);
        AddParam(insCmd, "@LA", ticket.Largo);
        AddParam(insCmd, "@MB", ticket.MaxBruto);
        AddParam(insCmd, "@EM", ((short)ticket.EstadoMop).ToString(CultureInfo.InvariantCulture));
        AddParam(insCmd, "@CH", FitLength(ticket.Chofer, caps.ChoferPesajeMaxLength));
        AddParam(insCmd, "@TRS", ticket.Traspaso);
        AddParam(insCmd, "@OB1", FitLength(ticket.Observacion1, caps.ObservacionMaxLength));
        AddParam(insCmd, "@OB2", FitLength(ticket.Observacion2, caps.ObservacionMaxLength));
        AddParam(insCmd, "@TR", FitLength(ticket.Transportista, 10));
        AddParam(insCmd, "@TRG", ticket.TaraRegistrada ? "1" : "0");
        AddParam(insCmd, "@TP", (short)ticket.TipoPesaje);
        AddParam(insCmd, "@SV", ticket.SavedToServer);
        AddParam(insCmd, "@UI", ticket.UsuarioIngreso > 0 ? ticket.UsuarioIngreso : usuarioId);
        AddParam(insCmd, "@US", ticket.UsuarioSalida > 0 ? ticket.UsuarioSalida : usuarioId);
        AddParam(insCmd, "@UM", usuarioId);
        AddParam(insCmd, "@UD", ticket.Anulado ? usuarioId : (short)0);
        AddParam(insCmd, "@AN", ticket.Anulado ? "1" : "0");
        return insCmd;
    }

    public async Task ModificarTransitoAsync(RegistroTransito tr, short usuarioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tr);
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        string pat = ChileanValidators.NormalizarPatente(tr.Patente);
        if (!ChileanValidators.EsPatenteValida(pat))
        {
            throw new InvalidOperationException("La patente ingresada no es válida.");
        }

        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand("""
                UPDATE DBTRANSITO SET
                    PATENTE = @PAT, ACOPLADO = @ACO, PRODUCTO = @PR, CLIENTE = @CL,
                    ORIGEN = @OR, DESTINO = @DE, GDESPACHO = @GD, NETOINF = @NI,
                    FECHAING = @FI, HORAING = @HI, BRUTO = @BR, TARA = @TA,
                    CHOFER = @CH, OBSERVA1 = @OB1, TRANSP = @TR, TPESAJE = @TP
                WHERE FLPESAJE = @FP
                """, conn, tx);
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
            AddParam(cmd, "@CH", FitLength(tr.Chofer, caps.ChoferPesajeMaxLength));
            AddParam(cmd, "@OB1", FitLength(tr.Observacion1, caps.ObservacionMaxLength));
            AddParam(cmd, "@TR", FitLength(tr.Transportista, 10));
            AddParam(cmd, "@TP", (short)tr.TipoPesaje);
            AddParam(cmd, "@FP", tr.FolioPesaje);
            int affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            if (affected == 0)
            {
                throw new InvalidOperationException($"El ticket en proceso #{tr.FolioPesaje} ya no se encuentra en tránsito.");
            }

            await SecurityRepository.InsertAuditInternalAsync(conn, tx, caps, usuarioId, "TRANSITO", "DBTRANSITO", tr.FolioPesaje.ToString(CultureInfo.InvariantCulture), "MODIFICAR", $"Patente={pat}, Bruto={tr.Bruto}, Tara={tr.Tara}", ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task ModificarPesajeAsync(
        Pesaje p,
        short usuarioId,
        bool preserveManualNeto = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(p);
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        DateTime now = DateTime.Now;
        p.Neto = p.Anulado
            ? 0
            : (preserveManualNeto ? Math.Max(0, p.Neto) : WeighingCalculator.CalcularNeto(p.Bruto, p.Tara));
        p.FechaModificacion = now.Date;
        p.HoraModificacion = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        p.UsuarioModificacion = usuarioId;

        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand("""
                UPDATE DBPESAJES SET
                    PATENTE = @PAT, ACOPLADO = @ACO, PRODUCTO = @PR, CLIENTE = @CL,
                    ORIGEN = @OR, DESTINO = @DE, GDESPACHO = @GD, NETOINF = @NI,
                    FECHAING = @FI, HORAING = @HI, FECHASAL = @FS, HORASAL = @HS,
                    FECHAMOD = @FMOD, HORAMOD = @HMOD, BRUTO = @BR, TARA = @TA, NETO = @NE,
                    CHOFER = @CH, OBSERVA1 = @OB1, OBSERVA2 = @OB2, TRANSP = @TR,
                    TARAREG = @TRG, TPESAJE = @TP, ANULADO = @AN, USUAMOD = @UM
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
            AddParam(cmd, "@FI", p.FechaIngreso.Date);
            AddParam(cmd, "@HI", p.HoraIngreso);
            AddParam(cmd, "@FS", p.FechaSalida.Date);
            AddParam(cmd, "@HS", p.HoraSalida);
            AddParam(cmd, "@FMOD", p.FechaModificacion);
            AddParam(cmd, "@HMOD", p.HoraModificacion);
            AddParam(cmd, "@BR", p.Bruto);
            AddParam(cmd, "@TA", p.Tara);
            AddParam(cmd, "@NE", p.Neto);
            AddParam(cmd, "@CH", FitLength(p.Chofer, caps.ChoferPesajeMaxLength));
            AddParam(cmd, "@OB1", FitLength(p.Observacion1, caps.ObservacionMaxLength));
            AddParam(cmd, "@OB2", FitLength(p.Observacion2, caps.ObservacionMaxLength));
            AddParam(cmd, "@TR", FitLength(p.Transportista, 10));
            AddParam(cmd, "@TRG", p.TaraRegistrada ? "1" : "0");
            AddParam(cmd, "@TP", (short)p.TipoPesaje);
            AddParam(cmd, "@AN", p.Anulado ? "1" : "0");
            AddParam(cmd, "@UM", usuarioId);
            AddParam(cmd, "@FP", p.FolioPesaje);
            int affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            if (affected == 0)
            {
                throw new InvalidOperationException($"No se encontró el ticket #{p.FolioPesaje} en histórico.");
            }

            await SecurityRepository.InsertAuditInternalAsync(conn, tx, caps, usuarioId, "GESTION", "DBPESAJES", p.FolioPesaje.ToString(CultureInfo.InvariantCulture), "MODIFICAR", $"Patente={p.Patente}, Bruto={p.Bruto}, Tara={p.Tara}, Neto={p.Neto}", ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Divide un ticket cerrado en 2 tickets atómicamente: conserva el folio origen con Parte 1
    /// y crea un nuevo ticket con Parte 2 asignándole un folio libre sin colisionar con En Proceso ni históricos.
    /// </summary>
    public async Task<TicketSplitResult> SplitTicketAsync(
        TicketSplitRequest request,
        short usuarioId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OriginalFolio <= 0)
        {
            throw new InvalidOperationException("El folio original a dividir debe ser mayor a 0.");
        }
        if (request.NetoParte1 <= 0 || request.NetoParte2 <= 0)
        {
            throw new InvalidOperationException("Ambas partes de la división deben tener Neto mayor a 0.");
        }

        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);

        return await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var sel = new FbCommand("SELECT FIRST 1 * FROM DBPESAJES WHERE FLPESAJE = @FP", conn, tx);
            AddParam(sel, "@FP", request.OriginalFolio);
            await using FbDataReader r = await sel.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await r.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"No existe el ticket histórico #{request.OriginalFolio} para dividir.");
            }
            if (GetFlag(r, "ANULADO"))
            {
                throw new InvalidOperationException($"El ticket #{request.OriginalFolio} está ANULADO y no puede dividirse.");
            }

            int origNeto = GetInt(r, "NETO");
            if (origNeto <= 0)
            {
                origNeto = WeighingCalculator.CalcularNeto(GetInt(r, "BRUTO"), GetInt(r, "TARA"));
            }
            if (origNeto < 2)
            {
                throw new InvalidOperationException($"El ticket #{request.OriginalFolio} tiene Neto={origNeto} kg (mínimo 2 kg para dividir).");
            }
            if (request.NetoParte1 + request.NetoParte2 != origNeto)
            {
                throw new InvalidOperationException($"La suma Neto Parte 1 ({request.NetoParte1:N0}) + Neto Parte 2 ({request.NetoParte2:N0}) debe igualar al Neto original ({origNeto:N0}).");
            }

            var clone = new Pesaje
            {
                Patente = GetStr(r, "PATENTE"),
                Acoplado = GetStr(r, "ACOPLADO"),
                FolioPesajeMop = GetInt(r, "FLPESAJEMOP"),
                Producto = GetStr(r, "PRODUCTO"),
                Cliente = GetStr(r, "CLIENTE"),
                Origen = GetStr(r, "ORIGEN"),
                Destino = GetStr(r, "DESTINO"),
                GuiaDespacho = GetStr(r, "GDESPACHO"),
                NetoInformado = 0,
                FechaIngreso = GetDate(r, "FECHAING") ?? DateTime.Today,
                HoraIngreso = GetStr(r, "HORAING"),
                FechaSalida = GetDate(r, "FECHASAL") ?? (GetDate(r, "FECHAING") ?? DateTime.Today),
                HoraSalida = GetStr(r, "HORASAL"),
                TipoMop = GetShort(r, "TMOP"),
                Largo = GetShort(r, "LARGO"),
                MaxBruto = GetInt(r, "MAXBRUTO"),
                EstadoMop = short.TryParse(GetStr(r, "ESTADOMOP"), out short em) ? (EstadoNormaMop)em : EstadoNormaMop.NoAplica,
                Chofer = GetStr(r, "CHOFER"),
                Traspaso = GetNullableStr(r, "TRASPASO"),
                Observacion1 = GetStr(r, "OBSERVA1"),
                Observacion2 = GetStr(r, "OBSERVA2"),
                Transportista = GetStr(r, "TRANSP"),
                TaraRegistrada = GetFlag(r, "TARAREG"),
                TipoPesaje = (TipoPesaje)GetShort(r, "TPESAJE", 1),
                SavedToServer = GetNullableStr(r, "SAVEDTOSERVER"),
                UsuarioIngreso = GetShort(r, "USUAING", usuarioId),
                UsuarioSalida = GetShort(r, "USUASAL", usuarioId),
                Anulado = false
            };
            await r.CloseAsync().ConfigureAwait(false);

            int newFolio;
            if (request.CustomNewFolio is > 0)
            {
                FolioAvailabilityResult chk = await CheckFolioAvailabilityInternalAsync(conn, tx, caps, request.CustomNewFolio.Value, null, null, ct).ConfigureAwait(false);
                if (!chk.Disponible)
                {
                    throw new InvalidOperationException(chk.Mensaje);
                }
                newFolio = request.CustomNewFolio.Value;
                await AdvanceFolioPointersIfNeededAsync(conn, tx, caps, newFolio, ct).ConfigureAwait(false);
            }
            else
            {
                newFolio = await AllocateNextPesajeFolioAsync(conn, tx, caps, ct).ConfigureAwait(false);
            }

            string obs1Orig = clone.Observacion2;
            string obs2New = clone.Observacion2;
            if (request.AppendSplitNote)
            {
                string tagOrig = $"Dividido #{request.OriginalFolio}->{request.OriginalFolio}/{newFolio} (Orig {origNeto})";
                string tagNew = $"División desde Folio #{request.OriginalFolio} (Orig {origNeto})";
                obs1Orig = FitLength(tagOrig, caps.ObservacionMaxLength);
                obs2New = FitLength(tagNew, caps.ObservacionMaxLength);
            }

            DateTime now = DateTime.Now;
            string hhmm = now.ToString("HH:mm", CultureInfo.InvariantCulture);

            await using var upd = new FbCommand("""
                UPDATE DBPESAJES SET
                    BRUTO = @BR, TARA = @TA, NETO = @NE,
                    OBSERVA2 = @OB2, FECHAMOD = @FMOD, HORAMOD = @HMOD, USUAMOD = @UM
                WHERE FLPESAJE = @FP
                """, conn, tx);
            AddParam(upd, "@BR", request.BrutoParte1);
            AddParam(upd, "@TA", request.TaraParte1);
            AddParam(upd, "@NE", request.NetoParte1);
            AddParam(upd, "@OB2", obs1Orig);
            AddParam(upd, "@FMOD", now.Date);
            AddParam(upd, "@HMOD", hhmm);
            AddParam(upd, "@UM", usuarioId);
            AddParam(upd, "@FP", request.OriginalFolio);
            await upd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            clone.FolioPesaje = newFolio;
            clone.Bruto = request.BrutoParte2;
            clone.Tara = request.TaraParte2;
            clone.Neto = request.NetoParte2;
            clone.Observacion2 = obs2New;
            await using var insNew = CreateInsertPesajeCommand(conn, tx, caps, clone, usuarioId);
            await insNew.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await SecurityRepository.InsertAuditInternalAsync(conn, tx, caps, usuarioId, "GESTION", "DBPESAJES", request.OriginalFolio.ToString(CultureInfo.InvariantCulture), "DIVIDIR_ORIGEN", $"Neto1={request.NetoParte1}, NuevoFolio={newFolio}, Neto2={request.NetoParte2}", ct).ConfigureAwait(false);
            await SecurityRepository.InsertAuditInternalAsync(conn, tx, caps, usuarioId, "GESTION", "DBPESAJES", newFolio.ToString(CultureInfo.InvariantCulture), "CREAR_DIVISION", $"OrigenFolio={request.OriginalFolio}, Neto2={request.NetoParte2}", ct).ConfigureAwait(false);

            return new TicketSplitResult(
                request.OriginalFolio,
                newFolio,
                request.NetoParte1,
                request.NetoParte2,
                request.BrutoParte1,
                request.TaraParte1,
                request.BrutoParte2,
                request.TaraParte2);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Anula o reactiva un pesaje específico por su folio (corrige bug legacy de PA_DEL_PESAJES sin filtro).</summary>
    public async Task ToggleAnularPesajeAsync(int folioPesaje, short usuarioId, CancellationToken ct = default)
    {
        DatabaseCapabilities caps = await _db.GetCapabilitiesAsync(ct).ConfigureAwait(false);
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

            await SecurityRepository.InsertAuditInternalAsync(conn, tx, caps, usuarioId, "GESTION", "DBPESAJES", folioPesaje.ToString(CultureInfo.InvariantCulture), nuevoAnulado ? "ANULAR" : "REACTIVAR", $"Neto={nuevoNeto}", ct).ConfigureAwait(false);
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

    private static async Task<int> PeekNextSafeFolioAsync(FbConnection conn, FbTransaction? tx, DatabaseCapabilities caps, CancellationToken ct)
    {
        await using var fCmd = new FbCommand(
            """
            SELECT COALESCE((SELECT FIRST 1 FLPESAJE FROM DBFOLIOS), 1),
                   COALESCE((SELECT MAX(FLPESAJE) FROM DBPESAJES WHERE FLPESAJE IS NOT NULL), 0),
                   COALESCE((SELECT MAX(FLPESAJE) FROM DBTRANSITO WHERE FLPESAJE IS NOT NULL), 0),
                   COALESCE((SELECT MAX(FLPESAJE) FROM DBTRAN_ANUL WHERE FLPESAJE IS NOT NULL), 0)
            FROM RDB$DATABASE
            """, conn, tx);
        await using FbDataReader fr = await fCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await fr.ReadAsync(ct).ConfigureAwait(false);
        int curFol = Convert.ToInt32(fr.GetValue(0), CultureInfo.InvariantCulture);
        int maxP = Convert.ToInt32(fr.GetValue(1), CultureInfo.InvariantCulture);
        int maxT = Convert.ToInt32(fr.GetValue(2), CultureInfo.InvariantCulture);
        int maxA = Convert.ToInt32(fr.GetValue(3), CultureInfo.InvariantCulture);
        await fr.CloseAsync().ConfigureAwait(false);

        int genCurrent = 0;
        if (caps.HasGenPesaje)
        {
            await using var gCmd = new FbCommand("SELECT GEN_ID(GEN_FLPESAJE, 0) FROM RDB$DATABASE", conn, tx);
            genCurrent = Convert.ToInt32(await gCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }

        int maxUsed = Math.Max(maxP, Math.Max(maxT, maxA));
        return Math.Max(Math.Max(curFol, genCurrent + 1), maxUsed + 1);
    }

    private static async Task<FolioAvailabilityResult> CheckFolioAvailabilityInternalAsync(
        FbConnection conn,
        FbTransaction? tx,
        DatabaseCapabilities caps,
        int folioPesaje,
        int? ignoreTransitoFolio,
        int? ignorePesajeFolio,
        CancellationToken ct)
    {
        int nextFree = await PeekNextSafeFolioAsync(conn, tx, caps, ct).ConfigureAwait(false);
        if (folioPesaje <= 0)
        {
            return new FolioAvailabilityResult(false, FolioEstadoCheck.Invalido, folioPesaje, null, nextFree, $"El folio debe ser mayor a 0. Próximo sugerido: #{nextFree}.");
        }

        if (ignoreTransitoFolio != folioPesaje)
        {
            await using var trCmd = new FbCommand("SELECT FIRST 1 PATENTE FROM DBTRANSITO WHERE FLPESAJE = @FP", conn, tx);
            AddParam(trCmd, "@FP", folioPesaje);
            object? patTr = await trCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (patTr is not null and not DBNull)
            {
                string pat = Convert.ToString(patTr, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                return new FolioAvailabilityResult(false, FolioEstadoCheck.EnProcesoTransito, folioPesaje, pat, nextFree,
                    $"El folio #{folioPesaje} pertenece a un vehículo EN PROCESO en tránsito ({pat}). Se respeta sin sobreescribir; use #{nextFree}.");
            }
        }

        if (ignorePesajeFolio != folioPesaje)
        {
            await using var pCmd = new FbCommand("SELECT FIRST 1 PATENTE FROM DBPESAJES WHERE FLPESAJE = @FP", conn, tx);
            AddParam(pCmd, "@FP", folioPesaje);
            object? patP = await pCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (patP is not null and not DBNull)
            {
                string pat = Convert.ToString(patP, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                return new FolioAvailabilityResult(false, FolioEstadoCheck.HistoricoPesaje, folioPesaje, pat, nextFree,
                    $"El folio #{folioPesaje} ya existe en tickets cerrados ({pat}). Próximo folio libre: #{nextFree}.");
            }
        }

        await using var aCmd = new FbCommand("SELECT FIRST 1 PATENTE FROM DBTRAN_ANUL WHERE FLPESAJE = @FP", conn, tx);
        AddParam(aCmd, "@FP", folioPesaje);
        object? patA = await aCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (patA is not null and not DBNull)
        {
            string pat = Convert.ToString(patA, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            return new FolioAvailabilityResult(false, FolioEstadoCheck.AnuladoTransito, folioPesaje, pat, nextFree,
                $"El folio #{folioPesaje} pertenece a un tránsito anulado ({pat}). Próximo folio libre: #{nextFree}.");
        }

        return new FolioAvailabilityResult(true, FolioEstadoCheck.Libre, folioPesaje, null, nextFree, $"Folio #{folioPesaje} disponible y libre de conflictos.");
    }

    private static async Task AdvanceFolioPointersIfNeededAsync(FbConnection conn, FbTransaction tx, DatabaseCapabilities caps, int assignedFolio, CancellationToken ct)
    {
        if (caps.HasGenPesaje)
        {
            await using var gCmd = new FbCommand("SELECT GEN_ID(GEN_FLPESAJE, 0) FROM RDB$DATABASE", conn, tx);
            int curGen = Convert.ToInt32(await gCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (curGen < assignedFolio)
            {
                await Schema.FirebirdSchemaManager.SynchronizeGeneratorAsync(conn, tx, "GEN_FLPESAJE", assignedFolio, ct).ConfigureAwait(false);
            }
        }

        string readFolSql = caps.HasFoliosId ? "SELECT FIRST 1 FLPESAJE FROM DBFOLIOS WHERE ID = 1" : "SELECT FIRST 1 FLPESAJE FROM DBFOLIOS";
        await using var rfCmd = new FbCommand(readFolSql, conn, tx);
        object? rf = await rfCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        int curDbFol = rf is null or DBNull ? 1 : Convert.ToInt32(rf, CultureInfo.InvariantCulture);
        if (curDbFol <= assignedFolio)
        {
            string updSql = caps.HasFoliosId ? "UPDATE DBFOLIOS SET FLPESAJE = @NEXT WHERE ID = 1" : "UPDATE DBFOLIOS SET FLPESAJE = @NEXT";
            await using var folCmd = new FbCommand(updSql, conn, tx);
            AddParam(folCmd, "@NEXT", assignedFolio + 1);
            await folCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task<int> AllocateNextPesajeFolioAsync(FbConnection conn, FbTransaction tx, DatabaseCapabilities caps, CancellationToken ct)
    {
        int safeMin = await PeekNextSafeFolioAsync(conn, tx, caps, ct).ConfigureAwait(false);
        int folio = safeMin;
        if (caps.HasGenPesaje)
        {
            await using var genCmd = new FbCommand("SELECT NEXT VALUE FOR GEN_FLPESAJE FROM RDB$DATABASE", conn, tx);
            int genVal = Convert.ToInt32(await genCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
            folio = Math.Max(genVal, safeMin);
        }

        while (true)
        {
            FolioAvailabilityResult avail = await CheckFolioAvailabilityInternalAsync(conn, tx, caps, folio, null, null, ct).ConfigureAwait(false);
            if (avail.Disponible)
            {
                break;
            }
            folio = Math.Max(folio + 1, avail.ProximoFolioSugerido);
        }

        await AdvanceFolioPointersIfNeededAsync(conn, tx, caps, folio, ct).ConfigureAwait(false);
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
