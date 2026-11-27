using System.Drawing;
using System.Drawing.Drawing2D;

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
    Tare
}

/// <summary>Generador vectorial nítido de íconos vectoriales High-DPI y diagramas de camión para formularios y botones.</summary>
public static class IconCatalog
{
    private static readonly Dictionary<(AppIcon, int, int), Bitmap> Cache = new();

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
        }

        Cache[key] = bmp;
        return bmp;
    }

    public static Bitmap RenderTruckBadge(int width, int height)
    {
        var bmp = new Bitmap(width, height);
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var platformBrush = new SolidBrush(AppTheme.AccentAmber);
        using var trailerBrush = new SolidBrush(Color.White);
        using var cabBrush = new SolidBrush(Color.FromArgb(224, 232, 244));
        using var glassBrush = new SolidBrush(AppTheme.NavyPrimary);
        using var wheelBrush = new SolidBrush(Color.FromArgb(30, 41, 59));
        using var rimBrush = new SolidBrush(Color.LightGray);

        float pad = 4f;
        float uw = width - 2 * pad;
        float uh = height - 2 * pad;

        // Trailer
        g.FillRectangle(trailerBrush, pad + uw * 0.04f, pad + uh * 0.14f, uw * 0.58f, uh * 0.54f);
        // Cab
        g.FillRectangle(cabBrush, pad + uw * 0.64f, pad + uh * 0.28f, uw * 0.31f, uh * 0.40f);
        g.FillRectangle(glassBrush, pad + uw * 0.74f, pad + uh * 0.34f, uw * 0.16f, uh * 0.16f);
        // Platform
        g.FillRectangle(platformBrush, pad, pad + uh * 0.80f, uw, uh * 0.12f);
        // Wheels
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
