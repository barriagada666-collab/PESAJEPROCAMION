using PesajeProCamion.Domain.Entities;

namespace PesajeProCamion.Domain.Security;

public enum AccionAutorizacion
{
    IngresarPesaje,
    IngresarGestion,
    AgregarRegistro,
    ModificarRegistro,
    EliminarRegistro,
    ImprimirTicket,
    AnularPesaje,
    CambiarPassword,
    ConfigurarVisorBascula,
    ConfigurarInstalacion,
    ConfigurarPesajeDinamico,
    AdministrarUsuarios,
    VerAuditoria
}

/// <summary>Política centralizada de control de acceso basado en roles (RBAC) y privilegios por usuario.</summary>
public static class PermissionService
{
    public static bool EstaAutorizado(Usuario? usuario, AccionAutorizacion accion)
    {
        if (usuario is null || !usuario.Activo)
        {
            return false;
        }

        bool esAdmin = usuario.Perfil == PerfilOperacion.Administracion || usuario.SupervisorSistema;
        bool esConsulta = usuario.Perfil == PerfilOperacion.Consulta;

        return accion switch
        {
            AccionAutorizacion.IngresarPesaje => usuario.IngresoPesaje && !esConsulta,
            AccionAutorizacion.IngresarGestion => usuario.IngresoGestion || esAdmin,
            AccionAutorizacion.AgregarRegistro => !esConsulta && (usuario.PuedeAgregar || esAdmin),
            AccionAutorizacion.ModificarRegistro => !esConsulta && (usuario.PuedeModificar || esAdmin),
            AccionAutorizacion.EliminarRegistro => !esConsulta && (usuario.PuedeEliminar || esAdmin),
            AccionAutorizacion.ImprimirTicket => usuario.PuedeImprimir || esAdmin,
            AccionAutorizacion.AnularPesaje => !esConsulta && (usuario.PuedeEliminar || usuario.PuedeModificar || esAdmin),
            AccionAutorizacion.CambiarPassword => usuario.PuedeCambiarPassword || esAdmin,
            AccionAutorizacion.ConfigurarVisorBascula => esAdmin,
            AccionAutorizacion.ConfigurarInstalacion => esAdmin,
            AccionAutorizacion.ConfigurarPesajeDinamico => esAdmin,
            AccionAutorizacion.AdministrarUsuarios => esAdmin,
            AccionAutorizacion.VerAuditoria => esAdmin || usuario.JefeTurno,
            _ => false
        };
    }

    public static string DescribirPerfil(PerfilOperacion perfil) => perfil switch
    {
        PerfilOperacion.OperacionPesaje => "Operación de Pesaje",
        PerfilOperacion.Consulta => "Consulta de Datos",
        PerfilOperacion.Administracion => "Administración",
        _ => "Operación de Pesaje"
    };
}
