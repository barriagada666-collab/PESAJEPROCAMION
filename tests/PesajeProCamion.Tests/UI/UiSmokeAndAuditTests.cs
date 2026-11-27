using System.Drawing;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Catalogs;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Main;
using PesajeProCamion.App.UI.Management;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.App.UI.Weighing;
using PesajeProCamion.Data.Configuration;
using PesajeProCamion.Domain.Diagnostics;
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

                AppLogger.IsSilentTestHost = true;
                ApplicationConfiguration.Initialize();
                Assert.NotNull(IconCatalog.GetCorporateLogo());

                var samplePesaje = new Pesaje
                {
                    FolioPesaje = 100,
                    Patente = "ABCD12",
                    Bruto = 28000,
                    Tara = 11000,
                    Neto = 17000,
                    FechaIngreso = DateTime.Today,
                    HoraIngreso = "08:00",
                    FechaSalida = DateTime.Today,
                    HoraSalida = "08:30"
                };

                Control[] components =
                [
                    new SplashForm(),
                    new LoginForm(services),
                    new ChangePasswordForm(),
                    new DatabaseConnectionForm(services),
                    new ErrorLogViewerDialog(),
                    new AcercaForm(services),
                    new ManualWeightDialog(10000),
                    new FolioTicketDialog(),
                    new TicketCopiesDialog(2),
                    new VisorSetupForm(services),
                    new DinamicoSetupForm(services),
                    new InstalacionForm(services),
                    new UsuariosForm(services),
                    new TicketViewerDialog(services, samplePesaje),
                    new TicketEditorDialog(services, TicketEditorMode.CreateNew),
                    new ModPesajeDialog(services, samplePesaje),
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
                        if (root is Form form)
                        {
                            form.ShowInTaskbar = false;
                            form.Opacity = 0;
                            if (form is MainShellForm ms)
                            {
                                ms.WindowState = FormWindowState.Normal;
                                ms.Size = new Size(1024, 700);
                            }
                            form.Show();
                            Application.DoEvents();
                        }
                        else
                        {
                            root.Size = new Size(1240, 720);
                            root.PerformLayout();
                        }
                        AuditControlTree(root);
                        if (root is MainShellForm msLoaded)
                        {
                            TabControl? tabs = msLoaded.Controls.OfType<TabControl>().FirstOrDefault();
                            Assert.NotNull(tabs);
                            for (int t = 0; t < tabs.TabPages.Count; t++)
                            {
                                tabs.SelectedIndex = t;
                                Application.DoEvents();
                                AuditControlTree(tabs.TabPages[t]);
                            }
                        }
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
            if (!string.IsNullOrWhiteSpace(btn.Text))
            {
                Size measured = TextRenderer.MeasureText(btn.Text, btn.Font);
                int neededW = measured.Width + (btn.Image is not null ? btn.Image.Width + 6 : 0) + btn.Padding.Horizontal + 10;
                Assert.True(btn.Width >= neededW, $"Botón recortado '{btn.Text}': Width={btn.Width} < Needed={neededW}");
            }
        }
        else if (root is DataGridView dgv &&
                 (string.Equals(dgv.Name, "_detalleBiGrid", StringComparison.Ordinal) ||
                  root.FindForm() is UsuariosForm))
        {
            Assert.NotNull(dgv.ContextMenuStrip);
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

        if (root.Parent is not null && root.Dock == DockStyle.None &&
            !(root.Parent is Panel p && p.AutoScroll) && root is not ScrollBar && root.Parent is not Form)
        {
            Assert.True(
                root.Right <= root.Parent.ClientSize.Width + 2 &&
                root.Bottom <= root.Parent.ClientSize.Height + 2 &&
                root.Left >= 0 && root.Top >= 0,
                $"Control fuera de rango: {root.GetType().Name} '{root.Text}' {root.Bounds} en {root.Parent.ClientSize}");
        }

        foreach (Control child in root.Controls)
        {
            AuditControlTree(child);
        }
    }
}
