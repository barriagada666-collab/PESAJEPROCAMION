namespace PesajeProCamion.Domain.Entities;

public enum PerfilOperacion : short
{
    OperacionPesaje = 0,
    Consulta = 1,
    Administracion = 2
}

public sealed class Usuario
{
    public short Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public bool PuedeModificar { get; set; }
    public bool PuedeAgregar { get; set; }
    public bool PuedeEliminar { get; set; }
    public bool PuedeImprimir { get; set; } = true;
    public bool PuedeCambiarPassword { get; set; } = true;
    public bool IngresoPesaje { get; set; } = true;
    public bool IngresoGestion { get; set; }
    public bool SupervisorSistema { get; set; }
    public PerfilOperacion Perfil { get; set; } = PerfilOperacion.OperacionPesaje;
    public bool JefeTurno { get; set; }
    public short UsuarioIngreso { get; set; }
    public short UsuarioModificacion { get; set; }
}

public sealed class LogIngreso
{
    public int IdLog { get; set; }
    public int UsuarioId { get; set; }
    public string NombreUsuario { get; set; } = string.Empty;
    public int Sistema { get; set; }
    public string DescripcionSistema { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public string Hora { get; set; } = string.Empty;
    public int Turno { get; set; }
    public int Accion { get; set; }
    public string DescripcionAccion { get; set; } = string.Empty;
}

public sealed class AuditLogEntry
{
    public int IdAudit { get; set; }
    public DateTime Fecha { get; set; }
    public string Hora { get; set; } = string.Empty;
    public short UsuarioId { get; set; }
    public string NombreUsuario { get; set; } = string.Empty;
    public string Modulo { get; set; } = string.Empty;
    public string Entidad { get; set; } = string.Empty;
    public string Clave { get; set; } = string.Empty;
    public string Accion { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
}
