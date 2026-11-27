using System.Drawing;
using System.Windows.Forms;
using PesajeProCamion.App.UI.Theme;

namespace PesajeProCamion.App.UI.Dialogs;

/// <summary>
/// Pantalla de arranque corporativa Petrol Teal + Logo Cambiaso inspirada en el diseño de referencia.
/// </summary>
public sealed class SplashForm : Form
{
    private readonly Panel _progressBar = new();
    private int _progressValue = 25;
    private readonly Label _lblStatus = new();

    public SplashForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(612, 316);
        BackColor = AppTheme.NavyDark;
        ShowInTaskbar = false;
        AppTheme.ApplyFormIcon(this);
        DoubleBuffered = true;

        var leftPanel = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(236, 316),
            BackColor = AppTheme.NavyDark
        };
        leftPanel.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, leftPanel.ClientRectangle, includeGoldBottomAccent: false);

        var picGear = new PictureBox
        {
            Location = new Point(38, 30),
            Size = new Size(158, 158),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Image = IconCatalog.RenderIndustrialSplashEmblem(158)
        };
        var lblDiv = new Label
        {
            Location = new Point(10, 212),
            Size = new Size(215, 22),
            Text = "DIVISIÓN PESAJE INDUSTRIAL",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
        };
        var lblLoc = new Label
        {
            Location = new Point(10, 236),
            Size = new Size(215, 20),
            Text = "Control de Báscula y Ejes",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = AppTheme.GoldAccent,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.25f, FontStyle.Regular)
        };
        leftPanel.Controls.AddRange([picGear, lblDiv, lblLoc]);

        var rightPanel = new Panel
        {
            Location = new Point(236, 0),
            Size = new Size(376, 316),
            BackColor = AppTheme.SurfaceCard
        };
        var picLogo = new PictureBox
        {
            Location = new Point(24, 18),
            Size = new Size(135, 58),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = IconCatalog.GetCorporateLogo()
        };
        var lblTitle = new Label
        {
            Location = new Point(24, 88),
            Size = new Size(328, 30),
            Text = "PesajeProCamion",
            ForeColor = AppTheme.NavyDark,
            Font = new Font("Segoe UI", 16f, FontStyle.Bold)
        };
        var lblSub = new Label
        {
            Location = new Point(24, 118),
            Size = new Size(328, 22),
            Text = "Sistema Integral de Pesaje de Camiones",
            ForeColor = AppTheme.NavyPrimary,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };

        _progressBar.Location = new Point(24, 156);
        _progressBar.Size = new Size(328, 16);
        _progressBar.BackColor = AppTheme.BackgroundLight;
        _progressBar.BorderStyle = BorderStyle.FixedSingle;
        _progressBar.Paint += (_, e) =>
        {
            int fillW = (int)Math.Round((_progressBar.Width - 2) * (_progressValue / 100.0));
            if (fillW > 0)
            {
                AppTheme.PaintTealGradient(e.Graphics, new Rectangle(1, 1, fillW, _progressBar.Height - 2), includeGoldBottomAccent: false);
            }
        };

        _lblStatus.Location = new Point(24, 184);
        _lblStatus.Size = new Size(328, 44);
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _lblStatus.Font = AppTheme.FontBody;
        _lblStatus.Text = "Inicializando servicios de báscula y base de datos...";

        var lblVer = new Label
        {
            Location = new Point(24, 250),
            Size = new Size(328, 18),
            TextAlign = ContentAlignment.MiddleRight,
            Text = "Versión 2.2.0 LTS (.NET 10 Zero-Deps)",
            ForeColor = AppTheme.NavyPrimary,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
        };
        var lblCopy = new Label
        {
            Location = new Point(24, 270),
            Size = new Size(328, 18),
            TextAlign = ContentAlignment.MiddleRight,
            Text = "Plataforma Corporativa Industrial",
            ForeColor = AppTheme.TextMuted,
            Font = new Font("Segoe UI", 8f)
        };
        rightPanel.Controls.AddRange([picLogo, lblTitle, lblSub, _progressBar, _lblStatus, lblVer, lblCopy]);

        Controls.AddRange([leftPanel, rightPanel]);
    }

    public void UpdateProgress(int percent, string message)
    {
        if (IsDisposed) return;
        _progressValue = Math.Clamp(percent, 0, 100);
        _lblStatus.Text = message;
        _progressBar.Invalidate();
        Application.DoEvents();
    }
}
