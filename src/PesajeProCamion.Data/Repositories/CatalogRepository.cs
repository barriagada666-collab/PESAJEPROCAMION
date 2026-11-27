using FirebirdSql.Data.FirebirdClient;
using PesajeProCamion.Data.Infrastructure;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Validation;
using static PesajeProCamion.Data.Infrastructure.DatabaseSessionFactory;

namespace PesajeProCamion.Data.Repositories;

/// <summary>Repositorio unificado para catálogos maestros, báscula, hardware y configuración general.</summary>
public sealed class CatalogRepository
{
    private readonly DatabaseSessionFactory _db;

    public CatalogRepository(DatabaseSessionFactory db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task<BasculaInfo> GetBasculaAsync(short id = 1, CancellationToken ct = default)
    {
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("SELECT BASCULA, DESCRIP, FLPESAJEMOP, NROINST, RUT, NOMBRE, DIRECCION, FOLIO, USUAING, USUAMOD FROM DBBASCULAS WHERE BASCULA = @ID", conn);
        AddParam(cmd, "@ID", id);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return new BasculaInfo { Bascula = id };
        }
        return new BasculaInfo
        {
            Bascula = GetShort(r, "BASCULA", id),
            Descripcion = GetStr(r, "DESCRIP"),
            FolioPesajeMop = GetInt(r, "FLPESAJEMOP", 1),
            NroInstalacion = GetStr(r, "NROINST"),
            Rut = GetStr(r, "RUT"),
            NombreEmpresa = GetStr(r, "NOMBRE"),
            Direccion = GetStr(r, "DIRECCION"),
            Folio = r.IsDBNull(r.GetOrdinal("FOLIO")) ? null : GetInt(r, "FOLIO"),
            UsuarioIngreso = GetShort(r, "USUAING"),
            UsuarioModificacion = GetShort(r, "USUAMOD")
        };
    }

    public async Task SaveBasculaAsync(BasculaInfo info, short usuarioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand("""
                UPDATE OR INSERT INTO DBBASCULAS (BASCULA, DESCRIP, FLPESAJEMOP, NROINST, RUT, NOMBRE, DIRECCION, FOLIO, USUAING, USUAMOD)
                VALUES (@B, @D, @FM, @NI, @RUT, @NOM, @DIR, @FOL, @UI, @UM)
                MATCHING (BASCULA)
                """, conn, tx);
            AddParam(cmd, "@B", info.Bascula);
            AddParam(cmd, "@D", info.Descripcion);
            AddParam(cmd, "@FM", info.FolioPesajeMop);
            AddParam(cmd, "@NI", info.NroInstalacion);
            AddParam(cmd, "@RUT", info.Rut);
            AddParam(cmd, "@NOM", info.NombreEmpresa);
            AddParam(cmd, "@DIR", info.Direccion);
            AddParam(cmd, "@FOL", info.Folio);
            AddParam(cmd, "@UI", info.UsuarioIngreso > 0 ? info.UsuarioIngreso : usuarioId);
            AddParam(cmd, "@UM", usuarioId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<ConfiguracionSistema> GetConfigAsync(CancellationToken ct = default)
    {
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("SELECT FIRST 1 * FROM DBCONFIG ORDER BY ID", conn);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var cfg = new ConfiguracionSistema();
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return cfg;
        }
        cfg.FechaInstalacion = GetDate(r, "FECHAINST");
        cfg.PorcentajeTara = GetInt(r, "NPORCTARA", 10);
        cfg.NroCopiasTicket = GetInt(r, "NROCOPTCK", 1);
        cfg.BasculaEnCeroActivo = GetFlag(r, "BASCENCERO", true);
        cfg.CeroBasculaKg = GetInt(r, "CEROBASC", 50);
        cfg.DinamicoActivo = GetFlag(r, "DINACTIVO", true);
        cfg.EstableEstatico = GetNullableStr(r, "STABLEESTATICO");
        cfg.ConfirmarImpresionTicket = GetFlag(r, "CONFIMPTICKET", true);
        cfg.SistemaOperativo = GetInt(r, "SISTOP", 1);
        cfg.PrintIndexTicket = GetInt(r, "PRINTINDEX");
        cfg.PrintIndexInforme = GetInt(r, "PRINTINDEX2");
        cfg.Titulo1 = GetNullableStr(r, "TITLE1");
        cfg.Titulo2 = GetNullableStr(r, "TITLE2");
        cfg.Titulo3 = GetNullableStr(r, "TITLE3");
        cfg.SeguridadActiva = GetNullableStr(r, "SEGACTIVA");
        cfg.VmopUnder = GetInt(r, "VMOPUNDER", 350);
        cfg.VmopOver = GetInt(r, "VMOPOVER", 350);
        cfg.VmopPreSample = GetInt(r, "VMOPPRESAMPLE", 800);
        cfg.VmopMinSample = GetInt(r, "VMOPMINSAMPLE", 500);
        cfg.VmopLowSamples = GetInt(r, "VMOPLOWSAMPLES", 5);
        cfg.VmopHiSamples = GetInt(r, "VMOPHISAMPLES", 12);
        cfg.VmopInsideSamples = GetInt(r, "VMOPINSIDESAMPLES");
        cfg.VmopCutLeft = GetInt(r, "VMOPCUTLEFT", 30);
        cfg.VmopCutRight = GetInt(r, "VMOPCUTRIGHT", 30);
        cfg.VmopMaxNodes = GetInt(r, "VMOPMAXNODES");
        cfg.VLimit = GetInt(r, "VLIMIT", 1);
        cfg.VDelta = GetInt(r, "VDELTA", 10);
        cfg.VisorStx = GetInt(r, "VISOR_STX", 2);
        cfg.VisorPosData = GetInt(r, "VISOR_POSDATA", 3);
        cfg.VisorLenData = GetInt(r, "VISOR_LENDATA", 5);
        cfg.NroFiltros = Math.Max(1, GetInt(r, "VNRO_FILTROS", 80));
        cfg.DstUpFactor = GetDouble(r, "DSTDUPFACTOR", 1.0);
        cfg.DstDownFactor = GetDouble(r, "DSTDDOWNFACTOR", 1.0);
        for (int i = 0; i < 7; i++)
        {
            cfg.Offsets[i] = GetInt(r, $"OFFSET{i + 1}");
            cfg.Calibraciones[i] = GetInt(r, $"CALIB{i + 1}");
        }
        return cfg;
    }

    public async Task SaveConfigAsync(ConfiguracionSistema cfg, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand("""
                UPDATE OR INSERT INTO DBCONFIG (
                    ID, FECHAINST, NPORCTARA, NROCOPTCK, BASCENCERO, CEROBASC, DINACTIVO, CONFIMPTICKET,
                    SISTOP, PRINTINDEX, PRINTINDEX2, VMOPUNDER, VMOPOVER, VMOPCUTLEFT, VMOPCUTRIGHT,
                    VISOR_STX, VISOR_POSDATA, VISOR_LENDATA, VNRO_FILTROS, DSTDUPFACTOR, DSTDDOWNFACTOR,
                    OFFSET1, OFFSET2, OFFSET3, OFFSET4, OFFSET5, OFFSET6, OFFSET7,
                    CALIB1, CALIB2, CALIB3, CALIB4, CALIB5, CALIB6, CALIB7)
                VALUES (
                    1, @FI, @NPT, @NRC, @BEC, @CB, @DA, @CIT,
                    @SO, @P1, @P2, @VU, @VO, @VCL, @VCR,
                    @VSTX, @VPOS, @VLEN, @VNF, @DUF, @DDF,
                    @O1, @O2, @O3, @O4, @O5, @O6, @O7,
                    @C1, @C2, @C3, @C4, @C5, @C6, @C7)
                MATCHING (ID)
                """, conn, tx);
            AddParam(cmd, "@FI", cfg.FechaInstalacion ?? DateTime.Today);
            AddParam(cmd, "@NPT", cfg.PorcentajeTara);
            AddParam(cmd, "@NRC", cfg.NroCopiasTicket);
            AddParam(cmd, "@BEC", cfg.BasculaEnCeroActivo ? "1" : "0");
            AddParam(cmd, "@CB", cfg.CeroBasculaKg);
            AddParam(cmd, "@DA", cfg.DinamicoActivo ? "1" : "0");
            AddParam(cmd, "@CIT", cfg.ConfirmarImpresionTicket ? "1" : "0");
            AddParam(cmd, "@SO", cfg.SistemaOperativo);
            AddParam(cmd, "@P1", cfg.PrintIndexTicket);
            AddParam(cmd, "@P2", cfg.PrintIndexInforme);
            AddParam(cmd, "@VU", cfg.VmopUnder);
            AddParam(cmd, "@VO", cfg.VmopOver);
            AddParam(cmd, "@VCL", cfg.VmopCutLeft);
            AddParam(cmd, "@VCR", cfg.VmopCutRight);
            AddParam(cmd, "@VSTX", cfg.VisorStx);
            AddParam(cmd, "@VPOS", cfg.VisorPosData);
            AddParam(cmd, "@VLEN", cfg.VisorLenData);
            AddParam(cmd, "@VNF", Math.Max(1, cfg.NroFiltros));
            AddParam(cmd, "@DUF", cfg.DstUpFactor);
            AddParam(cmd, "@DDF", cfg.DstDownFactor);
            for (int i = 0; i < 7; i++)
            {
                AddParam(cmd, $"@O{i + 1}", cfg.Offsets[i]);
                AddParam(cmd, $"@C{i + 1}", cfg.Calibraciones[i]);
            }
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<FoliosSistema> GetFoliosAsync(CancellationToken ct = default)
    {
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("SELECT FIRST 1 FLPESAJE, FLPESAJEMOP, FLUSUARIO, FLGDESPACHO FROM DBFOLIOS ORDER BY ID", conn);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return new FoliosSistema();
        }
        return new FoliosSistema
        {
            FolioPesaje = GetInt(r, "FLPESAJE", 1),
            FolioPesajeMop = GetInt(r, "FLPESAJEMOP", 1),
            FolioUsuario = GetInt(r, "FLUSUARIO", 1),
            FolioGuiaDespacho = r.IsDBNull(r.GetOrdinal("FLGDESPACHO")) ? null : GetInt(r, "FLGDESPACHO")
        };
    }

    public async Task SaveFoliosAsync(FoliosSistema folios, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(folios);
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand("""
                UPDATE OR INSERT INTO DBFOLIOS (ID, FLPESAJE, FLPESAJEMOP, FLUSUARIO, FLGDESPACHO)
                VALUES (1, @FP, @FM, @FU, @GD)
                MATCHING (ID)
                """, conn, tx);
            AddParam(cmd, "@FP", folios.FolioPesaje);
            AddParam(cmd, "@FM", folios.FolioPesajeMop);
            AddParam(cmd, "@FU", folios.FolioUsuario);
            AddParam(cmd, "@GD", folios.FolioGuiaDespacho ?? 1);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await Schema.FirebirdSchemaManager.SynchronizeGeneratorAsync(conn, tx, "GEN_FLPESAJE", Math.Max(0, folios.FolioPesaje - 1), ct).ConfigureAwait(false);
            await Schema.FirebirdSchemaManager.SynchronizeGeneratorAsync(conn, tx, "GEN_FLPESAJEMOP", Math.Max(0, folios.FolioPesajeMop - 1), ct).ConfigureAwait(false);
            await Schema.FirebirdSchemaManager.SynchronizeGeneratorAsync(conn, tx, "GEN_FLUSUARIO", Math.Max(0, folios.FolioUsuario - 1), ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<HardwareSetup> GetHardwareSetupAsync(string device = "BASCULA", CancellationToken ct = default)
    {
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("SELECT DEVICE, COMNUMBER, BAUDRATE, PARITY, DATABITS, STOPBITS, PRINTER FROM DBHWSETUP WHERE DEVICE = @D", conn);
        AddParam(cmd, "@D", device);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return new HardwareSetup { Device = device };
        }
        return new HardwareSetup
        {
            Device = GetStr(r, "DEVICE"),
            ComNumber = GetShort(r, "COMNUMBER", 1),
            BaudRateIndex = GetInt(r, "BAUDRATE"),
            ParityIndex = GetInt(r, "PARITY"),
            DataBitsIndex = GetInt(r, "DATABITS", 1),
            StopBitsIndex = r.IsDBNull(r.GetOrdinal("STOPBITS")) ? null : GetInt(r, "STOPBITS"),
            Printer = GetNullableStr(r, "PRINTER")
        };
    }

    public async Task SaveHardwareSetupAsync(HardwareSetup hw, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(hw);
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand("""
                UPDATE OR INSERT INTO DBHWSETUP (DEVICE, COMNUMBER, BAUDRATE, PARITY, DATABITS, STOPBITS, PRINTER)
                VALUES (@D, @C, @B, @P, @DB, @SB, @PR)
                MATCHING (DEVICE)
                """, conn, tx);
            AddParam(cmd, "@D", hw.Device);
            AddParam(cmd, "@C", hw.ComNumber);
            AddParam(cmd, "@B", hw.BaudRateIndex);
            AddParam(cmd, "@P", hw.ParityIndex);
            AddParam(cmd, "@DB", hw.DataBitsIndex);
            AddParam(cmd, "@SB", hw.StopBitsIndex);
            AddParam(cmd, "@PR", hw.Printer);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    // --- Clientes ---
    public async Task<List<Cliente>> GetClientesAsync(CancellationToken ct = default)
    {
        var list = new List<Cliente>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("SELECT RUT, DIGITO, NOMBRE, DIRECCION, GIRO, COMUNA, CIUDAD, TELEFONO, FAX, CASILLA, USUAING, USUAMOD FROM DBCLIENTES ORDER BY NOMBRE, RUT", conn);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(MapCliente(r));
        }
        return list;
    }

    public async Task<Cliente?> GetClienteAsync(string rut, CancellationToken ct = default)
    {
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("SELECT RUT, DIGITO, NOMBRE, DIRECCION, GIRO, COMUNA, CIUDAD, TELEFONO, FAX, CASILLA, USUAING, USUAMOD FROM DBCLIENTES WHERE TRIM(RUT) = @R", conn);
        AddParam(cmd, "@R", rut.Trim());
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false) ? MapCliente(r) : null;
    }

    public async Task SaveClienteAsync(Cliente c, short usuarioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand("""
                UPDATE OR INSERT INTO DBCLIENTES (RUT, DIGITO, NOMBRE, DIRECCION, GIRO, COMUNA, CIUDAD, TELEFONO, FAX, CASILLA, USUAING, USUAMOD)
                VALUES (@R, @DV, @N, @D, @G, @CO, @CI, @TE, @FA, @CA, @UI, @UM)
                MATCHING (RUT)
                """, conn, tx);
            AddParam(cmd, "@R", c.Rut.Trim());
            AddParam(cmd, "@DV", c.Digito.Trim().ToUpperInvariant());
            AddParam(cmd, "@N", c.Nombre.Trim());
            AddParam(cmd, "@D", c.Direccion.Trim());
            AddParam(cmd, "@G", c.Giro.Trim());
            AddParam(cmd, "@CO", c.Comuna.Trim());
            AddParam(cmd, "@CI", c.Ciudad.Trim());
            AddParam(cmd, "@TE", c.Telefono.Trim());
            AddParam(cmd, "@FA", c.Fax.Trim());
            AddParam(cmd, "@CA", c.Casilla.Trim());
            AddParam(cmd, "@UI", c.UsuarioIngreso > 0 ? c.UsuarioIngreso : usuarioId);
            AddParam(cmd, "@UM", usuarioId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public Task DeleteClienteAsync(string rut, CancellationToken ct = default)
        => DeleteByKeyAsync("DBCLIENTES", "RUT", rut.Trim(), ct);

    // --- Productos / Origenes / Destinos ---
    public async Task<List<Producto>> GetProductosAsync(CancellationToken ct = default)
    {
        var list = new List<Producto>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("SELECT PRODUCTO, DESCRIP, USUAING, USUAMOD FROM DBPRODUCTOS ORDER BY DESCRIP, PRODUCTO", conn);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new Producto { Codigo = GetStr(r, "PRODUCTO"), Descripcion = GetStr(r, "DESCRIP"), UsuarioIngreso = GetShort(r, "USUAING"), UsuarioModificacion = GetShort(r, "USUAMOD") });
        }
        return list;
    }

    public Task SaveProductoAsync(Producto p, short usuarioId, CancellationToken ct = default)
        => SaveCodeDescripAsync("DBPRODUCTOS", "PRODUCTO", p.Codigo.Trim(), p.Descripcion.Trim(), p.UsuarioIngreso, usuarioId, ct);

    public Task DeleteProductoAsync(string codigo, CancellationToken ct = default)
        => DeleteByKeyAsync("DBPRODUCTOS", "PRODUCTO", codigo.Trim(), ct);

    public async Task<List<Origen>> GetOrigenesAsync(CancellationToken ct = default)
    {
        var list = new List<Origen>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("SELECT ORIGEN, DESCRIP, USUAING, USUAMOD FROM DBORIGEN ORDER BY DESCRIP, ORIGEN", conn);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new Origen { Codigo = GetStr(r, "ORIGEN"), Descripcion = GetStr(r, "DESCRIP"), UsuarioIngreso = GetShort(r, "USUAING"), UsuarioModificacion = GetShort(r, "USUAMOD") });
        }
        return list;
    }

    public Task SaveOrigenAsync(Origen o, short usuarioId, CancellationToken ct = default)
        => SaveCodeDescripAsync("DBORIGEN", "ORIGEN", o.Codigo.Trim(), o.Descripcion.Trim(), o.UsuarioIngreso, usuarioId, ct);

    public Task DeleteOrigenAsync(string codigo, CancellationToken ct = default)
        => DeleteByKeyAsync("DBORIGEN", "ORIGEN", codigo.Trim(), ct);

    public async Task<List<Destino>> GetDestinosAsync(CancellationToken ct = default)
    {
        var list = new List<Destino>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("SELECT DESTINO, DESCRIP, USUAING, USUAMOD FROM DBDESTINOS ORDER BY DESCRIP, DESTINO", conn);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new Destino { Codigo = GetStr(r, "DESTINO"), Descripcion = GetStr(r, "DESCRIP"), UsuarioIngreso = GetShort(r, "USUAING"), UsuarioModificacion = GetShort(r, "USUAMOD") });
        }
        return list;
    }

    public Task SaveDestinoAsync(Destino d, short usuarioId, CancellationToken ct = default)
        => SaveCodeDescripAsync("DBDESTINOS", "DESTINO", d.Codigo.Trim(), d.Descripcion.Trim(), d.UsuarioIngreso, usuarioId, ct);

    public Task DeleteDestinoAsync(string codigo, CancellationToken ct = default)
        => DeleteByKeyAsync("DBDESTINOS", "DESTINO", codigo.Trim(), ct);

    // --- Choferes ---
    public async Task<List<Chofer>> GetChoferesAsync(CancellationToken ct = default)
    {
        var list = new List<Chofer>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("SELECT CHOFER, DIGITO, NOMBRE, USUAING, USUAMOD FROM DBCHOFERES ORDER BY NOMBRE, CHOFER", conn);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new Chofer { Rut = GetStr(r, "CHOFER"), Digito = GetStr(r, "DIGITO"), Nombre = GetStr(r, "NOMBRE"), UsuarioIngreso = GetShort(r, "USUAING"), UsuarioModificacion = GetShort(r, "USUAMOD") });
        }
        return list;
    }

    public async Task SaveChoferAsync(Chofer c, short usuarioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand("""
                UPDATE OR INSERT INTO DBCHOFERES (CHOFER, DIGITO, NOMBRE, USUAING, USUAMOD)
                VALUES (@C, @DV, @N, @UI, @UM)
                MATCHING (CHOFER)
                """, conn, tx);
            AddParam(cmd, "@C", c.Rut.Trim());
            AddParam(cmd, "@DV", c.Digito.Trim().ToUpperInvariant());
            AddParam(cmd, "@N", c.Nombre.Trim());
            AddParam(cmd, "@UI", c.UsuarioIngreso > 0 ? c.UsuarioIngreso : usuarioId);
            AddParam(cmd, "@UM", usuarioId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public Task DeleteChoferAsync(string rut, CancellationToken ct = default)
        => DeleteByKeyAsync("DBCHOFERES", "CHOFER", rut.Trim(), ct);

    // --- Transportistas ---
    public async Task<List<Transportista>> GetTransportistasAsync(CancellationToken ct = default)
    {
        var list = new List<Transportista>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("SELECT TRANSP, DIGITO, NOMBRE, DIRECCION, CIUDAD, TELEFONO, FAX, USUAING, USUAMOD FROM DBTRANSP ORDER BY NOMBRE, TRANSP", conn);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new Transportista
            {
                Rut = GetStr(r, "TRANSP"),
                Digito = GetStr(r, "DIGITO"),
                Nombre = GetStr(r, "NOMBRE"),
                Direccion = GetStr(r, "DIRECCION"),
                Ciudad = GetStr(r, "CIUDAD"),
                Telefono = GetStr(r, "TELEFONO"),
                Fax = GetStr(r, "FAX"),
                UsuarioIngreso = GetShort(r, "USUAING"),
                UsuarioModificacion = GetShort(r, "USUAMOD")
            });
        }
        return list;
    }

    public async Task SaveTransportistaAsync(Transportista t, short usuarioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(t);
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand("""
                UPDATE OR INSERT INTO DBTRANSP (TRANSP, DIGITO, NOMBRE, DIRECCION, CIUDAD, TELEFONO, FAX, USUAING, USUAMOD)
                VALUES (@T, @DV, @N, @D, @CI, @TE, @FA, @UI, @UM)
                MATCHING (TRANSP)
                """, conn, tx);
            AddParam(cmd, "@T", t.Rut.Trim());
            AddParam(cmd, "@DV", t.Digito.Trim().ToUpperInvariant());
            AddParam(cmd, "@N", t.Nombre.Trim());
            AddParam(cmd, "@D", t.Direccion.Trim());
            AddParam(cmd, "@CI", t.Ciudad.Trim());
            AddParam(cmd, "@TE", t.Telefono.Trim());
            AddParam(cmd, "@FA", t.Fax.Trim());
            AddParam(cmd, "@UI", t.UsuarioIngreso > 0 ? t.UsuarioIngreso : usuarioId);
            AddParam(cmd, "@UM", usuarioId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public Task DeleteTransportistaAsync(string rut, CancellationToken ct = default)
        => DeleteByKeyAsync("DBTRANSP", "TRANSP", rut.Trim(), ct);

    // --- Camiones ---
    public async Task<List<Camion>> GetCamionesAsync(string? filtroPatente = null, CancellationToken ct = default)
    {
        var list = new List<Camion>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        string sql = string.IsNullOrWhiteSpace(filtroPatente)
            ? "SELECT PATENTE, ACOPLADO, TRANSP, CHOFER, TMOP, LARGO, TARA, FTARA, HTARA, TARAREG, FTARAREG, NDIAS, USUAING, USUAMOD FROM DBCAMIONES ORDER BY PATENTE"
            : "SELECT PATENTE, ACOPLADO, TRANSP, CHOFER, TMOP, LARGO, TARA, FTARA, HTARA, TARAREG, FTARAREG, NDIAS, USUAING, USUAMOD FROM DBCAMIONES WHERE UPPER(PATENTE) CONTAINING @F ORDER BY PATENTE";
        await using var cmd = new FbCommand(sql, conn);
        if (!string.IsNullOrWhiteSpace(filtroPatente))
        {
            AddParam(cmd, "@F", ChileanValidators.NormalizarPatente(filtroPatente));
        }
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(MapCamion(r));
        }
        return list;
    }

    public async Task<Camion?> GetCamionAsync(string patente, CancellationToken ct = default)
    {
        string pat = ChileanValidators.NormalizarPatente(patente);
        if (pat.Length == 0)
        {
            return null;
        }
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("SELECT PATENTE, ACOPLADO, TRANSP, CHOFER, TMOP, LARGO, TARA, FTARA, HTARA, TARAREG, FTARAREG, NDIAS, USUAING, USUAMOD FROM DBCAMIONES WHERE PATENTE = @P", conn);
        AddParam(cmd, "@P", pat);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false) ? MapCamion(r) : null;
    }

    public async Task SaveCamionAsync(Camion c, short usuarioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        string pat = ChileanValidators.NormalizarPatente(c.Patente);
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand("""
                UPDATE OR INSERT INTO DBCAMIONES (PATENTE, ACOPLADO, TRANSP, CHOFER, TMOP, LARGO, TARA, FTARA, HTARA, TARAREG, FTARAREG, NDIAS, USUAING, USUAMOD)
                VALUES (@P, @AC, @TR, @CH, @TM, @LA, @TA, @FT, @HT, @TRG, @FTRG, @ND, @UI, @UM)
                MATCHING (PATENTE)
                """, conn, tx);
            AddParam(cmd, "@P", pat);
            AddParam(cmd, "@AC", ChileanValidators.NormalizarPatente(c.Acoplado));
            AddParam(cmd, "@TR", c.Transportista.Trim());
            AddParam(cmd, "@CH", c.Chofer.Trim());
            AddParam(cmd, "@TM", c.TipoMop);
            AddParam(cmd, "@LA", c.Largo);
            AddParam(cmd, "@TA", c.Tara);
            AddParam(cmd, "@FT", c.FechaTara);
            AddParam(cmd, "@HT", c.HoraTara);
            AddParam(cmd, "@TRG", c.TaraRegistrada ? "1" : "0");
            AddParam(cmd, "@FTRG", c.FechaTaraRegistrada);
            AddParam(cmd, "@ND", c.DiasValidez);
            AddParam(cmd, "@UI", c.UsuarioIngreso > 0 ? c.UsuarioIngreso : usuarioId);
            AddParam(cmd, "@UM", usuarioId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public Task DeleteCamionAsync(string patente, CancellationToken ct = default)
        => DeleteByKeyAsync("DBCAMIONES", "PATENTE", ChileanValidators.NormalizarPatente(patente), ct);

    public async Task<List<TipoCarga>> GetTiposCargaAsync(CancellationToken ct = default)
    {
        var list = new List<TipoCarga>();
        await using FbConnection conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = new FbCommand("SELECT TCARGA, DESCRIP, TCNORMAL, USUAING, USUAMOD FROM DBTCARGA ORDER BY TCARGA", conn);
        await using FbDataReader r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new TipoCarga
            {
                Codigo = GetShort(r, "TCARGA"),
                Descripcion = GetStr(r, "DESCRIP"),
                Normal = GetShort(r, "TCNORMAL"),
                UsuarioIngreso = GetShort(r, "USUAING"),
                UsuarioModificacion = GetShort(r, "USUAMOD")
            });
        }
        return list;
    }

    public async Task SaveTipoCargaNormalAsync(short tCarga, CancellationToken ct = default)
    {
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd1 = new FbCommand("UPDATE DBTCARGA SET TCNORMAL = 0", conn, tx);
            await cmd1.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await using var cmd2 = new FbCommand("UPDATE DBTCARGA SET TCNORMAL = 1 WHERE TCARGA = @T", conn, tx);
            AddParam(cmd2, "@T", tCarga);
            await cmd2.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task SaveCodeDescripAsync(string table, string keyCol, string key, string descrip, short usuaIng, short usuarioId, CancellationToken ct)
    {
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand($"""
                UPDATE OR INSERT INTO {table} ({keyCol}, DESCRIP, USUAING, USUAMOD)
                VALUES (@K, @D, @UI, @UM)
                MATCHING ({keyCol})
                """, conn, tx);
            AddParam(cmd, "@K", key);
            AddParam(cmd, "@D", descrip);
            AddParam(cmd, "@UI", usuaIng > 0 ? usuaIng : usuarioId);
            AddParam(cmd, "@UM", usuarioId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task DeleteByKeyAsync(string table, string keyCol, string keyValue, CancellationToken ct)
    {
        await _db.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await using var cmd = new FbCommand($"DELETE FROM {table} WHERE {keyCol} = @K", conn, tx);
            AddParam(cmd, "@K", keyValue);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private static Cliente MapCliente(FbDataReader r) => new()
    {
        Rut = GetStr(r, "RUT"),
        Digito = GetStr(r, "DIGITO"),
        Nombre = GetStr(r, "NOMBRE"),
        Direccion = GetStr(r, "DIRECCION"),
        Giro = GetStr(r, "GIRO"),
        Comuna = GetStr(r, "COMUNA"),
        Ciudad = GetStr(r, "CIUDAD"),
        Telefono = GetStr(r, "TELEFONO"),
        Fax = GetStr(r, "FAX"),
        Casilla = GetStr(r, "CASILLA"),
        UsuarioIngreso = GetShort(r, "USUAING"),
        UsuarioModificacion = GetShort(r, "USUAMOD")
    };

    private static Camion MapCamion(FbDataReader r) => new()
    {
        Patente = GetStr(r, "PATENTE"),
        Acoplado = GetStr(r, "ACOPLADO"),
        Transportista = GetStr(r, "TRANSP"),
        Chofer = GetStr(r, "CHOFER"),
        TipoMop = GetShort(r, "TMOP"),
        Largo = GetShort(r, "LARGO"),
        Tara = GetInt(r, "TARA"),
        FechaTara = GetDate(r, "FTARA"),
        HoraTara = GetStr(r, "HTARA"),
        TaraRegistrada = GetFlag(r, "TARAREG"),
        FechaTaraRegistrada = GetDate(r, "FTARAREG"),
        DiasValidez = GetShort(r, "NDIAS"),
        UsuarioIngreso = GetShort(r, "USUAING"),
        UsuarioModificacion = GetShort(r, "USUAMOD")
    };
}
