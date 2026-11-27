using System.Drawing;
using System.Windows.Forms;

namespace PesajeProCamion.App.UI.Theme;

/// <summary>Paleta cromática y utilidades de diseño consistente y alto contraste para toda la aplicación.</summary>
public static class AppTheme
{
    public static readonly Color NavyDark = Color.FromArgb(15, 39, 68);
    public static readonly Color NavyPrimary = Color.FromArgb(29, 78, 137);
    public static readonly Color SlateHeader = Color.FromArgb(30, 41, 59);
    public static readonly Color BackgroundLight = Color.FromArgb(244, 246, 249);
    public static readonly Color SurfaceCard = Color.White;
    public static readonly Color BorderSubtle = Color.FromArgb(214, 222, 234);
    public static readonly Color TextPrimary = Color.FromArgb(22, 32, 48);
    public static readonly Color TextMuted = Color.FromArgb(95, 110, 130);

    public static readonly Color AccentEmerald = Color.FromArgb(16, 165, 110);
    public static readonly Color AccentAmber = Color.FromArgb(217, 119, 6);
    public static readonly Color AccentDanger = Color.FromArgb(205, 42, 42);
    public static readonly Color AccentBlueSoft = Color.FromArgb(232, 241, 252);

    public static readonly Color LcdBackground = Color.FromArgb(11, 18, 32);
    public static readonly Color LcdDigitOn = Color.FromArgb(34, 245, 148);
    public static readonly Color LcdDigitOff = Color.FromArgb(24, 42, 60);

    public static void StylePrimaryButton(Button btn, AppIcon icon, string text, string tooltip, ToolTip? tt = null)
    {
        ArgumentNullException.ThrowIfNull(btn);
        btn.Text = text;
        btn.Image = IconCatalog.Get(icon, 18, Color.White);
        btn.ImageAlign = ContentAlignment.MiddleLeft;
        btn.TextImageRelation = TextImageRelation.ImageBeforeText;
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderSize = 0;
        btn.BackColor = NavyPrimary;
        btn.ForeColor = Color.White;
        btn.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        btn.Cursor = Cursors.Hand;
        btn.Padding = new Padding(8, 2, 8, 2);
        tt?.SetToolTip(btn, tooltip);
    }

    public static void StyleSecondaryButton(Button btn, AppIcon icon, string text, string tooltip, Color? tint = null, ToolTip? tt = null)
    {
        ArgumentNullException.ThrowIfNull(btn);
        Color fg = tint ?? TextPrimary;
        btn.Text = text;
        btn.Image = IconCatalog.Get(icon, 18, fg);
        btn.ImageAlign = ContentAlignment.MiddleLeft;
        btn.TextImageRelation = TextImageRelation.ImageBeforeText;
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderColor = BorderSubtle;
        btn.FlatAppearance.BorderSize = 1;
        btn.BackColor = SurfaceCard;
        btn.ForeColor = fg;
        btn.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        btn.Cursor = Cursors.Hand;
        btn.Padding = new Padding(6, 2, 6, 2);
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
        btn.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        btn.Cursor = Cursors.Hand;
        btn.Padding = new Padding(8, 2, 8, 2);
        tt?.SetToolTip(btn, tooltip);
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
            BackColor = NavyDark,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
            Padding = new Padding(6, 6, 6, 6),
            SelectionBackColor = NavyDark,
            SelectionForeColor = Color.White
        };
        grid.ColumnHeadersHeight = 34;
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = SurfaceCard,
            ForeColor = TextPrimary,
            SelectionBackColor = AccentBlueSoft,
            SelectionForeColor = NavyDark,
            Font = new Font("Segoe UI", 9.25f),
            Padding = new Padding(4, 3, 4, 3)
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(248, 250, 253),
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
            Image = IconCatalog.Get(icon, 18, tint ?? NavyDark),
            ImageScaling = ToolStripItemImageScaling.None,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            ToolTipText = tooltip,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Padding = new Padding(4, 3, 6, 3),
            ForeColor = TextPrimary
        };
    }
}
