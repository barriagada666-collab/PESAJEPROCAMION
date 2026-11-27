using System.Drawing;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Theme;

namespace PesajeProCamion.App.UI.Dialogs;

/// <summary>Información simplificada y dinámica de la versión e instalación actual.</summary>
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
        ClientSize = new Size(460, 260);
        BackColor = AppTheme.SurfaceCard;

        var top = new Panel { Dock = DockStyle.Top, Height = 86, BackColor = AppTheme.NavyDark };
        var pic = new PictureBox { Location = new Point(16, 14), Size = new Size(88, 56), SizeMode = PictureBoxSizeMode.Zoom, Image = IconCatalog.RenderTruckBadge(88, 56) };
        var title = new Label { Text = "PESAJE PRO CAMIÓN v2.0", Location = new Point(116, 16), AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 14f, FontStyle.Bold) };
        var sub = new Label { Text = "Plataforma .NET 10 + Firebird SQL", Location = new Point(118, 46), AutoSize = true, ForeColor = Color.FromArgb(190, 212, 240), Font = new Font("Segoe UI", 9.25f) };
        top.Controls.AddRange([pic, title, sub]);

        string empresa = string.IsNullOrWhiteSpace(services.BasculaActual.NombreEmpresa) ? "Instalación Estándar" : services.BasculaActual.NombreEmpresa;
        string fecha = services.ConfigActual.FechaInstalacion?.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? DateTime.Today.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);

        var lblInfo = new Label
        {
            Location = new Point(22, 102),
            Size = new Size(416, 102),
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = AppTheme.TextPrimary,
            Text = $"Licenciado a: {empresa}\r\n" +
                   $"Planta / Báscula: {services.BasculaActual.Descripcion}\r\n" +
                   $"Fecha Instalación: {fecha}\r\n" +
                   "Sistema unificado de pesaje comercial y control de ejes MOP."
        };

        var btnClose = new Button { Location = new Point(332, 212), Size = new Size(106, 32) };
        AppTheme.StylePrimaryButton(btnClose, AppIcon.Check, "Aceptar", "Cerrar");
        btnClose.Click += (_, _) => Close();
        AcceptButton = btnClose;

        Controls.AddRange([top, lblInfo, btnClose]);
    }
}
