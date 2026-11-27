namespace PesajeProCamion.Domain.Entities;

public enum EstadoTicketFiltro
{
    SoloActivos = 0,
    Todos = 1,
    SoloAnulados = 2,
    EnProceso = 3
}

public sealed class ReportFilterCriteria
{
    public DateTime? Desde { get; set; }
    public DateTime? Hasta { get; set; }
    public string? Patente { get; set; }
    public string? ProveedorOrigen { get; set; }
    public string? Transportista { get; set; }
    public string? Cliente { get; set; }
    public string? Producto { get; set; }
    public TipoPesaje? TipoPesaje { get; set; }
    public EstadoTicketFiltro Estado { get; set; } = EstadoTicketFiltro.SoloActivos;
    public string? TextoLibre { get; set; }
}

public sealed record ReportKpiSummary(
    int TotalTickets,
    int TicketsActivos,
    int TicketsAnulados,
    int TicketsEnProceso,
    long TotalBrutoKg,
    long TotalTaraKg,
    long TotalNetoKg,
    double ToneladasNetas,
    double PromedioNetoKg,
    int MaximoNetoKg,
    int MinimoNetoKg,
    string TopPatente,
    string TopCliente,
    string TopProducto,
    string TopProveedorOrigen);

public sealed record SegmentSummaryRow(
    string Clave,
    string Nombre,
    int Tickets,
    long TotalBrutoKg,
    long TotalTaraKg,
    long TotalNetoKg,
    double ToneladasNetas,
    double PromedioNetoKg,
    double PorcentajeNeto);

public sealed record DailyTrendPoint(
    DateTime Fecha,
    int Tickets,
    long TotalNetoKg,
    double ToneladasNetas);

public sealed class AnalyticsResult
{
    public required ReportKpiSummary Kpis { get; init; }
    public required IReadOnlyList<Pesaje> Filtrados { get; init; }
    public required IReadOnlyList<SegmentSummaryRow> PorCliente { get; init; }
    public required IReadOnlyList<SegmentSummaryRow> PorProveedorOrigen { get; init; }
    public required IReadOnlyList<SegmentSummaryRow> PorProducto { get; init; }
    public required IReadOnlyList<SegmentSummaryRow> PorPatente { get; init; }
    public required IReadOnlyList<DailyTrendPoint> TendenciaDiaria { get; init; }
}
