using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.UI.Theme;

namespace PesajeProCamion.App.UI.Controls;

/// <summary>Visor digital de 7 segmentos de alto contraste con indicadores de estabilidad, cero y errores.</summary>
public sealed class DigitalWeightDisplay : Control
{
    private int _weightKg;
    private bool _stable = true;
    private bool _isZero = true;
    private bool _isDynamic;
    private bool _commError;
    private bool _dataError;
    private bool _isSimulation;

    public DigitalWeightDisplay()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(340, 82);
        BackColor = AppTheme.LcdBackground;
    }

    public int WeightKg => _weightKg;
    public bool IsStable => _stable;

    public void UpdateReading(int weightKg, bool stable, bool isZero, bool isDynamic, bool commError, bool dataError, bool isSimulation)
    {
        _weightKg = weightKg;
        _stable = stable;
        _isZero = isZero;
        _isDynamic = isDynamic;
        _commError = commError;
        _dataError = dataError;
        _isSimulation = isSimulation;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(AppTheme.LcdBackground);

        using var borderPen = new Pen(Color.FromArgb(40, 62, 90), 2f);
        g.DrawRectangle(borderPen, 1, 1, Width - 2, Height - 2);

        // Render 7-segment style digits up to 6 characters
        string text = Math.Clamp(_weightKg, -99999, 999999).ToString(CultureInfo.InvariantCulture).PadLeft(6);
        int digitCount = 6;
        float displayAreaWidth = Width - 92f;
        float slotWidth = Math.Max(22f, displayAreaWidth / digitCount);
        float segLen = Math.Min(slotWidth * 0.62f, (Height - 26f) * 0.42f);
        float thick = Math.Max(3.2f, segLen * 0.22f);
        float topY = (Height - (segLen * 2 + thick)) / 2f;

        Color digitColor = _commError || _dataError
            ? AppTheme.AccentDanger
            : (_isDynamic ? Color.FromArgb(250, 204, 21) : AppTheme.LcdDigitOn);

        for (int i = 0; i < digitCount; i++)
        {
            float x = 14f + i * slotWidth;
            DrawSevenSegmentChar(g, text[i], x, topY, segLen, thick, digitColor, AppTheme.LcdDigitOff);
        }

        // Unit & status badges on the right
        float rx = Width - 74f;
        using var fUnit = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var fBadge = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        g.DrawString("KG", fUnit, Brushes.LightSteelBlue, rx, 10f);

        DrawStatusPill(g, fBadge, _stable ? "ESTABLE" : "MOVIM.", _stable ? AppTheme.AccentEmerald : AppTheme.AccentAmber, rx, 34f, 64f);
        if (_commError || _dataError)
        {
            DrawStatusPill(g, fBadge, _commError ? "ERR COM" : "ERR DATO", AppTheme.AccentDanger, rx, 54f, 64f);
        }
        else if (_isZero)
        {
            DrawStatusPill(g, fBadge, "CERO", Color.FromArgb(56, 189, 248), rx, 54f, 64f);
        }
        else if (_isSimulation)
        {
            DrawStatusPill(g, fBadge, "SIMULAC.", Color.FromArgb(139, 92, 246), rx, 54f, 64f);
        }
    }

    private static void DrawStatusPill(Graphics g, Font font, string text, Color color, float x, float y, float w)
    {
        using var bg = new SolidBrush(Color.FromArgb(45, color));
        using var fg = new SolidBrush(color);
        g.FillRectangle(bg, x, y, w, 17f);
        g.DrawString(text, font, fg, x + 4f, y + 2f);
    }

    private static void DrawSevenSegmentChar(Graphics g, char c, float x, float y, float len, float t, Color onColor, Color offColor)
    {
        int mask = c switch
        {
            '0' => 0b0111111,
            '1' => 0b0000110,
            '2' => 0b1011011,
            '3' => 0b1001111,
            '4' => 0b1100110,
            '5' => 0b1101101,
            '6' => 0b1111101,
            '7' => 0b0000111,
            '8' => 0b1111111,
            '9' => 0b1101111,
            '-' => 0b1000000,
            _ => 0
        };

        using var onBrush = new SolidBrush(onColor);
        using var offBrush = new SolidBrush(offColor);

        // a (top), b (top-right), c (bottom-right), d (bottom), e (bottom-left), f (top-left), g (middle)
        g.FillRectangle((mask & 1) != 0 ? onBrush : offBrush, x + t * 0.6f, y, len, t);
        g.FillRectangle((mask & 2) != 0 ? onBrush : offBrush, x + len + t * 0.5f, y + t * 0.6f, t, len);
        g.FillRectangle((mask & 4) != 0 ? onBrush : offBrush, x + len + t * 0.5f, y + len + t * 1.4f, t, len);
        g.FillRectangle((mask & 8) != 0 ? onBrush : offBrush, x + t * 0.6f, y + len * 2 + t * 1.2f, len, t);
        g.FillRectangle((mask & 16) != 0 ? onBrush : offBrush, x, y + len + t * 1.4f, t, len);
        g.FillRectangle((mask & 32) != 0 ? onBrush : offBrush, x, y + t * 0.6f, t, len);
        g.FillRectangle((mask & 64) != 0 ? onBrush : offBrush, x + t * 0.6f, y + len + t * 0.6f, len, t);
    }
}
