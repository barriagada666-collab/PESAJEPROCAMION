using System.Drawing;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Theme;

namespace PesajeProCamion.App.UI.Dialogs;

/// <summary>Información corporativa de versión e instalación con logo oficial y metadatos genéricos limpios.</summary>
public sealed class AcercaForm : Form
{
    public AcercaForm(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Text = "Acerca de PesajeProCamion";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(500, 300);
        BackColor = AppTheme.SurfaceCard;
        AppTheme.ApplyFormIcon(this);

        var top = new Panel { Dock = DockStyle.Top, Height = 92 };
        top.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, top.ClientRectangle);

        var logoCard = new Panel { Location = new Point(16, 14), Size = new Size(106, 62), BackColor = Color.White };
        var pic = new PictureBox
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(4),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = IconCatalog.GetCorporateLogo() ?? IconCatalog.RenderTruckBadge(98, 54)
        };
        logoCard.Controls.Add(pic);

        var title = new Label
        {
            Text = "PesajeProCamion — v2.4.0",
            Location = new Point(136, 18),
            AutoSize = true,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold)
        };
        var sub = new Label
        {
            Text = "Plataforma .NET 10 Zero-Dependencias + Firebird SQL",
            Location = new Point(138, 50),
            AutoSize = true,
            ForeColor = AppTheme.GoldAccent,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9.25f, FontStyle.Bold)
        };
        top.Controls.AddRange([logoCard, title, sub]);

        string empresa = string.IsNullOrWhiteSpace(services.BasculaActual.NombreEmpresa) ? "Instalación Industrial" : services.BasculaActual.NombreEmpresa;
        string fecha = services.ConfigActual.FechaInstalacion?.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? DateTime.Today.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);

        var lblInfo = new Label
        {
            Location = new Point(22, 108),
            Size = new Size(454, 128),
            Font = AppTheme.FontBody,
            ForeColor = AppTheme.TextPrimary,
            Text = $"Organización: {empresa}\r\n" +
                   $"Planta / Báscula: {services.BasculaActual.Descripcion}\r\n" +
                   $"Servidor BD Activo: {services.DatabaseSettings.DataSource}:{services.DatabaseSettings.Port}/{services.DatabaseSettings.DatabasePath}\r\n" +
                   $"Fecha Instalación: {fecha}\r\n" +
                   $"Modo Ejecución: {(AppConfigStore.IsPortableMode ? "Portable Autocontenido" : "Instalación Corporativa")}"
        };

        var btnLogs = new Button { Location = new Point(22, 248), Size = new Size(148, 34) };
        AppTheme.StyleSecondaryButton(btnLogs, AppIcon.Logs, "Logs de Errores", "Abrir visor de logs y diagnóstico", AppTheme.AccentAmber);
        btnLogs.Click += (_, _) => { using var f = new ErrorLogViewerDialog(); f.ShowDialog(this); };

        var btnClose = new Button { Location = new Point(362, 248), Size = new Size(114, 34) };
        AppTheme.StylePrimaryButton(btnClose, AppIcon.Check, "Aceptar", "Cerrar");
        btnClose.Click += (_, _) => Close();
        AcceptButton = btnClose;

        Controls.AddRange([top, lblInfo, btnLogs, btnClose]);
    }
}
