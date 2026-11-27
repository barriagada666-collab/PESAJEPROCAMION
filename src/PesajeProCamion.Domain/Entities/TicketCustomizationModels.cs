using System.Text.RegularExpressions;

namespace PesajeProCamion.Domain.Entities;

/// <summary>Preferencias de visibilidad por campo para visor visual, impresión y exportaciones PDF/Excel del ticket.</summary>
public sealed class TicketFieldVisibility
{
    public bool MostrarPatente { get; set; } = true;
    public bool MostrarCliente { get; set; } = true;
    public bool MostrarProducto { get; set; } = true;
    public bool MostrarOrigen { get; set; } = true;
    public bool MostrarDestino { get; set; } = true;
    public bool MostrarTransportista { get; set; } = true;
    public bool MostrarChofer { get; set; } = true;
    public bool MostrarGuiaDespacho { get; set; } = true;
    public bool MostrarFardos { get; set; } = true;
    public bool MostrarEstado { get; set; } = true;
    public bool MostrarTiemposOperador { get; set; } = true;
    public bool MostrarObservaciones { get; set; } = true;

    public TicketFieldVisibility Clone() => (TicketFieldVisibility)MemberwiseClone();

    public static TicketFieldVisibility Default => new();
}

/// <summary>Parámetros de división de un ticket cerrado en 2 tickets (conserva folio origen y asigna folio libre al nuevo).</summary>
public sealed class TicketSplitRequest
{
    public int OriginalFolio { get; init; }
    public int NetoParte1 { get; init; }
    public int NetoParte2 { get; init; }
    public int BrutoParte1 { get; init; }
    public int TaraParte1 { get; init; }
    public int BrutoParte2 { get; init; }
    public int TaraParte2 { get; init; }
    public int? FardosParte1 { get; init; }
    public int? FardosParte2 { get; init; }
    public bool ConfirmarFardosPersonalizados { get; init; }
    public int? CustomNewFolio { get; init; }
    public bool AppendSplitNote { get; init; } = true;
}

/// <summary>Resultado transaccional de la división de un ticket en 2 folios.</summary>
public sealed record TicketSplitResult(
    int OriginalFolio,
    int NewFolio,
    int NetoParte1,
    int NetoParte2,
    int BrutoParte1,
    int TaraParte1,
    int BrutoParte2,
    int TaraParte2,
    int FardosParte1 = 0,
    int FardosParte2 = 0);

/// <summary>Definición compartida y estandarizada para emitir un informe tabular equivalente en PDF, XLSX e Impresión.</summary>
public sealed class StandardTableReport
{
    public required string Title { get; init; }
    public string Subtitle { get; init; } = string.Empty;
    public string SheetName { get; init; } = "Informe";
    public IReadOnlyList<(string Label, string Value)> Kpis { get; init; } = [];
    public required IReadOnlyList<string> Headers { get; init; }
    public required IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; }
    public IReadOnlyList<object?>? TotalsRow { get; init; }
    public int? FolioColumnIndex { get; init; }
    public IReadOnlySet<int>? BoldHighlightColumnIndices { get; init; }
    public bool Landscape { get; init; } = true;
}

/// <summary>Reglas puras de formateo de folio/tipo destacado, fardos y reparto matemático coherente al dividir tickets.</summary>
public static partial class TicketCustomizationHelper
{
    [GeneratedRegex(@"\b(?<n1>\d{1,6})\s*FARDOS?\b|\bFARDOS?\s*[:=]?\s*(?<n2>\d{1,6})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FardosRegex();

    [GeneratedRegex(@"\s*(?:/\s*)?\b\d{1,6}\s*FARDOS?\b|\s*\bFARDOS?\s*[:=]?\s*\d{1,6}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FardosTagCleanupRegex();

    public static string FormatFolioHeader(int folioPesaje)
        => $"N° Folio: {folioPesaje:D6}";

    public static string FormatTipoPesajeBadge(TipoPesaje tipo, bool anulado = false, bool enProceso = false)
    {
        string baseLabel = tipo switch
        {
            TipoPesaje.Recepcion => "RECEPCIÓN DE CARGA",
            TipoPesaje.Despacho => "DESPACHO DE CARGA",
            TipoPesaje.MovimientoInterno => "MOVIMIENTO INTERNO",
            _ => tipo.ToString().ToUpperInvariant()
        };

        if (anulado)
        {
            return $"Tipo de Pesaje: ANULADO · {baseLabel}";
        }
        if (enProceso)
        {
            return $"Tipo de Pesaje: {baseLabel} (EN PROCESO)";
        }
        return $"Tipo de Pesaje: {baseLabel}";
    }

    public static string FormatTipoPesajeNombre(TipoPesaje tipo) => tipo switch
    {
        TipoPesaje.Recepcion => "RECEPCIÓN DE CARGA",
        TipoPesaje.Despacho => "DESPACHO DE CARGA",
        TipoPesaje.MovimientoInterno => "MOVIMIENTO INTERNO",
        _ => tipo.ToString().ToUpperInvariant()
    };

    public static string FormatEstadoTicket(bool anulado, bool enProceso)
        => anulado ? "ANULADO" : (enProceso ? "EN PROCESO" : "COMPLETADO");

    public static int ExtractFardosFromObservacion(string? obs1, string? obs2)
    {
        foreach (string? text in new[] { obs2, obs1 })
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            Match m = FardosRegex().Match(text);
            if (!m.Success) continue;
            string digits = m.Groups["n1"].Success ? m.Groups["n1"].Value : m.Groups["n2"].Value;
            if (int.TryParse(digits, out int val) && val > 0)
            {
                return val;
            }
        }
        return 0;
    }

    public static string MergeFardosNote(string? baseObservacion, int fardos, int maxLength)
    {
        string cleaned = FardosTagCleanupRegex().Replace(baseObservacion ?? string.Empty, string.Empty).Trim(' ', '/', '|', '-');
        if (fardos <= 0)
        {
            return maxLength > 0 && cleaned.Length > maxLength ? cleaned[..maxLength] : cleaned;
        }
        string tag = $"{fardos} FARDOS";
        string combined = string.IsNullOrWhiteSpace(cleaned) ? tag : $"{cleaned} / {tag}";
        return maxLength > 0 && combined.Length > maxLength ? combined[..maxLength] : combined;
    }

    public static (int Fardos1, int Fardos2) ComputeSplitFardos(int origFardos, int netoParte1, int totalNeto)
    {
        if (origFardos <= 0)
        {
            return (0, 0);
        }
        int f1 = Math.Clamp((int)Math.Round(origFardos * ((double)netoParte1 / Math.Max(1, totalNeto)), MidpointRounding.AwayFromZero), 0, origFardos);
        return (f1, origFardos - f1);
    }

    public static (int Bruto1, int Tara1, int Neto1, int Bruto2, int Tara2, int Neto2) ComputeSplitWeights(
        int origBruto,
        int origTara,
        int origNeto,
        int netoParte1)
    {
        int totalNeto = origNeto > 0 ? origNeto : Math.Abs(origBruto - origTara);
        if (totalNeto < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(origNeto), "El peso neto debe ser al menos 2 kg para dividir en 2 tickets.");
        }
        if (netoParte1 < 1 || netoParte1 >= totalNeto)
        {
            throw new ArgumentOutOfRangeException(nameof(netoParte1), $"La parte 1 debe estar entre 1 y {totalNeto - 1} kg.");
        }

        int netoParte2 = totalNeto - netoParte1;
        if (origBruto >= origTara)
        {
            int tara1 = Math.Max(0, origTara);
            int tara2 = Math.Max(0, origTara);
            return (tara1 + netoParte1, tara1, netoParte1, tara2 + netoParte2, tara2, netoParte2);
        }

        int bruto1 = Math.Max(0, origBruto);
        int bruto2 = Math.Max(0, origBruto);
        return (bruto1, bruto1 + netoParte1, netoParte1, bruto2, bruto2 + netoParte2, netoParte2);
    }
}
