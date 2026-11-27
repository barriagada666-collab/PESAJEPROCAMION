namespace PesajeProCamion.Domain.Entities;

/// <summary>Información general de la báscula e instalación.</summary>
public sealed class BasculaInfo
{
    public short Bascula { get; set; } = 1;
    public string Descripcion { get; set; } = string.Empty;
    public int FolioPesajeMop { get; set; } = 1;
    public string NroInstalacion { get; set; } = "1";
    public string Rut { get; set; } = string.Empty;
    public string NombreEmpresa { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public int? Folio { get; set; }
    public short UsuarioIngreso { get; set; }
    public short UsuarioModificacion { get; set; }
}

/// <summary>Parámetros operativos, de impresión, visor serial y pesaje dinámico.</summary>
public sealed class ConfiguracionSistema
{
    public DateTime? FechaInstalacion { get; set; }
    public int PorcentajeTara { get; set; } = 10;
    public int NroCopiasTicket { get; set; } = 1;
    public bool BasculaEnCeroActivo { get; set; } = true;
    public int CeroBasculaKg { get; set; } = 50;
    public bool DinamicoActivo { get; set; } = true;
    public string? EstableEstatico { get; set; }
    public bool ConfirmarImpresionTicket { get; set; } = true;
    public int SistemaOperativo { get; set; } = 1;
    public int PrintIndexTicket { get; set; }
    public int PrintIndexInforme { get; set; }
    public string? Titulo1 { get; set; }
    public string? Titulo2 { get; set; }
    public string? Titulo3 { get; set; }
    public string? SeguridadActiva { get; set; }
    public int VmopUnder { get; set; } = 350;
    public int VmopOver { get; set; } = 350;
    public int VmopPreSample { get; set; } = 800;
    public int VmopMinSample { get; set; } = 500;
    public int VmopLowSamples { get; set; } = 5;
    public int VmopHiSamples { get; set; } = 12;
    public int VmopInsideSamples { get; set; }
    public int VmopCutLeft { get; set; } = 30;
    public int VmopCutRight { get; set; } = 30;
    public int VmopMaxNodes { get; set; }
    public int VLimit { get; set; } = 1;
    public int VDelta { get; set; } = 10;
    public int VisorStx { get; set; } = 2;
    public int VisorPosData { get; set; } = 3;
    public int VisorLenData { get; set; } = 5;
    public int NroFiltros { get; set; } = 80;
    public double DstUpFactor { get; set; } = 1.0;
    public double DstDownFactor { get; set; } = 1.0;
    public int[] Offsets { get; set; } = new int[7];
    public int[] Calibraciones { get; set; } = new int[7];
}

/// <summary>Folios globales del sistema.</summary>
public sealed class FoliosSistema
{
    public int FolioPesaje { get; set; } = 1;
    public int FolioPesajeMop { get; set; } = 1;
    public int FolioUsuario { get; set; } = 1;
    public int? FolioGuiaDespacho { get; set; }
}

/// <summary>Configuración de puerto serial / hardware.</summary>
public sealed class HardwareSetup
{
    public string Device { get; set; } = "BASCULA";
    public short ComNumber { get; set; } = 1;
    public int BaudRateIndex { get; set; }
    public int ParityIndex { get; set; }
    public int DataBitsIndex { get; set; } = 1;
    public int? StopBitsIndex { get; set; }
    public string? Printer { get; set; }

    public int BaudRateReal => BaudRateIndex switch
    {
        0 => 9600,
        1 => 4800,
        2 => 2400,
        3 => 1200,
        9600 or 4800 or 2400 or 1200 or 19200 or 38400 or 115200 => BaudRateIndex,
        _ => 9600
    };

    public int DataBitsReal => DataBitsIndex switch
    {
        0 => 7,
        1 => 8,
        7 or 8 => DataBitsIndex,
        _ => 8
    };
}

public sealed class Cliente
{
    public string Rut { get; set; } = string.Empty;
    public string Digito { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string Giro { get; set; } = string.Empty;
    public string Comuna { get; set; } = string.Empty;
    public string Ciudad { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Fax { get; set; } = string.Empty;
    public string Casilla { get; set; } = string.Empty;
    public short UsuarioIngreso { get; set; }
    public short UsuarioModificacion { get; set; }
}

public sealed class Producto
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public short UsuarioIngreso { get; set; }
    public short UsuarioModificacion { get; set; }
}

public sealed class Origen
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public short UsuarioIngreso { get; set; }
    public short UsuarioModificacion { get; set; }
}

public sealed class Destino
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public short UsuarioIngreso { get; set; }
    public short UsuarioModificacion { get; set; }
}

public sealed class Chofer
{
    public string Rut { get; set; } = string.Empty;
    public string Digito { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public short UsuarioIngreso { get; set; }
    public short UsuarioModificacion { get; set; }
}

public sealed class Transportista
{
    public string Rut { get; set; } = string.Empty;
    public string Digito { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string Ciudad { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Fax { get; set; } = string.Empty;
    public short UsuarioIngreso { get; set; }
    public short UsuarioModificacion { get; set; }
}

public sealed class Camion
{
    public string Patente { get; set; } = string.Empty;
    public string Acoplado { get; set; } = string.Empty;
    public string Transportista { get; set; } = string.Empty;
    public string Chofer { get; set; } = string.Empty;
    public short TipoMop { get; set; }
    public short Largo { get; set; }
    public int Tara { get; set; }
    public DateTime? FechaTara { get; set; }
    public string HoraTara { get; set; } = string.Empty;
    public bool TaraRegistrada { get; set; }
    public DateTime? FechaTaraRegistrada { get; set; }
    public short DiasValidez { get; set; }
    public short UsuarioIngreso { get; set; }
    public short UsuarioModificacion { get; set; }
}

public sealed class TipoCarga
{
    public short Codigo { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public short Normal { get; set; }
    public short UsuarioIngreso { get; set; }
    public short UsuarioModificacion { get; set; }
}
