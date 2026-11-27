using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace PesajeProCamion.App.UI.Theme;

public enum AppIcon
{
    Scale,
    Truck,
    Add,
    Save,
    Delete,
    Print,
    Cancel,
    Search,
    Filter,
    ExportExcel,
    ExportDbf,
    User,
    Lock,
    Key,
    Settings,
    SerialPort,
    AxleMop,
    Client,
    Product,
    Driver,
    Carrier,
    Origin,
    Destination,
    List,
    Info,
    Refresh,
    Check,
    Warning,
    First,
    Prev,
    Next,
    Last,
    Power,
    Audit,
    Shift,
    Migrate,
    ManualWeight,
    Tare,
    Database,
    Logs,
    Pdf,
    ViewTicket,
    EditTicket,
    Duplicate,
    Chart,
    Eye,
    Copy,
    Toggle,
    Split
}

/// <summary>
/// Generador vectorial High-DPI y cargador del logo corporativo Cambiaso / emblema industrial
/// desde recursos embebidos (100% compatible con despliegue portable single-file).
/// </summary>
public static class IconCatalog
{
    private static readonly Dictionary<(AppIcon, int, int), Bitmap> Cache = new();
    private static Image? _corporateLogo;

    public static Image? GetCorporateLogo()
    {
        if (_corporateLogo is not null)
        {
            return _corporateLogo;
        }

        string diskPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo_cambiaso.png");
        if (File.Exists(diskPath))
        {
            using var fs = new FileStream(diskPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _corporateLogo = Image.FromStream(fs);
            return _corporateLogo;
        }

        Assembly asm = typeof(IconCatalog).Assembly;
        string? resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("logo_cambiaso.png", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(resName))
        {
            using Stream? s = asm.GetManifestResourceStream(resName);
            if (s is not null)
            {
                _corporateLogo = Image.FromStream(s);
                return _corporateLogo;
            }
        }
        return null;
    }

    public static Image Get(AppIcon icon, int size, Color color)
    {
        (AppIcon, int, int) key = (icon, size, color.ToArgb());
        if (Cache.TryGetValue(key, out Bitmap? cached))
        {
            return cached;
        }

        var bmp = new Bitmap(size, size);
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        float s = size;
        float pad = s * 0.14f;
        float w = s - 2 * pad;
        using var pen = new Pen(color, Math.Max(1.6f, s / 10f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var brush = new SolidBrush(color);

        switch (icon)
        {
            case AppIcon.Scale:
                g.DrawRectangle(pen, pad + 1, pad, w - 2, w * 0.45f);
                g.DrawLine(pen, s * 0.5f, pad + w * 0.45f, s * 0.5f, s - pad - 2);
                g.DrawLine(pen, pad, s - pad - 1, s - pad, s - pad - 1);
                g.FillEllipse(brush, s * 0.43f, pad + w * 0.16f, s * 0.14f, s * 0.14f);
                break;

            case AppIcon.Truck:
                g.DrawRectangle(pen, pad, pad + w * 0.20f, w * 0.62f, w * 0.45f);
                g.DrawRectangle(pen, pad + w * 0.62f, pad + w * 0.34f, w * 0.34f, w * 0.31f);
                g.FillEllipse(brush, pad + w * 0.08f, pad + w * 0.62f, w * 0.20f, w * 0.20f);
                g.FillEllipse(brush, pad + w * 0.42f, pad + w * 0.62f, w * 0.20f, w * 0.20f);
                g.FillEllipse(brush, pad + w * 0.72f, pad + w * 0.62f, w * 0.20f, w * 0.20f);
                break;

            case AppIcon.Add:
                g.DrawLine(pen, s * 0.5f, pad + 1, s * 0.5f, s - pad - 1);
                g.DrawLine(pen, pad + 1, s * 0.5f, s - pad - 1, s * 0.5f);
                break;

            case AppIcon.Save:
                g.DrawRectangle(pen, pad + 1, pad + 1, w - 2, w - 2);
                g.FillRectangle(brush, pad + w * 0.22f, pad + 1, w * 0.48f, w * 0.28f);
                g.DrawRectangle(pen, pad + w * 0.22f, pad + w * 0.54f, w * 0.56f, w * 0.42f);
                break;

            case AppIcon.Delete:
                g.DrawLine(pen, pad + 2, pad + 3, s - pad - 2, pad + 3);
                g.DrawLine(pen, s * 0.38f, pad, s * 0.62f, pad);
                g.DrawRectangle(pen, pad + 3, pad + 3, w - 6, w - 3);
                g.DrawLine(pen, s * 0.42f, pad + 6, s * 0.42f, s - pad - 2);
                g.DrawLine(pen, s * 0.58f, pad + 6, s * 0.58f, s - pad - 2);
                break;

            case AppIcon.Print:
                g.DrawRectangle(pen, pad + 2, pad, w - 4, w * 0.32f);
                g.DrawRectangle(pen, pad, pad + w * 0.30f, w, w * 0.42f);
                g.DrawRectangle(pen, pad + 2, pad + w * 0.56f, w - 4, w * 0.42f);
                break;

            case AppIcon.Cancel:
                g.DrawLine(pen, pad + 2, pad + 2, s - pad - 2, s - pad - 2);
                g.DrawLine(pen, s - pad - 2, pad + 2, pad + 2, s - pad - 2);
                break;

            case AppIcon.Search:
                g.DrawEllipse(pen, pad + 1, pad + 1, w * 0.68f, w * 0.68f);
                g.DrawLine(pen, pad + w * 0.60f, pad + w * 0.60f, s - pad - 1, s - pad - 1);
                break;

            case AppIcon.Filter:
                g.DrawPolygon(pen,
                [
                    new PointF(pad, pad + 1),
                    new PointF(s - pad, pad + 1),
                    new PointF(s * 0.58f, s * 0.55f),
                    new PointF(s * 0.58f, s - pad),
                    new PointF(s * 0.42f, s - pad - 1),
                    new PointF(s * 0.42f, s * 0.55f)
                ]);
                break;

            case AppIcon.ExportExcel:
            case AppIcon.ExportDbf:
                g.DrawRectangle(pen, pad + 1, pad + 1, w - 2, w - 2);
                g.DrawLine(pen, pad + w * 0.45f, pad + 1, pad + w * 0.45f, s - pad - 1);
                g.DrawLine(pen, pad + 1, pad + w * 0.35f, s - pad - 1, pad + w * 0.35f);
                g.DrawLine(pen, pad + 1, pad + w * 0.68f, s - pad - 1, pad + w * 0.68f);
                break;

            case AppIcon.User:
            case AppIcon.Driver:
                g.DrawEllipse(pen, s * 0.32f, pad + 1, s * 0.36f, s * 0.36f);
                g.DrawArc(pen, pad + 1, s * 0.52f, w - 2, w * 0.60f, 180, 180);
                break;

            case AppIcon.Lock:
                g.DrawRectangle(pen, pad + 2, s * 0.46f, w - 4, w * 0.52f);
                g.DrawArc(pen, pad + w * 0.22f, pad + 1, w * 0.56f, w * 0.52f, 180, 180);
                break;

            case AppIcon.Key:
                g.DrawEllipse(pen, pad + 1, s * 0.28f, w * 0.42f, w * 0.42f);
                g.DrawLine(pen, pad + w * 0.42f, s * 0.50f, s - pad - 1, s * 0.50f);
                g.DrawLine(pen, s - pad - 3, s * 0.50f, s - pad - 3, s * 0.68f);
                break;

            case AppIcon.Settings:
                g.DrawEllipse(pen, pad + w * 0.22f, pad + w * 0.22f, w * 0.56f, w * 0.56f);
                for (int i = 0; i < 8; i++)
                {
                    double ang = i * Math.PI / 4.0;
                    float x1 = s * 0.5f + (float)Math.Cos(ang) * w * 0.32f;
                    float y1 = s * 0.5f + (float)Math.Sin(ang) * w * 0.32f;
                    float x2 = s * 0.5f + (float)Math.Cos(ang) * w * 0.48f;
                    float y2 = s * 0.5f + (float)Math.Sin(ang) * w * 0.48f;
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
                break;

            case AppIcon.SerialPort:
                g.DrawRectangle(pen, pad + 1, pad + w * 0.18f, w - 2, w * 0.64f);
                g.FillEllipse(brush, pad + w * 0.22f, pad + w * 0.38f, 2.5f, 2.5f);
                g.FillEllipse(brush, s * 0.48f, pad + w * 0.38f, 2.5f, 2.5f);
                g.FillEllipse(brush, pad + w * 0.72f, pad + w * 0.38f, 2.5f, 2.5f);
                break;

            case AppIcon.AxleMop:
                g.DrawLine(pen, pad, s * 0.55f, s - pad, s * 0.55f);
                g.FillEllipse(brush, pad, s * 0.40f, w * 0.25f, w * 0.30f);
                g.FillEllipse(brush, s * 0.40f, s * 0.40f, w * 0.25f, w * 0.30f);
                g.FillEllipse(brush, s - pad - w * 0.25f, s * 0.40f, w * 0.25f, w * 0.30f);
                break;

            case AppIcon.Client:
            case AppIcon.Carrier:
                g.DrawRectangle(pen, pad + 1, pad + 2, w - 2, w - 3);
                g.DrawLine(pen, pad + 4, pad + w * 0.36f, s - pad - 4, pad + w * 0.36f);
                g.DrawLine(pen, pad + 4, pad + w * 0.62f, s - pad - 6, pad + w * 0.62f);
                break;

            case AppIcon.Product:
                g.DrawPolygon(pen,
                [
                    new PointF(s * 0.5f, pad),
                    new PointF(s - pad, pad + w * 0.28f),
                    new PointF(s - pad, s - pad - w * 0.22f),
                    new PointF(s * 0.5f, s - pad),
                    new PointF(pad, s - pad - w * 0.22f),
                    new PointF(pad, pad + w * 0.28f)
                ]);
                g.DrawLine(pen, pad, pad + w * 0.28f, s * 0.5f, s * 0.5f);
                g.DrawLine(pen, s - pad, pad + w * 0.28f, s * 0.5f, s * 0.5f);
                g.DrawLine(pen, s * 0.5f, s * 0.5f, s * 0.5f, s - pad);
                break;

            case AppIcon.Origin:
            case AppIcon.Destination:
                g.DrawEllipse(pen, pad + w * 0.18f, pad, w * 0.64f, w * 0.64f);
                g.DrawLine(pen, s * 0.5f, pad + w * 0.64f, s * 0.5f, s - pad);
                break;

            case AppIcon.List:
            case AppIcon.Audit:
            case AppIcon.Shift:
            case AppIcon.Logs:
                g.DrawLine(pen, pad + 1, pad + w * 0.22f, s - pad - 1, pad + w * 0.22f);
                g.DrawLine(pen, pad + 1, s * 0.50f, s - pad - 1, s * 0.50f);
                g.DrawLine(pen, pad + 1, pad + w * 0.78f, s - pad - 1, pad + w * 0.78f);
                break;

            case AppIcon.Info:
                g.DrawEllipse(pen, pad + 1, pad + 1, w - 2, w - 2);
                g.DrawLine(pen, s * 0.5f, s * 0.44f, s * 0.5f, s - pad - 3);
                g.FillEllipse(brush, s * 0.44f, pad + 3, 2.5f, 2.5f);
                break;

            case AppIcon.Refresh:
            case AppIcon.Migrate:
                g.DrawArc(pen, pad + 1, pad + 1, w - 2, w - 2, 30, 290);
                g.DrawLine(pen, s - pad - 4, pad + 2, s - pad, pad + 6);
                break;

            case AppIcon.Database:
                g.DrawEllipse(pen, pad + 1, pad, w - 2, w * 0.34f);
                g.DrawLine(pen, pad + 1, pad + w * 0.17f, pad + 1, s - pad - w * 0.17f);
                g.DrawLine(pen, s - pad - 1, pad + w * 0.17f, s - pad - 1, s - pad - w * 0.17f);
                g.DrawArc(pen, pad + 1, s - pad - w * 0.34f, w - 2, w * 0.34f, 0, 180);
                break;

            case AppIcon.Check:
                g.DrawLine(pen, pad + 1, s * 0.55f, s * 0.42f, s - pad - 2);
                g.DrawLine(pen, s * 0.42f, s - pad - 2, s - pad - 1, pad + 2);
                break;

            case AppIcon.Warning:
                g.DrawPolygon(pen,
                [
                    new PointF(s * 0.5f, pad + 1),
                    new PointF(s - pad - 1, s - pad - 1),
                    new PointF(pad + 1, s - pad - 1)
                ]);
                g.DrawLine(pen, s * 0.5f, pad + w * 0.36f, s * 0.5f, pad + w * 0.64f);
                break;

            case AppIcon.First:
                g.DrawLine(pen, pad + 2, pad + 2, pad + 2, s - pad - 2);
                g.DrawLine(pen, s - pad - 2, pad + 2, s * 0.42f, s * 0.5f);
                g.DrawLine(pen, s * 0.42f, s * 0.5f, s - pad - 2, s - pad - 2);
                break;

            case AppIcon.Prev:
                g.DrawLine(pen, s * 0.65f, pad + 2, s * 0.35f, s * 0.5f);
                g.DrawLine(pen, s * 0.35f, s * 0.5f, s * 0.65f, s - pad - 2);
                break;

            case AppIcon.Next:
                g.DrawLine(pen, s * 0.35f, pad + 2, s * 0.65f, s * 0.5f);
                g.DrawLine(pen, s * 0.65f, s * 0.5f, s * 0.35f, s - pad - 2);
                break;

            case AppIcon.Last:
                g.DrawLine(pen, s - pad - 2, pad + 2, s - pad - 2, s - pad - 2);
                g.DrawLine(pen, pad + 2, pad + 2, s * 0.58f, s * 0.5f);
                g.DrawLine(pen, s * 0.58f, s * 0.5f, pad + 2, s - pad - 2);
                break;

            case AppIcon.Power:
                g.DrawArc(pen, pad + 1, pad + 2, w - 2, w - 2, -55, 290);
                g.DrawLine(pen, s * 0.5f, pad, s * 0.5f, s * 0.52f);
                break;

            case AppIcon.ManualWeight:
            case AppIcon.Tare:
                g.DrawRectangle(pen, pad + 1, pad + 2, w - 2, w - 3);
                g.DrawLine(pen, pad + 4, s * 0.50f, s - pad - 4, s * 0.50f);
                g.DrawLine(pen, s * 0.50f, pad + 4, s * 0.50f, s * 0.72f);
                break;

            case AppIcon.Pdf:
            case AppIcon.ViewTicket:
                g.DrawRectangle(pen, pad + 2, pad, w - 4, w);
                g.DrawLine(pen, pad + 5, pad + w * 0.30f, s - pad - 5, pad + w * 0.30f);
                g.DrawLine(pen, pad + 5, pad + w * 0.52f, s - pad - 5, pad + w * 0.52f);
                g.DrawLine(pen, pad + 5, pad + w * 0.74f, s - pad - 7, pad + w * 0.74f);
                break;

            case AppIcon.EditTicket:
                g.DrawRectangle(pen, pad + 1, pad + 3, w - 4, w - 4);
                g.DrawLine(pen, s * 0.44f, s * 0.56f, s - pad, pad + 1);
                break;

            case AppIcon.Duplicate:
            case AppIcon.Copy:
                g.DrawRectangle(pen, pad + 4, pad + 4, w - 5, w - 5);
                g.DrawLine(pen, pad, pad, s - pad - 5, pad);
                g.DrawLine(pen, pad, pad, pad, s - pad - 5);
                break;

            case AppIcon.Split:
                g.DrawLine(pen, pad + 1, s * 0.5f, s * 0.45f, s * 0.5f);
                g.DrawLine(pen, s * 0.45f, s * 0.5f, s - pad - 2, pad + 3);
                g.DrawLine(pen, s * 0.45f, s * 0.5f, s - pad - 2, s - pad - 3);
                g.FillEllipse(brush, s - pad - 4, pad + 1, 4, 4);
                g.FillEllipse(brush, s - pad - 4, s - pad - 5, 4, 4);
                break;

            case AppIcon.Chart:
                g.DrawLine(pen, pad + 1, s - pad - 1, s - pad - 1, s - pad - 1);
                g.FillRectangle(brush, pad + 2, s * 0.52f, w * 0.22f, s - pad - 2 - s * 0.52f);
                g.FillRectangle(brush, pad + w * 0.38f, pad + 2, w * 0.22f, s - pad - 2 - (pad + 2));
                g.FillRectangle(brush, pad + w * 0.74f, s * 0.34f, w * 0.22f, s - pad - 2 - s * 0.34f);
                break;

            case AppIcon.Eye:
                g.DrawArc(pen, pad, pad + w * 0.15f, w, w * 0.70f, 180, 180);
                g.DrawArc(pen, pad, pad + w * 0.15f, w, w * 0.70f, 0, 180);
                g.FillEllipse(brush, s * 0.38f, s * 0.38f, s * 0.24f, s * 0.24f);
                break;

            case AppIcon.Toggle:
                g.DrawRectangle(pen, pad + 1, s * 0.28f, w - 2, w * 0.44f);
                g.FillEllipse(brush, s - pad - w * 0.40f, s * 0.33f, w * 0.34f, w * 0.34f);
                break;
        }

        Cache[key] = bmp;
        return bmp;
    }

    public static Bitmap RenderIndustrialSplashEmblem(int size)
    {
        var bmp = new Bitmap(size, size);
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        float c = size / 2f;
        float outerR = size * 0.44f;
        using var gearPen = new Pen(Color.FromArgb(190, 214, 224), Math.Max(3f, size * 0.055f));
        for (int i = 0; i < 16; i++)
        {
            double ang = i * (Math.PI * 2.0 / 16.0);
            float x1 = c + (float)Math.Cos(ang) * (outerR * 0.85f);
            float y1 = c + (float)Math.Sin(ang) * (outerR * 0.85f);
            float x2 = c + (float)Math.Cos(ang) * outerR;
            float y2 = c + (float)Math.Sin(ang) * outerR;
            g.DrawLine(gearPen, x1, y1, x2, y2);
        }

        using var ringBrush = new SolidBrush(AppTheme.NavyDark);
        using var ringPen = new Pen(AppTheme.TealHover, Math.Max(4f, size * 0.06f));
        g.FillEllipse(ringBrush, c - outerR * 0.78f, c - outerR * 0.78f, outerR * 1.56f, outerR * 1.56f);
        g.DrawEllipse(ringPen, c - outerR * 0.78f, c - outerR * 0.78f, outerR * 1.56f, outerR * 1.56f);

        using var goldPen = new Pen(AppTheme.GoldAccent, Math.Max(2.2f, size * 0.025f));
        g.DrawEllipse(goldPen, c - outerR * 0.52f, c - outerR * 0.52f, outerR * 1.04f, outerR * 1.04f);

        Image? logo = GetCorporateLogo();
        if (logo is not null)
        {
            // Tomar el emblema dorado superior del PNG oficial y centrarlo dentro del engranaje
            var srcRect = new RectangleF(logo.Width * 0.30f, 0f, logo.Width * 0.40f, logo.Height * 0.44f);
            float inner = outerR * 0.82f;
            var dstRect = new RectangleF(c - inner / 2f, c - inner / 2f, inner, inner);
            g.DrawImage(logo, dstRect, srcRect, GraphicsUnit.Pixel);
        }

        return bmp;
    }

    public static Bitmap RenderTruckBadge(int width, int height)
    {
        var bmp = new Bitmap(width, height);
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var platformBrush = new SolidBrush(AppTheme.GoldAccent);
        using var trailerBrush = new SolidBrush(Color.White);
        using var cabBrush = new SolidBrush(Color.FromArgb(224, 236, 240));
        using var glassBrush = new SolidBrush(AppTheme.NavyPrimary);
        using var wheelBrush = new SolidBrush(AppTheme.NavyDark);
        using var rimBrush = new SolidBrush(Color.LightGray);

        float pad = 4f;
        float uw = width - 2 * pad;
        float uh = height - 2 * pad;

        g.FillRectangle(trailerBrush, pad + uw * 0.04f, pad + uh * 0.14f, uw * 0.58f, uh * 0.54f);
        g.FillRectangle(cabBrush, pad + uw * 0.64f, pad + uh * 0.28f, uw * 0.31f, uh * 0.40f);
        g.FillRectangle(glassBrush, pad + uw * 0.74f, pad + uh * 0.34f, uw * 0.16f, uh * 0.16f);
        g.FillRectangle(platformBrush, pad, pad + uh * 0.80f, uw, uh * 0.12f);

        foreach (float cx in new[] { 0.18f, 0.48f, 0.78f })
        {
            float r = Math.Min(uw, uh) * 0.16f;
            float px = pad + uw * cx - r * 0.5f;
            float py = pad + uh * 0.62f;
            g.FillEllipse(wheelBrush, px, py, r, r);
            g.FillEllipse(rimBrush, px + r * 0.28f, py + r * 0.28f, r * 0.44f, r * 0.44f);
        }
        return bmp;
    }
}
