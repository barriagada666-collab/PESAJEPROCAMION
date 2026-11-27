using System.Globalization;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Validation;

namespace PesajeProCamion.Domain.Services;

/// <summary>Motor determinístico de segmentación multidimensional y cálculo de KPIs operativos de pesaje.</summary>
public static class AnalyticsCalculator
{
    public static AnalyticsResult FilterAndAnalyze(
        IReadOnlyList<Pesaje> source,
        ReportFilterCriteria? criteria = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ReportFilterCriteria f = criteria ?? new ReportFilterCriteria();

        DateTime? desde = f.Desde?.Date;
        DateTime? hasta = f.Hasta?.Date;
        string? patRaw = NormalizeFilter(f.Patente);
        string? patNorm = patRaw is null ? null : ChileanValidators.NormalizarPatente(patRaw);
        string? provFilter = NormalizeFilter(f.ProveedorOrigen);
        string? transpFilter = NormalizeFilter(f.Transportista);
        string? cliFilter = NormalizeFilter(f.Cliente);
        string? prodFilter = NormalizeFilter(f.Producto);
        string? freeText = NormalizeFilter(f.TextoLibre);

        var filtered = new List<Pesaje>();
        foreach (Pesaje p in source)
        {
            DateTime fechaRef = (p.FechaSalida > DateTime.MinValue ? p.FechaSalida : p.FechaIngreso).Date;
            if (desde.HasValue && fechaRef < desde.Value) continue;
            if (hasta.HasValue && fechaRef > hasta.Value) continue;
            if (f.TipoPesaje.HasValue && p.TipoPesaje != f.TipoPesaje.Value) continue;

            if (f.Estado == EstadoTicketFiltro.SoloActivos && p.Anulado) continue;
            if (f.Estado == EstadoTicketFiltro.SoloAnulados && !p.Anulado) continue;

            if (patNorm is not null && !p.Patente.Contains(patNorm, StringComparison.OrdinalIgnoreCase)) continue;
            if (cliFilter is not null && !MatchesAny(cliFilter, p.Cliente, p.NombreCliente)) continue;
            if (prodFilter is not null && !MatchesAny(prodFilter, p.Producto, p.NombreProducto)) continue;
            if (transpFilter is not null && !MatchesAny(transpFilter, p.Transportista, p.NombreTransportista)) continue;
            if (provFilter is not null &&
                !MatchesAny(provFilter, p.Origen, p.NombreOrigen, p.Transportista, p.NombreTransportista) &&
                !(p.TipoPesaje == TipoPesaje.Recepcion && MatchesAny(provFilter, p.Cliente, p.NombreCliente)))
            {
                continue;
            }
            if (freeText is not null && !MatchesAny(
                    freeText,
                    p.FolioPesaje.ToString(CultureInfo.InvariantCulture),
                    p.Patente, p.Acoplado, p.Cliente, p.NombreCliente,
                    p.Producto, p.NombreProducto, p.Origen, p.NombreOrigen,
                    p.Destino, p.NombreDestino, p.Transportista, p.NombreTransportista,
                    p.Chofer, p.NombreChofer, p.GuiaDespacho))
            {
                continue;
            }

            filtered.Add(p);
        }

        List<Pesaje> activos = filtered.Where(x => !x.Anulado).ToList();
        long sumBruto = activos.Sum(x => (long)x.Bruto);
        long sumTara = activos.Sum(x => (long)x.Tara);
        long sumNeto = activos.Sum(x => (long)x.Neto);
        int maxNeto = activos.Count > 0 ? activos.Max(x => x.Neto) : 0;
        int minNeto = activos.Count > 0 ? activos.Min(x => x.Neto) : 0;
        double avgNeto = activos.Count > 0 ? (double)sumNeto / activos.Count : 0.0;

        IReadOnlyList<SegmentSummaryRow> porCliente = BuildSegments(
            activos, sumNeto,
            p => string.IsNullOrWhiteSpace(p.Cliente) ? "(SIN CLIENTE)" : p.Cliente.Trim(),
            p => string.IsNullOrWhiteSpace(p.NombreCliente) ? "(SIN CLIENTE)" : p.NombreCliente.Trim());

        IReadOnlyList<SegmentSummaryRow> porProveedor = BuildSegments(
            activos, sumNeto,
            p => ResolveProveedorKey(p),
            p => ResolveProveedorLabel(p));

        IReadOnlyList<SegmentSummaryRow> porProducto = BuildSegments(
            activos, sumNeto,
            p => string.IsNullOrWhiteSpace(p.Producto) ? "(SIN PRODUCTO)" : p.Producto.Trim(),
            p => string.IsNullOrWhiteSpace(p.NombreProducto) ? "(SIN PRODUCTO)" : p.NombreProducto.Trim());

        IReadOnlyList<SegmentSummaryRow> porPatente = BuildSegments(
            activos, sumNeto,
            p => string.IsNullOrWhiteSpace(p.Patente) ? "(SIN PATENTE)" : p.Patente.Trim(),
            p => string.IsNullOrWhiteSpace(p.Patente) ? "(SIN PATENTE)" : p.Patente.Trim());

        IReadOnlyList<DailyTrendPoint> tendencia = activos
            .GroupBy(p => (p.FechaSalida > DateTime.MinValue ? p.FechaSalida : p.FechaIngreso).Date)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                long neto = g.Sum(x => (long)x.Neto);
                return new DailyTrendPoint(g.Key, g.Count(), neto, Math.Round(neto / 1000.0, 2));
            })
            .ToList();

        var kpis = new ReportKpiSummary(
            TotalTickets: filtered.Count,
            TicketsActivos: activos.Count,
            TicketsAnulados: filtered.Count - activos.Count,
            TicketsEnProceso: 0,
            TotalBrutoKg: sumBruto,
            TotalTaraKg: sumTara,
            TotalNetoKg: sumNeto,
            ToneladasNetas: Math.Round(sumNeto / 1000.0, 2),
            PromedioNetoKg: Math.Round(avgNeto, 1),
            MaximoNetoKg: maxNeto,
            MinimoNetoKg: minNeto,
            TopPatente: porPatente.FirstOrDefault() is { } tp ? $"{tp.Nombre} ({tp.ToneladasNetas:N1} t)" : "-",
            TopCliente: porCliente.FirstOrDefault() is { } tc ? $"{Truncate(tc.Nombre, 26)} ({tc.ToneladasNetas:N1} t)" : "-",
            TopProducto: porProducto.FirstOrDefault() is { } tpr ? $"{Truncate(tpr.Nombre, 26)} ({tpr.ToneladasNetas:N1} t)" : "-",
            TopProveedorOrigen: porProveedor.FirstOrDefault() is { } tpv ? $"{Truncate(tpv.Nombre, 26)} ({tpv.ToneladasNetas:N1} t)" : "-");

        return new AnalyticsResult
        {
            Kpis = kpis,
            Filtrados = filtered,
            PorCliente = porCliente,
            PorProveedorOrigen = porProveedor,
            PorProducto = porProducto,
            PorPatente = porPatente,
            TendenciaDiaria = tendencia
        };
    }

    private static string ResolveProveedorKey(Pesaje p)
    {
        if (!string.IsNullOrWhiteSpace(p.NombreOrigen) && !string.Equals(p.NombreOrigen.Trim(), "(SIN ORIGEN)", StringComparison.OrdinalIgnoreCase))
        {
            return p.Origen.Trim();
        }
        if (!string.IsNullOrWhiteSpace(p.Transportista))
        {
            return p.Transportista.Trim();
        }
        return "(SIN PROVEEDOR/ORIGEN)";
    }

    private static string ResolveProveedorLabel(Pesaje p)
    {
        string origen = !string.IsNullOrWhiteSpace(p.NombreOrigen) ? p.NombreOrigen.Trim() : p.Origen.Trim();
        string transp = !string.IsNullOrWhiteSpace(p.NombreTransportista) ? p.NombreTransportista.Trim() : p.Transportista.Trim();
        if (!string.IsNullOrEmpty(origen) && !string.IsNullOrEmpty(transp) && !string.Equals(origen, transp, StringComparison.OrdinalIgnoreCase))
        {
            return $"{origen} / {transp}";
        }
        if (!string.IsNullOrEmpty(origen)) return origen;
        if (!string.IsNullOrEmpty(transp)) return transp;
        return "(SIN PROVEEDOR/ORIGEN)";
    }

    private static IReadOnlyList<SegmentSummaryRow> BuildSegments(
        List<Pesaje> activos,
        long grandTotalNeto,
        Func<Pesaje, string> keySelector,
        Func<Pesaje, string> labelSelector)
    {
        return activos
            .GroupBy(p => (Key: keySelector(p), Label: labelSelector(p)))
            .Select(g =>
            {
                long neto = g.Sum(x => (long)x.Neto);
                long bruto = g.Sum(x => (long)x.Bruto);
                long tara = g.Sum(x => (long)x.Tara);
                int count = g.Count();
                double pct = grandTotalNeto > 0 ? Math.Round(neto * 100.0 / grandTotalNeto, 2) : 0.0;
                return new SegmentSummaryRow(
                    g.Key.Key,
                    g.Key.Label,
                    count,
                    bruto,
                    tara,
                    neto,
                    Math.Round(neto / 1000.0, 2),
                    count > 0 ? Math.Round((double)neto / count, 1) : 0.0,
                    pct);
            })
            .OrderByDescending(r => r.TotalNetoKg)
            .ThenByDescending(r => r.Tickets)
            .ToList();
    }

    private static string? NormalizeFilter(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string trimmed = raw.Trim();
        return trimmed.StartsWith("(Todos", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("(Todas", StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;
    }

    private static bool MatchesAny(string filter, params string?[] fields)
    {
        foreach (string? val in fields)
        {
            if (!string.IsNullOrWhiteSpace(val) && val.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
