using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Catalogs;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Main;
using PesajeProCamion.App.UI.Management;
using PesajeProCamion.App.UI.Weighing;
using PesajeProCamion.Data.Configuration;
using PesajeProCamion.Domain.Entities;
using Xunit;

namespace PesajeProCamion.Tests.UI;

public sealed class UiSmokeAndAuditTests
{
    private static readonly string[] ForbiddenLegacyTypos =
    [
        "Excell",
        "delSistema",
        "Pueto",
        "Impirmir",
        "CAMIMERO",
        "New Item"
    ];

    [Fact]
    public void AllFormsAndControls_InstantiateWithoutLegacyTyposOrBrokenButtons()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new DatabaseSettings { DataSource = "localhost", DatabasePath = @"C:\nonexistent\test.fdb" };
                using var services = new AppServices(settings, testMode: true)
                {
                    UsuarioActual = new Usuario
                    {
                        Id = 5,
                        Nombre = "ADMIN TEST",
                        Activo = true,
                        Perfil = PerfilOperacion.Administracion,
                        IngresoPesaje = true,
                        IngresoGestion = true,
                        PuedeAgregar = true,
                        PuedeModificar = true,
                        PuedeEliminar = true,
                        PuedeImprimir = true,
                        PuedeCambiarPassword = true
                    }
                };

                Control[] components =
                [
                    new LoginForm(services),
                    new ChangePasswordForm(),
                    new DatabaseConnectionForm(services),
                    new AcercaForm(services),
                    new ManualWeightDialog(10000),
                    new FolioTicketDialog(),
                    new VisorSetupForm(services),
                    new DinamicoSetupForm(services),
                    new InstalacionForm(services),
                    new UsuariosForm(services),
                    new ClientesForm(services),
                    new CamionesForm(services),
                    new ProductosForm(services),
                    new OrigenesForm(services),
                    new DestinosForm(services),
                    new ChoferesForm(services),
                    new TransportistasForm(services),
                    new PesajeMopForm(services),
                    new MainShellForm(services),
                    new PesajeWorkspaceControl(services),
                    new GestionPesajesControl(services),
                    new ReportsWorkspaceControl(services)
                ];

                foreach (Control root in components)
                {
                    using (root)
                    {
                        AuditControlTree(root);
                    }
                }
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
        {
            throw new InvalidOperationException("Falló la auditoría UI en hilo STA.", threadEx);
        }
    }

    private static void AuditControlTree(Control root)
    {
        foreach (string typo in ForbiddenLegacyTypos)
        {
            Assert.DoesNotContain(typo, root.Text, StringComparison.OrdinalIgnoreCase);
        }

        if (root is Button btn)
        {
            Assert.NotNull(btn.Image);
            Assert.True(!string.IsNullOrWhiteSpace(btn.Text) || btn.Size.Width <= 40, $"Botón sin texto descriptivo en {root.Parent?.Name}");
        }
        else if (root is ToolStrip ts)
        {
            foreach (ToolStripItem item in ts.Items)
            {
                foreach (string typo in ForbiddenLegacyTypos)
                {
                    Assert.DoesNotContain(typo, item.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                }
                if (item is ToolStripButton tsb)
                {
                    Assert.NotNull(tsb.Image);
                    Assert.False(string.IsNullOrWhiteSpace(tsb.ToolTipText), $"ToolStripButton '{tsb.Text}' sin ToolTip.");
                }
            }
        }

        foreach (Control child in root.Controls)
        {
            AuditControlTree(child);
        }
    }
}
