using System.Globalization;
using FirebirdSql.Data.FirebirdClient;
using PesajeProCamion.Data.Infrastructure;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Validation;
using static PesajeProCamion.Data.Infrastructure.DatabaseSessionFactory;

namespace PesajeProCamion.Data.Repositories;

/// <summary>Registro de pesajes por eje MOP y consulta de tablas normativas Decreto 158/80 y curvas DEMO1.</summary>
public sealed class MopRepository
{
    private readonly DatabaseSessionFactory _db;

    public MopRepository(DatabaseSessionFactory db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task<(
        List<MopNormaCamion> Camiones,
        Dictionary<short, MopNormaTandem> Tandems,
        List<MopNormaEje> Ejes,
        List<MopNormaLargo> Largos)> GetNormativaCompletaAsync(CancellationToken ct = default)
    {
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        var camiones = new List<MopNormaCamion>();
        await using (var cmd = new FbCommand("SELECT TIPO, TANDEM01, TANDEM02, TANDEM03, TANDEM04, MAXIMO FROM NM_CAMIONES ORDER BY TIPO", conn))
        await using (FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                camiones.Add(new MopNormaCamion(
                    GetShort(r, "TIPO"),
                    GetShort(r, "TANDEM01"),
                    GetShort(r, "TANDEM02"),
                    GetShort(r, "TANDEM03"),
                    GetShort(r, "TANDEM04"),
                    r.IsDBNull(r.GetOrdinal("MAXIMO")) ? null : GetInt(r, "MAXIMO")));
            }
        }

        var tandems = new Dictionary<short, MopNormaTandem>();
        await using (var cmd = new FbCommand("SELECT TANDEM, NOMBRE, MAXIMO, TOLERANCIA FROM NM_TANDEMS ORDER BY TANDEM", conn))
        await using (FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                short id = GetShort(r, "TANDEM");
                tandems[id] = new MopNormaTandem(id, GetStr(r, "NOMBRE"), GetDouble(r, "MAXIMO"), GetInt(r, "TOLERANCIA"));
            }
        }

        var ejes = new List<MopNormaEje>();
        await using (var cmd = new FbCommand("SELECT TANDEM, EJE, TIPO_EJE, MAXIMO, TOLERANCIA, NOMEJE FROM NM_EJES ORDER BY TANDEM, EJE", conn))
        await using (FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                ejes.Add(new MopNormaEje(
                    GetShort(r, "TANDEM"),
                    GetShort(r, "EJE"),
                    GetShort(r, "TIPO_EJE"),
                    GetDouble(r, "MAXIMO"),
                    GetInt(r, "TOLERANCIA"),
                    GetStr(r, "NOMEJE")));
            }
        }

        var largos = new List<MopNormaLargo>();
        await using (var cmd = new FbCommand("SELECT DESDE, HASTA, MAXIMO FROM NM_LCAMION ORDER BY DESDE", conn))
        await using (FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                largos.Add(new MopNormaLargo(GetShort(r, "DESDE"), GetShort(r, "HASTA"), GetDouble(r, "MAXIMO")));
            }
        }

        return (camiones, tandems, ejes, largos);
    }

    public async Task<List<DemoMuestra>> GetDemoSamplesAsync(CancellationToken ct = default)
    {
        var list = new List<DemoMuestra>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("SELECT PESO, HORA FROM DEMO1 ORDER BY ID_MUESTRA", conn);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new DemoMuestra(GetDouble(r, "PESO"), GetStr(r, "HORA")));
        }
        return list;
    }

    public async Task<int> SavePesajeMopAsync(PesajeMop p, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(p);
        string pat = ChileanValidators.NormalizarPatente(p.Patente);
        return await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            if (p.FolioMop <= 0)
            {
                await using var gen = new FbCommand("SELECT NEXT VALUE FOR GEN_FLPESAJEMOP FROM RDB$DATABASE", conn, tx);
                p.FolioMop = Convert.ToInt32(await gen.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }

            await using var updBasc = new FbCommand("UPDATE DBBASCULAS SET FLPESAJEMOP = @NEXT WHERE BASCULA = @B", conn, tx);
            AddParam(updBasc, "@NEXT", p.FolioMop + 1);
            AddParam(updBasc, "@B", p.Bascula);
            await updBasc.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await using var updFol = new FbCommand("UPDATE DBFOLIOS SET FLPESAJEMOP = @NEXT WHERE ID = 1", conn, tx);
            AddParam(updFol, "@NEXT", p.FolioMop + 1);
            await updFol.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await using var cmd = new FbCommand("""
                INSERT INTO DBMOPPJES (
                    FLMOP, BASCULA, FLPESAJE, PATENTE, ACOPLADO, FECHA, HORA, TCARGA, TRANSP,
                    TMOP, LARGO, BRUTO, MAXIMO, SOBREPESO, GDESPACHO, CHOFER, ESTADO, SAVEDTOSERVER, USUARIO)
                VALUES (
                    @FM, @B, @FP, @P, @A, @F, @H, @TC, @TR,
                    @TM, @LA, @BR, @MA, @SP, @GD, @CH, @ES, @SV, @US)
                """, conn, tx);
            AddParam(cmd, "@FM", p.FolioMop);
            AddParam(cmd, "@B", p.Bascula);
            AddParam(cmd, "@FP", p.FolioPesaje);
            AddParam(cmd, "@P", pat);
            AddParam(cmd, "@A", ChileanValidators.NormalizarPatente(p.Acoplado));
            AddParam(cmd, "@F", p.Fecha.Date);
            AddParam(cmd, "@H", p.Hora);
            AddParam(cmd, "@TC", p.TipoCarga);
            AddParam(cmd, "@TR", p.Transportista.Trim());
            AddParam(cmd, "@TM", p.TipoMop);
            AddParam(cmd, "@LA", p.Largo);
            AddParam(cmd, "@BR", p.Bruto);
            AddParam(cmd, "@MA", p.Maximo);
            AddParam(cmd, "@SP", p.Sobrepeso);
            AddParam(cmd, "@GD", p.GuiaDespacho.Trim());
            AddParam(cmd, "@CH", p.Chofer.Trim());
            AddParam(cmd, "@ES", ((short)p.Estado).ToString(CultureInfo.InvariantCulture));
            AddParam(cmd, "@SV", p.SavedToServer);
            AddParam(cmd, "@US", p.UsuarioId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            foreach (PesajeMopDetalle d in p.Detalles)
            {
                await using var dcmd = new FbCommand("""
                    INSERT INTO DBMOPDETPJES (FLMOP, BASCULA, EJNRO, EJTPO, EJPES, EJLIM, TDNRO, TDTPO, TDPES, TDLIM, SAVEDTOSERVER)
                    VALUES (@FM, @B, @EN, @ET, @EP, @EL, @TN, @TT, @TP, @TL, @SV)
                    """, conn, tx);
                AddParam(dcmd, "@FM", p.FolioMop);
                AddParam(dcmd, "@B", p.Bascula);
                AddParam(dcmd, "@EN", d.EjeNumero);
                AddParam(dcmd, "@ET", d.EjeTipo);
                AddParam(dcmd, "@EP", d.EjePeso);
                AddParam(dcmd, "@EL", d.EjeLimite);
                AddParam(dcmd, "@TN", d.TandemNumero);
                AddParam(dcmd, "@TT", d.TandemTipo);
                AddParam(dcmd, "@TP", d.TandemPeso);
                AddParam(dcmd, "@TL", d.TandemLimite);
                AddParam(dcmd, "@SV", d.SavedToServer);
                await dcmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            return p.FolioMop;
        }, ct).ConfigureAwait(false);
    }

    public async Task<List<PesajeMop>> GetPesajesMopAsync(DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        var list = new List<PesajeMop>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("""
            SELECT * FROM DBMOPPJES
            WHERE FECHA >= @D AND FECHA <= @H
            ORDER BY FECHA DESC, FLMOP DESC
            """, conn);
        AddParam(cmd, "@D", desde.Date);
        AddParam(cmd, "@H", hasta.Date);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new PesajeMop
            {
                FolioMop = GetInt(r, "FLMOP"),
                Bascula = GetShort(r, "BASCULA", 1),
                FolioPesaje = GetInt(r, "FLPESAJE"),
                Patente = GetStr(r, "PATENTE"),
                Acoplado = GetStr(r, "ACOPLADO"),
                Fecha = GetDate(r, "FECHA") ?? DateTime.Today,
                Hora = GetStr(r, "HORA"),
                TipoCarga = GetShort(r, "TCARGA", 7),
                Transportista = GetStr(r, "TRANSP"),
                TipoMop = GetShort(r, "TMOP"),
                Largo = GetShort(r, "LARGO"),
                Bruto = GetInt(r, "BRUTO"),
                Maximo = GetInt(r, "MAXIMO"),
                Sobrepeso = GetInt(r, "SOBREPESO"),
                GuiaDespacho = GetStr(r, "GDESPACHO"),
                Chofer = GetStr(r, "CHOFER"),
                Estado = short.TryParse(GetStr(r, "ESTADO"), out short est) ? (EstadoNormaMop)est : EstadoNormaMop.DentroDeNorma,
                UsuarioId = GetShort(r, "USUARIO")
            });
        }

        foreach (PesajeMop mop in list)
        {
            await using var dcmd = new FbCommand("SELECT * FROM DBMOPDETPJES WHERE FLMOP = @FM AND BASCULA = @B ORDER BY EJNRO", conn);
            AddParam(dcmd, "@FM", mop.FolioMop);
            AddParam(dcmd, "@B", mop.Bascula);
            await using FbDataReader dr = await dcmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await dr.ReadAsync(ct).ConfigureAwait(false))
            {
                mop.Detalles.Add(new PesajeMopDetalle
                {
                    FolioMop = GetInt(dr, "FLMOP"),
                    Bascula = GetShort(dr, "BASCULA", 1),
                    EjeNumero = GetShort(dr, "EJNRO"),
                    EjeTipo = GetShort(dr, "EJTPO"),
                    EjePeso = GetInt(dr, "EJPES"),
                    EjeLimite = GetInt(dr, "EJLIM"),
                    TandemNumero = GetShort(dr, "TDNRO"),
                    TandemTipo = GetShort(dr, "TDTPO"),
                    TandemPeso = GetInt(dr, "TDPES"),
                    TandemLimite = GetInt(dr, "TDLIM")
                });
            }
        }
        return list;
    }
}
