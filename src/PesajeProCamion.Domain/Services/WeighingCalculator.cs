using PesajeProCamion.Domain.Entities;

namespace PesajeProCamion.Domain.Services;

public sealed record ResultadoValidacionTara(bool Permitida, string Motivo);

/// <summary>Cálculos centrales de pesaje comercial, validez de tara registrada y normativa MOP de pesos por eje.</summary>
public static class WeighingCalculator
{
    private static readonly DateTime SentinelNullDate = new(1899, 12, 30);

    /// <summary>Calcula el peso neto simétrico evitando valores negativos cuando se pesa primero tara y luego bruto.</summary>
    public static int CalcularNeto(int pesoBruto, int pesoTara)
        => Math.Abs(pesoBruto - pesoTara);

    /// <summary>Ordena correctamente peso bruto (mayor) y tara (menor) cuando corresponden a primer y segundo pesaje.</summary>
    public static (int Bruto, int Tara, int Neto) ResolverBrutoTaraNeto(int pesoA, int pesoB, TipoPesaje tipoPesaje)
    {
        if (pesoA <= 0 || pesoB <= 0)
        {
            return (pesoA, pesoB, 0);
        }

        // En recepción/despacho el bruto corresponde siempre al mayor peso y la tara al menor
        return (Math.Max(pesoA, pesoB), Math.Min(pesoA, pesoB), Math.Abs(pesoA - pesoB));
    }

    /// <summary>Verifica si un camión tiene una tara registrada vigente y utilizable.</summary>
    public static ResultadoValidacionTara EvaluarTaraRegistrada(
        Camion? camion,
        ConfiguracionSistema config,
        DateTime fechaActual,
        int? nuevoBrutoOpcional = null)
    {
        if (camion is null)
        {
            return new ResultadoValidacionTara(false, "Camión no encontrado en catálogo.");
        }

        if (camion.Tara <= 0)
        {
            return new ResultadoValidacionTara(false, "La tara del camión está en cero.");
        }

        if (!camion.TaraRegistrada)
        {
            return new ResultadoValidacionTara(false, "El camión no tiene habilitada la opción de tara registrada.");
        }

        if (camion.FechaTaraRegistrada is { } fReg && fReg.Date != SentinelNullDate && camion.DiasValidez > 0)
        {
            double diasTranscurridos = (fechaActual.Date - fReg.Date).TotalDays;
            if (diasTranscurridos > camion.DiasValidez)
            {
                return new ResultadoValidacionTara(
                    false,
                    $"Tara registrada vencida ({diasTranscurridos:0} días > {camion.DiasValidez} días permitidos).");
            }
        }

        if (nuevoBrutoOpcional is > 0 && nuevoBrutoOpcional.Value <= camion.Tara)
        {
            return new ResultadoValidacionTara(false, "El peso bruto actual debe superar la tara registrada.");
        }

        if (config.PorcentajeTara > 0 && nuevoBrutoOpcional is > 0)
        {
            double maxPermitido = camion.Tara * (1.0 + config.PorcentajeTara / 100.0);
            if (camion.Tara <= 0 || maxPermitido <= 0)
            {
                return new ResultadoValidacionTara(false, "Parámetro de porcentaje de tara inválido.");
            }
        }

        return new ResultadoValidacionTara(true, "Tara registrada vigente.");
    }

    /// <summary>Verifica si la báscula se encuentra en zona de cero según parámetros de configuración.</summary>
    public static bool EstaEnCero(int pesoKg, ConfiguracionSistema config)
        => !config.BasculaEnCeroActivo || Math.Abs(pesoKg) <= Math.Max(0, config.CeroBasculaKg);

    /// <summary>
    /// Evalúa un pesaje por ejes completo contra las tablas normativas MOP (Decreto 158/80 y Ley 19.171/92).
    /// </summary>
    public static MopEvaluacionResultado EvaluarPesajeMop(
        MopNormaCamion camionNorma,
        short largoMetros,
        IReadOnlyDictionary<short, MopNormaTandem> tandemsDict,
        IReadOnlyList<MopNormaEje> ejesNorma,
        IReadOnlyList<MopNormaLargo> largosNorma,
        IReadOnlyList<int> pesosPorEjeKg)
    {
        ArgumentNullException.ThrowIfNull(camionNorma);
        ArgumentNullException.ThrowIfNull(tandemsDict);
        ArgumentNullException.ThrowIfNull(ejesNorma);
        ArgumentNullException.ThrowIfNull(largosNorma);
        ArgumentNullException.ThrowIfNull(pesosPorEjeKg);

        var ejesResultado = new List<MopEjeResultado>();
        var tandemsResultado = new List<MopTandemResultado>();
        int idxPeso = 0;
        short tandemOrdinal = 0;
        bool algunoFuera = false;

        foreach (short tandemTipo in camionNorma.Tandems)
        {
            tandemOrdinal++;
            if (!tandemsDict.TryGetValue(tandemTipo, out MopNormaTandem? tandemInfo))
            {
                continue;
            }

            List<MopNormaEje> ejesDelTandem = ejesNorma
                .Where(e => e.Tandem == tandemTipo)
                .OrderBy(e => e.Eje)
                .ToList();

            int sumaTandemKg = 0;
            foreach (MopNormaEje ejeNorm in ejesDelTandem)
            {
                int pesoKg = idxPeso < pesosPorEjeKg.Count ? pesosPorEjeKg[idxPeso] : 0;
                idxPeso++;
                sumaTandemKg += pesoKg;

                int limEje = (int)Math.Round(ejeNorm.Maximo);
                int tolEje = ejeNorm.Tolerancia;
                int excesoEje = Math.Max(0, pesoKg - limEje);
                bool fueraEje = pesoKg > (limEje + tolEje);
                if (fueraEje)
                {
                    algunoFuera = true;
                }

                ejesResultado.Add(new MopEjeResultado(
                    (short)(ejesResultado.Count + 1),
                    tandemOrdinal,
                    ejeNorm.TipoEje,
                    ejeNorm.NombreEje,
                    pesoKg,
                    limEje,
                    tolEje,
                    excesoEje,
                    fueraEje));
            }

            int limTandem = (int)Math.Round(tandemInfo.Maximo);
            int tolTandem = tandemInfo.Tolerancia;
            int excesoTandem = Math.Max(0, sumaTandemKg - limTandem);
            bool fueraTandem = sumaTandemKg > (limTandem + tolTandem);
            if (fueraTandem)
            {
                algunoFuera = true;
            }

            tandemsResultado.Add(new MopTandemResultado(
                tandemOrdinal,
                tandemTipo,
                tandemInfo.Nombre,
                sumaTandemKg,
                limTandem,
                tolTandem,
                excesoTandem,
                fueraTandem));
        }

        int pesoBrutoTotal = pesosPorEjeKg.Sum();
        int limiteMaximoCamion = camionNorma.Maximo ?? ResolverLimitePorLargo(largoMetros, largosNorma);
        int excesoBruto = Math.Max(0, pesoBrutoTotal - limiteMaximoCamion);
        if (pesoBrutoTotal > limiteMaximoCamion)
        {
            algunoFuera = true;
        }

        return new MopEvaluacionResultado
        {
            TipoMop = camionNorma.Tipo,
            Largo = largoMetros,
            PesoBrutoTotalKg = pesoBrutoTotal,
            LimiteMaximoCamionKg = limiteMaximoCamion,
            ExcesoBrutoTotalKg = excesoBruto,
            EstadoGeneral = algunoFuera ? EstadoNormaMop.FueraDeNorma : EstadoNormaMop.DentroDeNorma,
            Ejes = ejesResultado,
            Tandems = tandemsResultado
        };
    }

    public static int ResolverLimitePorLargo(short largoMetros, IReadOnlyList<MopNormaLargo> largos)
    {
        MopNormaLargo? tramo = largos.FirstOrDefault(l => largoMetros >= l.Desde && largoMetros < l.Hasta)
            ?? largos.OrderByDescending(l => l.Hasta).FirstOrDefault();
        return tramo is null ? 45000 : (int)Math.Round(tramo.Maximo);
    }
}
