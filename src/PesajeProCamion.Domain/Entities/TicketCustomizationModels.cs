namespace PesajeProCamion.Domain.Entities;

/// <summary>Preferencias de visibilidad por campo para visor visual, impresión y exportación PDF del ticket.</summary>
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
    int TaraParte2);

/// <summary>Reglas puras de formateo de tipo destacado y reparto matemático coherente al dividir tickets.</summary>
public static class TicketCustomizationHelper
{
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
            return $"ANULADO · {baseLabel}";
        }
        if (enProceso)
        {
            return $"{baseLabel} (EN PROCESO)";
        }
        return baseLabel;
    }

    public static string FormatEstadoTicket(bool anulado, bool enProceso)
        => anulado ? "ANULADO" : (enProceso ? "EN PROCESO" : "COMPLETADO");

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
