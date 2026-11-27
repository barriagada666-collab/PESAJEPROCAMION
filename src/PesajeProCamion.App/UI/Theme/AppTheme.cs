using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PesajeProCamion.App.UI.Theme;

/// <summary>
/// Paleta cromática corporativa Petrol Teal y Dorado Champagne (inspirada en carrusel_mockups + identidad Cambiaso)
/// con utilidades de diseño uniforme High-DPI para toda la aplicación.
/// </summary>
public static class AppTheme
{
    public static readonly Color NavyDark = Color.FromArgb(8, 45, 69);          // #082D45 Chrome Petrol profundo
    public static readonly Color TealDeep = Color.FromArgb(6, 57, 83);          // #063953
    public static readonly Color NavyPrimary = Color.FromArgb(0, 87, 129);      // #005781 Petrol Teal primario
    public static readonly Color SlateHeader = Color.FromArgb(14, 59, 82);      // #0E3B52
    public static readonly Color TealHover = Color.FromArgb(14, 116, 160);
    public static readonly Color GoldAccent = Color.FromArgb(204, 169, 105);    // #CCA969 Dorado corporativo
    public static readonly Color GoldSoft = Color.FromArgb(245, 239, 226);

    public static readonly Color BackgroundLight = Color.FromArgb(219, 228, 229); // #DBE4E5 Canvas mockup
    public static readonly Color SurfaceCard = Color.FromArgb(252, 253, 253);     // #FCFDFD Tarjeta limpia
    public static readonly Color RowAlternate = Color.FromArgb(240, 246, 247);
    public static readonly Color BorderSubtle = Color.FromArgb(192, 208, 214);
    public static readonly Color TextPrimary = Color.FromArgb(22, 36, 51);
    public static readonly Color TextMuted = Color.FromArgb(90, 110, 126);

    public static readonly Color AccentEmerald = Color.FromArgb(14, 159, 110);
    public static readonly Color AccentAmber = Color.FromArgb(217, 119, 6);
    public static readonly Color AccentDanger = Color.FromArgb(198, 40, 40);
    public static readonly Color AccentBlueSoft = Color.FromArgb(212, 234, 241);

    public static readonly Color LcdBackground = Color.FromArgb(9, 24, 38);
    public static readonly Color LcdDigitOn = Color.FromArgb(56, 239, 155);
    public static readonly Color LcdDigitOff = Color.FromArgb(20, 48, 68);

    public static readonly Font FontBody = new("Segoe UI", 9.25f, FontStyle.Regular);
    public static readonly Font FontBodyBold = new("Segoe UI", 9.25f, FontStyle.Bold);
    public static readonly Font FontButton = new("Segoe UI", 9f, FontStyle.Bold);
    public static readonly Font FontTitle = new("Segoe UI", 13.5f, FontStyle.Bold);
    public static readonly Font FontMono = new("Consolas", 9.25f, FontStyle.Regular);

    public static void StylePrimaryButton(Button btn, AppIcon icon, string text, string tooltip, ToolTip? tt = null)
    {
        ArgumentNullException.ThrowIfNull(btn);
        btn.Text = text;
        btn.Image = IconCatalog.Get(icon, 18, Color.White);
        btn.ImageAlign = ContentAlignment.MiddleLeft;
        btn.TextImageRelation = TextImageRelation.ImageBeforeText;
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.BorderColor = TealDeep;
        btn.FlatAppearance.MouseOverBackColor = TealHover;
        btn.BackColor = NavyPrimary;
        btn.ForeColor = Color.White;
        btn.Font = FontButton;
        btn.Cursor = Cursors.Hand;
        btn.Padding = new Padding(8, 2, 8, 2);
        tt?.SetToolTip(btn, tooltip);
    }

    public static void StyleSecondaryButton(Button btn, AppIcon icon, string text, string tooltip, Color? tint = null, ToolTip? tt = null)
    {
        ArgumentNullException.ThrowIfNull(btn);
        Color fg = tint ?? NavyDark;
        bool iconOnly = string.IsNullOrEmpty(text);
        btn.Text = text;
        btn.Image = IconCatalog.Get(icon, 18, fg);
        btn.ImageAlign = iconOnly ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft;
        btn.TextImageRelation = iconOnly ? TextImageRelation.Overlay : TextImageRelation.ImageBeforeText;
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderColor = BorderSubtle;
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.MouseOverBackColor = AccentBlueSoft;
        btn.BackColor = SurfaceCard;
        btn.ForeColor = fg;
        btn.Font = FontButton;
        btn.Cursor = Cursors.Hand;
        btn.Padding = iconOnly ? Padding.Empty : new Padding(6, 2, 6, 2);
        tt?.SetToolTip(btn, tooltip);
    }

    public static void StyleDangerButton(Button btn, AppIcon icon, string text, string tooltip, ToolTip? tt = null)
    {
        ArgumentNullException.ThrowIfNull(btn);
        btn.Text = text;
        btn.Image = IconCatalog.Get(icon, 18, Color.White);
        btn.ImageAlign = ContentAlignment.MiddleLeft;
        btn.TextImageRelation = TextImageRelation.ImageBeforeText;
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderSize = 0;
        btn.BackColor = AccentDanger;
        btn.ForeColor = Color.White;
        btn.Font = FontButton;
        btn.Cursor = Cursors.Hand;
        btn.Padding = new Padding(8, 2, 8, 2);
        tt?.SetToolTip(btn, tooltip);
    }

    public static void PaintTealGradient(Graphics g, Rectangle bounds, bool includeGoldBottomAccent = true)
    {
        ArgumentNullException.ThrowIfNull(g);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }
        using var brush = new LinearGradientBrush(bounds, NavyDark, NavyPrimary, LinearGradientMode.Horizontal);
        g.FillRectangle(brush, bounds);
        if (includeGoldBottomAccent)
        {
            using var goldPen = new Pen(GoldAccent, 2f);
            g.DrawLine(goldPen, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
        }
    }

    public static void ApplyFormIcon(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        string icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(icoPath))
        {
            form.Icon = new Icon(icoPath);
        }
    }

    public static void StyleDataGrid(DataGridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.BackgroundColor = SurfaceCard;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = BorderSubtle;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = TealDeep,
            ForeColor = Color.White,
            Font = FontBodyBold,
            Padding = new Padding(6, 6, 6, 6),
            SelectionBackColor = TealDeep,
            SelectionForeColor = Color.White
        };
        grid.ColumnHeadersHeight = 34;
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = SurfaceCard,
            ForeColor = TextPrimary,
            SelectionBackColor = AccentBlueSoft,
            SelectionForeColor = NavyDark,
            Font = FontBody,
            Padding = new Padding(4, 3, 4, 3)
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = RowAlternate,
            ForeColor = TextPrimary,
            SelectionBackColor = AccentBlueSoft,
            SelectionForeColor = NavyDark
        };
        grid.RowTemplate.Height = 28;
        grid.RowHeadersVisible = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ReadOnly = true;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    }

    public static ToolStripButton CreateToolButton(AppIcon icon, string text, string tooltip, Color? tint = null)
    {
        return new ToolStripButton(text)
        {
            Image = IconCatalog.Get(icon, 18, tint ?? NavyPrimary),
            ImageScaling = ToolStripItemImageScaling.None,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            ToolTipText = tooltip,
            Font = FontButton,
            Padding = new Padding(5, 3, 7, 3),
            ForeColor = TextPrimary
        };
    }

    public static ToolStripRenderer CreateMenuRenderer() => new CorporateMenuRenderer();

    private sealed class CorporateMenuRenderer() : ToolStripProfessionalRenderer(new CorporateColorTable())
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is MenuStrip)
            {
                using var b = new LinearGradientBrush(e.AffectedBounds, NavyDark, TealDeep, LinearGradientMode.Horizontal);
                e.Graphics.FillRectangle(b, e.AffectedBounds);
            }
            else
            {
                base.OnRenderToolStripBackground(e);
            }
        }
    }

    private sealed class CorporateColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => NavyPrimary;
        public override Color MenuItemSelectedGradientBegin => NavyPrimary;
        public override Color MenuItemSelectedGradientEnd => TealHover;
        public override Color MenuItemPressedGradientBegin => TealDeep;
        public override Color MenuItemPressedGradientEnd => NavyPrimary;
        public override Color MenuItemBorder => GoldAccent;
        public override Color ToolStripDropDownBackground => SurfaceCard;
        public override Color ImageMarginGradientBegin => BackgroundLight;
        public override Color ImageMarginGradientMiddle => BackgroundLight;
        public override Color ImageMarginGradientEnd => BackgroundLight;
    }
}
