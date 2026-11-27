namespace PesajeProCamion.Domain.Entities;

public enum TipoPesaje : short
{
    Recepcion = 1,
    Despacho = 2,
    MovimientoInterno = 3
}

public enum EstadoNormaMop : short
{
    NoAplica = 0,
    DentroDeNorma = 1,
    FueraDeNorma = 2
}

public sealed class RegistroTransito
{
    public int FolioPesaje { get; set; }
    public string Patente { get; set; } = string.Empty;
    public string Acoplado { get; set; } = string.Empty;
    public string Producto { get; set; } = string.Empty;
    public string NombreProducto { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string NombreCliente { get; set; } = string.Empty;
    public string Origen { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string GuiaDespacho { get; set; } = string.Empty;
    public int NetoInformado { get; set; }
    public DateTime FechaIngreso { get; set; }
    public string HoraIngreso { get; set; } = string.Empty;
    public int Bruto { get; set; }
    public int Tara { get; set; }
    public short TipoMop { get; set; }
    public short Largo { get; set; }
    public int MaxBruto { get; set; }
    public string Chofer { get; set; } = string.Empty;
    public string Observacion1 { get; set; } = string.Empty;
    public int Fardos { get; set; }
    public string Transportista { get; set; } = string.Empty;
    public TipoPesaje TipoPesaje { get; set; } = TipoPesaje.Recepcion;
    public string? SavedToServer { get; set; }
    public short UsuarioIngreso { get; set; }
    public string? Traspaso { get; set; }
}

public sealed class RegistroTransitoAnulado
{
    public int FolioPesaje { get; set; }
    public string Patente { get; set; } = string.Empty;
    public string Producto { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public DateTime FechaIngreso { get; set; }
    public string HoraIngreso { get; set; } = string.Empty;
    public int Bruto { get; set; }
    public int Tara { get; set; }
    public int Fardos { get; set; }
    public string Observacion { get; set; } = string.Empty;
    public DateTime FechaAnulacion { get; set; }
    public string HoraAnulacion { get; set; } = string.Empty;
    public string NombreUsuarioAnulacion { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;
}

public sealed class Pesaje
{
    public int FolioPesaje { get; set; }
    public string Patente { get; set; } = string.Empty;
    public string Acoplado { get; set; } = string.Empty;
    public int FolioPesajeMop { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string NombreProducto { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string NombreCliente { get; set; } = string.Empty;
    public string Origen { get; set; } = string.Empty;
    public string NombreOrigen { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string NombreDestino { get; set; } = string.Empty;
    public string GuiaDespacho { get; set; } = string.Empty;
    public int NetoInformado { get; set; }
    public DateTime FechaIngreso { get; set; }
    public string HoraIngreso { get; set; } = string.Empty;
    public DateTime FechaSalida { get; set; }
    public string HoraSalida { get; set; } = string.Empty;
    public DateTime? FechaModificacion { get; set; }
    public string? HoraModificacion { get; set; }
    public int Bruto { get; set; }
    public int Tara { get; set; }
    public int Neto { get; set; }
    public short TipoMop { get; set; }
    public short Largo { get; set; }
    public int MaxBruto { get; set; }
    public EstadoNormaMop EstadoMop { get; set; } = EstadoNormaMop.NoAplica;
    public string Chofer { get; set; } = string.Empty;
    public string NombreChofer { get; set; } = string.Empty;
    public string? Traspaso { get; set; }
    public string Observacion1 { get; set; } = string.Empty;
    public string Observacion2 { get; set; } = string.Empty;
    public int Fardos { get; set; }
    public string Transportista { get; set; } = string.Empty;
    public string NombreTransportista { get; set; } = string.Empty;
    public bool TaraRegistrada { get; set; }
    public TipoPesaje TipoPesaje { get; set; } = TipoPesaje.Recepcion;
    public string? SavedToServer { get; set; }
    public short UsuarioIngreso { get; set; }
    public string NombreUsuarioIngreso { get; set; } = string.Empty;
    public short UsuarioSalida { get; set; }
    public string NombreUsuarioSalida { get; set; } = string.Empty;
    public short UsuarioModificacion { get; set; }
    public string NombreUsuarioModificacion { get; set; } = string.Empty;
    public short UsuarioEliminacion { get; set; }
    public string NombreUsuarioEliminacion { get; set; } = string.Empty;
    public bool Anulado { get; set; }
}

public sealed class PesajeMop
{
    public int FolioMop { get; set; }
    public short Bascula { get; set; } = 1;
    public int FolioPesaje { get; set; }
    public string Patente { get; set; } = string.Empty;
    public string Acoplado { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public string Hora { get; set; } = string.Empty;
    public short TipoCarga { get; set; } = 7;
    public string Transportista { get; set; } = string.Empty;
    public short TipoMop { get; set; }
    public short Largo { get; set; }
    public int Bruto { get; set; }
    public int Maximo { get; set; }
    public int Sobrepeso { get; set; }
    public string GuiaDespacho { get; set; } = string.Empty;
    public string Chofer { get; set; } = string.Empty;
    public EstadoNormaMop Estado { get; set; } = EstadoNormaMop.DentroDeNorma;
    public string? SavedToServer { get; set; }
    public short UsuarioId { get; set; }
    public List<PesajeMopDetalle> Detalles { get; } = [];
}

public sealed class PesajeMopDetalle
{
    public int FolioMop { get; set; }
    public short Bascula { get; set; } = 1;
    public short EjeNumero { get; set; }
    public short EjeTipo { get; set; }
    public int EjePeso { get; set; }
    public int EjeLimite { get; set; }
    public short TandemNumero { get; set; }
    public short TandemTipo { get; set; }
    public int TandemPeso { get; set; }
    public int TandemLimite { get; set; }
    public string? SavedToServer { get; set; }
}

public sealed record MopNormaCamion(short Tipo, short Tandem01, short Tandem02, short Tandem03, short Tandem04, int? Maximo)
{
    public IReadOnlyList<short> Tandems =>
        new[] { Tandem01, Tandem02, Tandem03, Tandem04 }.Where(t => t > 0).ToArray();
}

public sealed record MopNormaTandem(short Tandem, string Nombre, double Maximo, int Tolerancia);

public sealed record MopNormaEje(short Tandem, short Eje, short TipoEje, double Maximo, int Tolerancia, string NombreEje);

public sealed record MopNormaLargo(short Desde, short Hasta, double Maximo);

public sealed record MopEjeResultado(
    short EjeNumero,
    short TandemNumero,
    short TipoEje,
    string NombreEje,
    int PesoKg,
    int LimiteKg,
    int ToleranciaKg,
    int ExcesoKg,
    bool FueraDeNorma);

public sealed record MopTandemResultado(
    short TandemNumero,
    short TandemTipo,
    string NombreTandem,
    int PesoTotalKg,
    int LimiteKg,
    int ToleranciaKg,
    int ExcesoKg,
    bool FueraDeNorma);

public sealed class MopEvaluacionResultado
{
    public short TipoMop { get; init; }
    public short Largo { get; init; }
    public int PesoBrutoTotalKg { get; init; }
    public int LimiteMaximoCamionKg { get; init; }
    public int ExcesoBrutoTotalKg { get; init; }
    public EstadoNormaMop EstadoGeneral { get; init; }
    public IReadOnlyList<MopEjeResultado> Ejes { get; init; } = [];
    public IReadOnlyList<MopTandemResultado> Tandems { get; init; } = [];
}

public sealed record DemoMuestra(double PesoKg, string Hora);
