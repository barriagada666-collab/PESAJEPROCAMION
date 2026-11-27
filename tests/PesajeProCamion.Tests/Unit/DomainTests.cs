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

        var consulta = new Usuario { Activo = true, Perfil = PerfilOperacion.Consulta, IngresoGestion = true, PuedeImprimir = true };
        Assert.False(PermissionService.EstaAutorizado(consulta, AccionAutorizacion.IngresarPesaje));
        Assert.True(PermissionService.EstaAutorizado(consulta, AccionAutorizacion.IngresarGestion));
        Assert.False(PermissionService.EstaAutorizado(consulta, AccionAutorizacion.EliminarRegistro));
        Assert.False(PermissionService.EstaAutorizado(consulta, AccionAutorizacion.ConfigurarVisorBascula));
    }
}
