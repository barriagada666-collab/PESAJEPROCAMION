using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;
using PesajeProCamion.Hardware.Printing;

namespace PesajeProCamion.App.UI.Management;

/// <summary>
/// Visualizador gráfico corporativo de Ticket de Pesaje (completado, en proceso o anulado)
/// con selección de copias, impresión directa, vista previa nativa, exportación PDF y edición directa.
/// </summary>
public sealed class TicketViewerDialog : Form
{
    private readonly AppServices _services;
    private readonly ToolTip _tt = new();
    private readonly NumericUpDown _numCopias = new();
    private readonly Panel _ticketCanvas = new();
    private readonly Button _btnModify = new();
    private Pesaje _pesaje;
    private readonly bool _isEnProceso;

    public TicketViewerDialog(AppServices services, Pesaje pesaje, bool isEnProceso = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(pesaje);
        _services = services;
        _pesaje = pesaje;
        _isEnProceso = isEnProceso;

        Text = $"Visualizar Ticket — Folio #{pesaje.FolioPesaje}";
        AppTheme.ConfigureResponsiveDialog(this, 920, 740, 820, 650, resizable: true);
        BuildUi();
    }

    public bool Modified { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tt.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 50 };
        header.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, header.ClientRectangle);
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 16, 0),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = AppTheme.FontTitle,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = $"Visor de Ticket Comercial — Folio #{_pesaje.FolioPesaje}"
        });

        var actionsBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            BackColor = AppTheme.SurfaceCard,
            Padding = new Padding(12, 8, 12, 8)
        };
        var actionsRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var lblCopies = new Label { Text = "Copias:", AutoSize = true, Font = AppTheme.FontBodyBold, Margin = new Padding(0, 9, 4, 0) };
        _numCopias.Width = 54;
        _numCopias.Minimum = 1;
        _numCopias.Maximum = 20;
        _numCopias.Value = Math.Clamp(_services.ConfigActual.NroCopiasTicket, 1, 20);
        _numCopias.Margin = new Padding(0, 5, 10, 0);

        var btnPrint = new Button { Size = new Size(146, 36), Margin = new Padding(0, 0, 8, 0) };
        AppTheme.StylePrimaryButton(btnPrint, AppIcon.Print, "Imprimir / Vista", "Abrir vista previa e imprimir copias seleccionadas", _tt);
        btnPrint.Enabled = PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ImprimirTicket);
        btnPrint.Click += (_, _) => PrintPreviewTicket();

        var btnPdf = new Button { Size = new Size(134, 36), Margin = new Padding(0, 0, 8, 0) };
        AppTheme.StyleSecondaryButton(btnPdf, AppIcon.Pdf, "Exportar PDF", "Guardar ticket visual en documento PDF", AppTheme.AccentDanger, _tt);
        btnPdf.Click += (_, _) => ExportTicketPdf();

        _btnModify.Size = new Size(146, 36);
        _btnModify.Margin = new Padding(0, 0, 8, 0);
        AppTheme.StyleSecondaryButton(_btnModify, AppIcon.EditTicket, "Modificar Ticket", "Editar datos de este ticket", AppTheme.NavyPrimary, _tt);
        _btnModify.Enabled = PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ModificarRegistro);
        _btnModify.Click += async (_, _) => await OpenEditorAsync();

        var btnCopyRaw = new Button { Size = new Size(130, 36), Margin = new Padding(0, 0, 8, 0) };
        AppTheme.StyleSecondaryButton(btnCopyRaw, AppIcon.Copy, "Copiar Texto", "Copiar detalle completo del ticket al portapapeles", null, _tt);
        btnCopyRaw.Click += (_, _) =>
        {
            string raw = TicketPrinterService.BuildRawTicketText(_pesaje, _services.BasculaActual, _services.ConfigActual);
            Clipboard.SetText(raw);
            MessageBox.Show(this, "Detalle del ticket copiado al portapapeles.", "Copiar", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        var btnClose = new Button { Size = new Size(106, 36), Margin = Padding.Empty };
        AppTheme.StyleSecondaryButton(btnClose, AppIcon.Cancel, "Cerrar", "Cerrar visualizador", null, _tt);
        btnClose.Click += (_, _) =>
        {
            DialogResult = Modified ? DialogResult.OK : DialogResult.Cancel;
            Close();
        };

        actionsRow.Controls.AddRange([lblCopies, _numCopias, btnPrint, btnPdf, _btnModify, btnCopyRaw, btnClose]);
        actionsBar.Controls.Add(actionsRow);

        var scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20, 14, 20, 14), BackColor = AppTheme.BackgroundLight };
        _ticketCanvas.Size = new Size(760, 582);
        _ticketCanvas.Dock = DockStyle.Top;
        _ticketCanvas.BackColor = Color.White;
        _ticketCanvas.BorderStyle = BorderStyle.FixedSingle;
        _ticketCanvas.Paint += PaintVisualTicket;
        _ticketCanvas.Resize += (_, _) => _ticketCanvas.Invalidate();

        scrollHost.Controls.Add(_ticketCanvas);
        Controls.AddRange([scrollHost, actionsBar, header]);
    }

    private void PaintVisualTicket(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        Rectangle rect = _ticketCanvas.ClientRectangle;
        int w = rect.Width;
        int pad = 24;

        // Top corporate stripe
        AppTheme.PaintTealGradient(g, new Rectangle(0, 0, w, 78), includeGoldBottomAccent: true);
        Image? logo = IconCatalog.GetCorporateLogo();
        if (logo is not null)
        {
            using var logoBg = new SolidBrush(Color.White);
            g.FillRectangle(logoBg, pad, 10, 118, 56);
            g.DrawImage(logo, new Rectangle(pad + 6, 13, 106, 50));
        }

        string company = string.IsNullOrWhiteSpace(_services.BasculaActual.NombreEmpresa) ? "PESAJE COMERCIAL" : _services.BasculaActual.NombreEmpresa;
        string dir = string.IsNullOrWhiteSpace(_services.BasculaActual.Rut)
            ? _services.BasculaActual.Direccion
            : $"{_services.BasculaActual.Direccion}  |  RUT: {_services.BasculaActual.Rut}";
        using var fTitle = new Font("Segoe UI", 13.5f, FontStyle.Bold);
        using var fSub = new Font("Segoe UI", 8.75f, FontStyle.Regular);
        using var fSec = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var fVal = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        using var fBig = new Font("Segoe UI", 15f, FontStyle.Bold);
        using var fLblW = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var goldBrush = new SolidBrush(AppTheme.GoldAccent);
        using var mutedBrush = new SolidBrush(AppTheme.TextMuted);
        using var darkBrush = new SolidBrush(AppTheme.NavyDark);
        using var textBrush = new SolidBrush(AppTheme.TextPrimary);
        using var linePen = new Pen(AppTheme.BorderSubtle, 1f);

        int textLeft = logo is not null ? pad + 132 : pad;
        g.DrawString("TICKET DE PESAJE COMERCIAL", fTitle, Brushes.White, textLeft, 12);
        g.DrawString(company, fSec, goldBrush, textLeft, 36);
        g.DrawString(dir, fSub, Brushes.LightSteelBlue, textLeft, 54);

        // Status pill
        string badgeText = _pesaje.Anulado ? "ANULADO" : (_isEnProceso ? "EN PROCESO" : "COMPLETADO");
        Color badgeColor = _pesaje.Anulado ? AppTheme.AccentDanger : (_isEnProceso ? AppTheme.AccentAmber : AppTheme.AccentEmerald);
        using var badgeBrush = new SolidBrush(badgeColor);
        var badgeRect = new Rectangle(w - pad - 118, 20, 118, 28);
        g.FillRectangle(badgeBrush, badgeRect);
        using var centerFmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(badgeText, fSec, Brushes.White, badgeRect, centerFmt);

        int y = 96;
        g.DrawString($"FOLIO N° {_pesaje.FolioPesaje:D6}", new Font("Segoe UI", 14f, FontStyle.Bold), darkBrush, pad, y);
        g.DrawString($"TIPO: {_pesaje.TipoPesaje}", fSec, textBrush, w - pad - 240, y + 4);
        y += 32;
        g.DrawLine(linePen, pad, y, w - pad, y);
        y += 12;

        (string Lbl, string Val)[] fields =
        [
            ("PATENTE / ACOPLADO", string.IsNullOrWhiteSpace(_pesaje.Acoplado) ? _pesaje.Patente : $"{_pesaje.Patente}   (Acoplado: {_pesaje.Acoplado})"),
            ("CLIENTE", $"{_pesaje.Cliente} - {_pesaje.NombreCliente}".Trim(' ', '-')),
            ("PRODUCTO", $"{_pesaje.Producto} - {_pesaje.NombreProducto}".Trim(' ', '-')),
            ("ORIGEN / PROVEEDOR", $"{_pesaje.Origen} - {_pesaje.NombreOrigen}".Trim(' ', '-')),
            ("DESTINO", $"{_pesaje.Destino} - {_pesaje.NombreDestino}".Trim(' ', '-')),
            ("TRANSPORTISTA", $"{_pesaje.Transportista} - {_pesaje.NombreTransportista}".Trim(' ', '-')),
            ("CHOFER", $"{_pesaje.Chofer} - {_pesaje.NombreChofer}".Trim(' ', '-')),
            ("GUÍA DE DESPACHO", string.IsNullOrWhiteSpace(_pesaje.GuiaDespacho) ? "-" : _pesaje.GuiaDespacho)
        ];

        foreach ((string lbl, string val) in fields)
        {
            g.DrawString(lbl + ":", fSec, mutedBrush, pad, y);
            g.DrawString(string.IsNullOrWhiteSpace(val) ? "-" : val, fVal, textBrush, pad + 172, y);
            y += 22;
            g.DrawLine(linePen, pad, y - 3, w - pad, y - 3);
        }

        y += 10;
        // Weight Box Card
        var wBox = new Rectangle(pad, y, w - 2 * pad, 86);
        using var navyBrush = new SolidBrush(AppTheme.NavyDark);
        using var goldPen = new Pen(AppTheme.GoldAccent, 1.8f);
        g.FillRectangle(navyBrush, wBox);
        g.DrawRectangle(goldPen, wBox);

        int colW = wBox.Width / 3;
        DrawWeightCell(g, new Rectangle(wBox.X, wBox.Y, colW, wBox.Height), "PESO BRUTO", $"{_pesaje.Bruto:N0} kg", Brushes.White, fLblW, fBig, goldBrush);
        DrawWeightCell(g, new Rectangle(wBox.X + colW, wBox.Y, colW, wBox.Height), _pesaje.TaraRegistrada ? "PESO TARA (REG.)" : "PESO TARA", $"{_pesaje.Tara:N0} kg", Brushes.White, fLblW, fBig, goldBrush);
        using var neonBrush = new SolidBrush(AppTheme.LcdDigitOn);
        DrawWeightCell(g, new Rectangle(wBox.X + colW * 2, wBox.Y, colW, wBox.Height), "PESO NETO", _isEnProceso ? "PENDIENTE" : $"{_pesaje.Neto:N0} kg", neonBrush, fLblW, fBig, goldBrush);
        y += 102;

        // Entrada / Salida Footer Card
        using var softBrush = new SolidBrush(AppTheme.RowAlternate);
        var timeBox = new Rectangle(pad, y, w - 2 * pad, 108);
        g.FillRectangle(softBrush, timeBox);
        g.DrawRectangle(linePen, timeBox);

        int mid = w / 2;
        g.DrawString("ENTRADA A PLANTA", fSec, darkBrush, pad + 12, y + 8);
        g.DrawString($"Fecha/Hora: {_pesaje.FechaIngreso:dd/MM/yyyy} {_pesaje.HoraIngreso}", fVal, textBrush, pad + 12, y + 28);
        g.DrawString($"Operador: {_pesaje.NombreUsuarioIngreso}", fVal, mutedBrush, pad + 12, y + 48);

        g.DrawString("SALIDA DE PLANTA", fSec, darkBrush, mid + 8, y + 8);
        string salText = _isEnProceso ? "En tránsito (sin salida)" : $"{_pesaje.FechaSalida:dd/MM/yyyy} {_pesaje.HoraSalida}";
        g.DrawString($"Fecha/Hora: {salText}", fVal, textBrush, mid + 8, y + 28);
        g.DrawString($"Operador: {(_isEnProceso ? "-" : _pesaje.NombreUsuarioSalida)}", fVal, mutedBrush, mid + 8, y + 48);

        string obs = $"{_pesaje.Observacion1} {_pesaje.Observacion2}".Trim();
        g.DrawString($"Observaciones: {(string.IsNullOrWhiteSpace(obs) ? "-" : obs)}", fSub, textBrush, pad + 12, y + 72);
    }

    private static void DrawWeightCell(Graphics g, Rectangle cell, string title, string val, Brush valBrush, Font fLbl, Font fVal, Brush lblBrush)
    {
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(title, fLbl, lblBrush, new Rectangle(cell.X, cell.Y + 10, cell.Width, 20), fmt);
        g.DrawString(val, fVal, valBrush, new Rectangle(cell.X, cell.Y + 32, cell.Width, 42), fmt);
    }

    private void PrintPreviewTicket()
    {
        short copies = (short)_numCopias.Value;
        using PrintDocument doc = TicketPrinterService.CreateCommercialTicketDocument(
            _pesaje, _services.BasculaActual, _services.ConfigActual, copies, IconCatalog.GetCorporateLogo());
        using var prev = new PrintPreviewDialog { Document = doc, Width = 860, Height = 660 };
        prev.ShowDialog(this);
    }

    private void ExportTicketPdf()
    {
        using var sfd = new SaveFileDialog
        {
            Filter = "Documento PDF|*.pdf",
            FileName = $"Ticket_Folio_{_pesaje.FolioPesaje}_{_pesaje.Patente}.pdf"
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        TicketPrinterService.ExportCommercialTicketToPdf(sfd.FileName, _pesaje, _services.BasculaActual, _services.ConfigActual, _isEnProceso);
        MessageBox.Show(this, $"Ticket exportado a PDF:\r\n{sfd.FileName}", "Exportar PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task OpenEditorAsync()
    {
        TicketEditorMode mode = _isEnProceso ? TicketEditorMode.EditInProcess : TicketEditorMode.EditHistorical;
        using var dlg = new TicketEditorDialog(_services, mode, _pesaje);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            Modified = true;
            if (!_isEnProceso)
            {
                Pesaje? updated = await _services.Weighing.GetPesajePorFolioAsync(_pesaje.FolioPesaje);
                if (updated is not null) _pesaje = updated;
            }
            else
            {
                List<RegistroTransito> trs = await _services.Weighing.GetTransitoAsync();
                RegistroTransito? match = trs.FirstOrDefault(x => x.FolioPesaje == _pesaje.FolioPesaje);
                if (match is not null) _pesaje = TicketEditorDialog.ToPesajeView(match);
            }
            _ticketCanvas.Invalidate();
        }
    }
}
