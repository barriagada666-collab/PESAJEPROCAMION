using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;
using PesajeProCamion.Domain.Services;
using PesajeProCamion.Domain.Validation;
using Xunit;

namespace PesajeProCamion.Tests.Unit;

public sealed class DomainTests
{
    [Theory]
    [InlineData("1", "9", true)]
    [InlineData("17050099", "7", true)]
    [InlineData("16428039", "K", true)]
    [InlineData("16428039", "k", true)]
    [InlineData("9807507", "0", true)]
    [InlineData("96565810", "1", false)]
    [InlineData("", "9", false)]
    public void RutModulo11_ValidatesAccurately(string rut, string dv, bool expected)
    {
        Assert.Equal(expected, ChileanValidators.ValidarRut(rut, dv));
    }

    [Theory]
    [InlineData(" dlbt45 ", "DLBT45", true)]
    [InlineData("db89-50", "DB89-50", true)]
    [InlineData("ab cd 12", "ABCD12", true)]
    [InlineData("AB", "AB", false)]
    [InlineData("", "", false)]
    public void PatenteNormalizer_NormalizesAndValidates(string raw, string expectedNorm, bool expectedValid)
    {
        Assert.Equal(expectedNorm, ChileanValidators.NormalizarPatente(raw));
        Assert.Equal(expectedValid, ChileanValidators.EsPatenteValida(raw));
    }

    [Theory]
    [InlineData(30440, 14990, 15450)]
    [InlineData(15060, 33510, 18450)] // Caso Despacho donde entrada vacía < salida cargada
    [InlineData(10580, 16680, 6100)]
    public void CalcularNeto_AlwaysProducesPositiveSymmetricalDifference(int bruto, int tara, int expectedNeto)
    {
        Assert.Equal(expectedNeto, WeighingCalculator.CalcularNeto(bruto, tara));
    }

    [Fact]
    public void EvaluarTaraRegistrada_EnforcesExpirationAndZeroChecks()
    {
        var cfg = new ConfiguracionSistema { PorcentajeTara = 10 };
        var expiredTruck = new Camion
        {
            Patente = "ABCD12",
            Tara = 12000,
            TaraRegistrada = true,
            FechaTaraRegistrada = new DateTime(2026, 1, 1),
            DiasValidez = 30
        };
        ResultadoValidacionTara r1 = WeighingCalculator.EvaluarTaraRegistrada(expiredTruck, cfg, new DateTime(2026, 3, 1));
        Assert.False(r1.Permitida);

        var validTruck = new Camion
        {
            Patente = "ABCD12",
            Tara = 12000,
            TaraRegistrada = true,
            FechaTaraRegistrada = new DateTime(2026, 2, 15),
            DiasValidez = 30
        };
        ResultadoValidacionTara r2 = WeighingCalculator.EvaluarTaraRegistrada(validTruck, cfg, new DateTime(2026, 3, 1), 28000);
        Assert.True(r2.Permitida);
    }

    [Fact]
    public void EvaluarPesajeMop_DetectsAxleTandemAndTotalLimitsWithTolerances()
    {
        var camion510 = new MopNormaCamion(510, 9, 6, 6, 1, null);
        var tandems = new Dictionary<short, MopNormaTandem>
        {
            [9] = new(9, "RS", 7000, 500),
            [6] = new(6, "RD", 11000, 500),
            [1] = new(1, "2RD", 18000, 700)
        };
        List<MopNormaEje> ejes =
        [
            new(9, 1, 1, 7000, 500, "RS"),
            new(6, 1, 2, 11000, 500, "RD"),
            new(1, 1, 2, 11000, 500, "RD"),
            new(1, 2, 2, 11000, 500, "RD")
        ];
        List<MopNormaLargo> largos = [new(1, 13, 39000), new(13, 15, 42000), new(15, 90, 45000)];

        MopEvaluacionResultado ok = WeighingCalculator.EvaluarPesajeMop(camion510, 18, tandems, ejes, largos, [6500, 10500, 10000, 9000, 9000]);
        Assert.Equal(EstadoNormaMop.DentroDeNorma, ok.EstadoGeneral);
        Assert.Equal(45000, ok.LimiteMaximoCamionKg);

        MopEvaluacionResultado over = WeighingCalculator.EvaluarPesajeMop(camion510, 18, tandems, ejes, largos, [7800, 10500, 10500, 9000, 9000]);
        Assert.Equal(EstadoNormaMop.FueraDeNorma, over.EstadoGeneral);
        Assert.True(over.Ejes[0].FueraDeNorma);
    }

    [Fact]
    public void PasswordHasher_And_RbacPermissions_EnforceSecurity()
    {
        string hash = PasswordHasher.HashPassword("b1986");
        Assert.StartsWith("PBKDF2$100000$", hash, StringComparison.Ordinal);
        Assert.True(PasswordHasher.VerifyPassword("B1986", hash, out bool rehash1));
        Assert.False(rehash1);
        Assert.False(PasswordHasher.VerifyPassword("WRONG", hash, out _));

        // Plaintext legacy verification triggers automatic rehash
        Assert.True(PasswordHasher.VerifyPassword("patio", "PATIO", out bool rehash2));
        Assert.True(rehash2);

        string adminHash = PasswordHasher.HashPassword("Cambias0");
        Assert.True(PasswordHasher.VerifyPassword("Cambias0", adminHash, out _));
        Assert.True(PasswordHasher.VerifyPassword("CAMBIAS0", adminHash, out _));

        var consulta = new Usuario { Activo = true, Perfil = PerfilOperacion.Consulta, IngresoGestion = true, PuedeImprimir = true };
        Assert.False(PermissionService.EstaAutorizado(consulta, AccionAutorizacion.IngresarPesaje));
        Assert.True(PermissionService.EstaAutorizado(consulta, AccionAutorizacion.IngresarGestion));
        Assert.False(PermissionService.EstaAutorizado(consulta, AccionAutorizacion.EliminarRegistro));
        Assert.False(PermissionService.EstaAutorizado(consulta, AccionAutorizacion.ConfigurarVisorBascula));
    }

    [Fact]
    public void AnalyticsCalculator_FiltersAndSegmentsByPatenteProveedorClienteProductoAndDate()
    {
        List<Pesaje> dataset =
        [
            new() { FolioPesaje = 1, FechaSalida = new DateTime(2026, 11, 20), Patente = "CHHW80", Cliente = "C1", NombreCliente = "CLIENTE ALFA", Origen = "O1", NombreOrigen = "PROVEEDOR NORTE", Producto = "P1", NombreProducto = "HDPE", Bruto = 30000, Tara = 10000, Neto = 20000, Fardos = 51, TipoPesaje = TipoPesaje.Recepcion },
            new() { FolioPesaje = 2, FechaSalida = new DateTime(2026, 11, 21), Patente = "HKWK38", Cliente = "C2", NombreCliente = "CLIENTE BETA", Origen = "O2", NombreOrigen = "PROVEEDOR SUR", Producto = "P2", NombreProducto = "STRETCH", Bruto = 25000, Tara = 10000, Neto = 15000, Fardos = 29, TipoPesaje = TipoPesaje.Despacho },
            new() { FolioPesaje = 3, FechaSalida = new DateTime(2026, 11, 22), Patente = "CHHW80", Cliente = "C1", NombreCliente = "CLIENTE ALFA", Origen = "O1", NombreOrigen = "PROVEEDOR NORTE", Producto = "P1", NombreProducto = "HDPE", Bruto = 18000, Tara = 8000, Neto = 10000, Fardos = 99, Anulado = true }
        ];

        AnalyticsResult allActivos = AnalyticsCalculator.FilterAndAnalyze(dataset, new ReportFilterCriteria
        {
            Desde = new DateTime(2026, 11, 1),
            Hasta = new DateTime(2026, 11, 30),
            Estado = EstadoTicketFiltro.SoloActivos
        });
        Assert.Equal(2, allActivos.Kpis.TotalTickets);
        Assert.Equal(35000, allActivos.Kpis.TotalNetoKg);
        Assert.Equal(80, allActivos.Kpis.TotalFardos);
        Assert.Equal(35.0, allActivos.Kpis.ToneladasNetas);
        Assert.Equal(2, allActivos.PorCliente.Count);
        Assert.Equal(2, allActivos.PorProveedorOrigen.Count);
        Assert.Equal(2, allActivos.PorProducto.Count);
        Assert.Equal(2, allActivos.PorPatente.Count);

        AnalyticsResult slicePat = AnalyticsCalculator.FilterAndAnalyze(dataset, new ReportFilterCriteria
        {
            Patente = "CHHW80",
            ProveedorOrigen = "PROVEEDOR NORTE",
            Cliente = "CLIENTE ALFA",
            Producto = "HDPE",
            Estado = EstadoTicketFiltro.Todos
        });
        Assert.Equal(2, slicePat.Kpis.TotalTickets);
        Assert.Equal(1, slicePat.Kpis.TicketsActivos);
        Assert.Equal(1, slicePat.Kpis.TicketsAnulados);
        Assert.Equal(20000, slicePat.Kpis.TotalNetoKg);
        Assert.Equal(51, slicePat.Kpis.TotalFardos);
    }

    [Fact]
    public void TicketCustomizationHelper_BadgeFolioFardosAndSplit_AreConsistent()
    {
        Assert.Equal("Tipo de Pesaje: RECEPCIÓN DE CARGA", TicketCustomizationHelper.FormatTipoPesajeBadge(TipoPesaje.Recepcion));
        Assert.Equal("Tipo de Pesaje: DESPACHO DE CARGA", TicketCustomizationHelper.FormatTipoPesajeBadge(TipoPesaje.Despacho));
        Assert.Equal("Tipo de Pesaje: MOVIMIENTO INTERNO", TicketCustomizationHelper.FormatTipoPesajeBadge(TipoPesaje.MovimientoInterno));
        Assert.Contains("EN PROCESO", TicketCustomizationHelper.FormatTipoPesajeBadge(TipoPesaje.Recepcion, enProceso: true));
        Assert.Contains("ANULADO", TicketCustomizationHelper.FormatTipoPesajeBadge(TipoPesaje.Despacho, anulado: true));
        Assert.Equal("N° Folio: 002740", TicketCustomizationHelper.FormatFolioHeader(2740));
        Assert.Equal("N° Folio: 000001", TicketCustomizationHelper.FormatFolioHeader(1));

        (int b1, int t1, int n1, int b2, int t2, int n2) = TicketCustomizationHelper.ComputeSplitWeights(20, 10, 10, 6);
        Assert.Equal((16, 10, 6, 14, 10, 4), (b1, t1, n1, b2, t2, n2));

        (int f1, int f2) = TicketCustomizationHelper.ComputeSplitFardos(51, 6, 10);
        Assert.Equal(51, f1 + f2);
        Assert.Equal(31, f1);
        Assert.Equal(20, f2);

        Assert.Equal(51, TicketCustomizationHelper.ExtractFardosFromObservacion("GUIA 1333023 / P. ALTO / 51 FARDOS", null));
        string merged = TicketCustomizationHelper.MergeFardosNote("OBS BASE", 24, 120);
        Assert.Equal(24, TicketCustomizationHelper.ExtractFardosFromObservacion(null, merged));

        Assert.Throws<ArgumentOutOfRangeException>(() => TicketCustomizationHelper.ComputeSplitWeights(20, 10, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => TicketCustomizationHelper.ComputeSplitWeights(11, 10, 1, 1));
    }
}
