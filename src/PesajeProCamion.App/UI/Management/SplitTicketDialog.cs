using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Data.Repositories;
using PesajeProCamion.Domain.Diagnostics;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;

namespace PesajeProCamion.App.UI.Management;

/// <summary>
/// Asistente visual para dividir un ticket histórico en 2 tickets con reparto coherente de pesos
/// (ej. Neto 10 -> Folio origen 6 + nuevo folio libre 4) y asignación de folio sin colisión.
/// </summary>
public sealed class SplitTicketDialog : Form
{
    private readonly AppServices _services;
    private readonly Pesaje _source;
    private readonly int _origNeto;
    private readonly ToolTip _tt = new();

    private readonly NumericUpDown _numNeto1 = new() { Minimum = 1, ThousandsSeparator = true };
    private readonly NumericUpDown _numNeto2 = new() { Minimum = 1, ReadOnly = true, ThousandsSeparator = true };
    private readonly CheckBox _chkManualWeights = new();

    private readonly NumericUpDown _numBruto1 = new() { Minimum = 0, Maximum = 400_000, ThousandsSeparator = true };
    private readonly NumericUpDown _numTara1 = new() { Minimum = 0, Maximum = 400_000, ThousandsSeparator = true };
    private readonly NumericUpDown _numBruto2 = new() { Minimum = 0, Maximum = 400_000, ThousandsSeparator = true };
    private readonly NumericUpDown _numTara2 = new() { Minimum = 0, Maximum = 400_000, ThousandsSeparator = true };

    private readonly CheckBox _chkAutoFolio = new() { Checked = true };
    private readonly NumericUpDown _numNewFolio = new() { Minimum = 1, Maximum = 99_999_999, Enabled = false };
    private readonly Label _lblFolioStatus = new();
    private readonly CheckBox _chkAppendNote = new() { Checked = true };
    private readonly NumericUpDown _numFardos1 = new() { Minimum = 0, Maximum = 999_999, ThousandsSeparator = true };
    private readonly NumericUpDown _numFardos2 = new() { Minimum = 0, Maximum = 999_999, ThousandsSeparator = true };
    private readonly CheckBox _chkProportionalFardos = new() { Checked = true };
    private readonly CheckBox _chkConfirmCustomFardos = new();
    private bool _suppressSync;

    public SplitTicketDialog(AppServices services, Pesaje source)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(source);
        _services = services;
        _source = source;
        _origNeto = source.Neto > 0 ? source.Neto : Math.Abs(source.Bruto - source.Tara);

        Text = $"Dividir Ticket — {TicketCustomizationHelper.FormatFolioHeader(source.FolioPesaje)} en 2 Tickets";
        AppTheme.ConfigureResponsiveDialog(this, 920, 690, 780, 635, resizable: true);
        BuildUi();
        InitializeValues();
        Load += async (_, _) => await AppTheme.SafeLoadAsync(this, nameof(SplitTicketDialog), RefreshFolioSuggestionAsync);
    }

    public TicketSplitResult? Result { get; private set; }

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
        var header = new Panel { Dock = DockStyle.Top, Height = 52 };
        header.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, header.ClientRectangle);
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 16, 0),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = AppTheme.FontTitle,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = $"Dividir Ticket — {TicketCustomizationHelper.FormatFolioHeader(_source.FolioPesaje)} en 2 Folios Independientes"
        });

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = AppTheme.SurfaceCard,
            Padding = new Padding(12, 8, 14, 8)
        };
        var btnCancel = new Button { Size = new Size(124, 36) };
        AppTheme.StyleSecondaryButton(btnCancel, AppIcon.Cancel, "Cancelar", "Cancelar división", null, _tt);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        var btnConfirm = new Button { Size = new Size(196, 36) };
        AppTheme.StylePrimaryButton(btnConfirm, AppIcon.Split, "Dividir y Confirmar", "Actualizar folio origen y generar nuevo ticket", _tt);
        btnConfirm.BackColor = AppTheme.AccentEmerald;
        btnConfirm.Click += async (_, _) => await ConfirmSplitAsync();
        footer.Controls.AddRange([btnCancel, btnConfirm]);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14, 10, 14, 8)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 84f));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 168f));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // 1. Resumen ticket origen
        var grpOrig = new GroupBox
        {
            Text = "Ticket de Origen a Dividir",
            Dock = DockStyle.Fill,
            BackColor = AppTheme.SurfaceCard,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.NavyDark,
            Padding = new Padding(10, 4, 10, 4)
        };
        var origRows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        origRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        origRows.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        origRows.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        origRows.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Font = AppTheme.FontBody,
            ForeColor = AppTheme.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Text = $"{TicketCustomizationHelper.FormatFolioHeader(_source.FolioPesaje)}  |  Patente: {_source.Patente}  |  {TicketCustomizationHelper.FormatTipoPesajeBadge(_source.TipoPesaje)}  |  Cliente: {_source.NombreCliente}  |  Producto: {_source.NombreProducto}"
        }, 0, 0);
        origRows.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.NavyDark,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Text = $"Pesos actuales — Bruto: {_source.Bruto:N0} kg   |   Tara: {_source.Tara:N0} kg   |   Neto Total: {_origNeto:N0} kg   |   Fardos: {_source.Fardos:N0}"
        }, 0, 1);
        grpOrig.Controls.Add(origRows);

        // 2. Controles de reparto neto, fardos y presets
        var grpSplit = new GroupBox
        {
            Text = "Distribución de Peso Neto y Cantidad de Fardos entre los 2 Tickets",
            Dock = DockStyle.Fill,
            BackColor = AppTheme.SurfaceCard,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.NavyDark,
            Padding = new Padding(10, 6, 10, 4)
        };
        var splitTbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
        splitTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 195f));
        splitTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145f));
        splitTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 195f));
        splitTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        splitTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        splitTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        splitTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _numNeto1.Dock = DockStyle.Fill;
        _numNeto1.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        _numNeto1.ForeColor = AppTheme.NavyDark;
        _numNeto1.Margin = new Padding(2, 3, 12, 2);
        _numNeto1.ValueChanged += (_, _) => OnNeto1Changed();
        _numNeto2.Dock = DockStyle.Fill;
        _numNeto2.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        _numNeto2.ForeColor = AppTheme.NavyDark;
        _numNeto2.Margin = new Padding(2, 3, 12, 2);

        splitTbl.Controls.Add(MakeFieldLbl($"Neto ({TicketCustomizationHelper.FormatFolioHeader(_source.FolioPesaje)}):"), 0, 0);
        splitTbl.Controls.Add(_numNeto1, 1, 0);
        splitTbl.Controls.Add(MakeFieldLbl("Neto Ticket Nuevo:"), 2, 0);
        splitTbl.Controls.Add(_numNeto2, 3, 0);

        _numFardos1.Dock = DockStyle.Fill;
        _numFardos1.Margin = new Padding(2, 4, 12, 2);
        _numFardos1.ValueChanged += (_, _) => OnFardosEdited(fromFirst: true);
        _numFardos2.Width = 104;
        _numFardos2.Margin = new Padding(2, 4, 10, 2);
        _numFardos2.ValueChanged += (_, _) => OnFardosEdited(fromFirst: false);

        _chkProportionalFardos.Text = "Proporcional al Neto";
        _chkProportionalFardos.AutoSize = true;
        _chkProportionalFardos.Margin = new Padding(2, 7, 0, 0);
        _tt.SetToolTip(_chkProportionalFardos, "Distribuye los fardos proporcionalmente para conservar Fardos 1 + Fardos 2 = Original.");
        _chkProportionalFardos.CheckedChanged += (_, _) =>
        {
            if (_chkProportionalFardos.Checked)
            {
                ApplyProportionalFardos((int)_numNeto1.Value);
            }
            UpdateFardosValidationUi();
        };

        var fardosRight = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        fardosRight.Controls.AddRange([_numFardos2, _chkProportionalFardos]);

        splitTbl.Controls.Add(MakeFieldLbl("Fardos Folio Origen:"), 0, 1);
        splitTbl.Controls.Add(_numFardos1, 1, 1);
        splitTbl.Controls.Add(MakeFieldLbl("Fardos Ticket Nuevo:"), 2, 1);
        splitTbl.Controls.Add(fardosRight, 3, 1);

        var presetFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
        Button MakePctBtn(string text, double ratio)
        {
            var b = new Button { Size = new Size(90, 28), Margin = new Padding(0, 3, 6, 2) };
            AppTheme.StyleSecondaryButton(b, AppIcon.Split, text, $"Repartir {text}", AppTheme.NavyPrimary, _tt);
            b.Click += (_, _) => ApplyRatio(ratio);
            return b;
        }
        _chkManualWeights.Text = "Ajuste manual Bruto / Tara";
        _chkManualWeights.AutoSize = true;
        _chkManualWeights.Margin = new Padding(6, 7, 6, 0);
        _chkManualWeights.CheckedChanged += (_, _) => UpdateWeightEditability();

        _chkAppendNote.Text = "Nota en Obs. 2";
        _chkAppendNote.AutoSize = true;
        _chkAppendNote.Margin = new Padding(6, 7, 6, 0);

        _chkConfirmCustomFardos.Text = "Confirmar suma distinta fardos";
        _chkConfirmCustomFardos.AutoSize = true;
        _chkConfirmCustomFardos.Margin = new Padding(6, 7, 0, 0);
        _chkConfirmCustomFardos.Visible = false;

        presetFlow.Controls.AddRange([MakePctBtn("50% / 50%", 0.50), MakePctBtn("60% / 40%", 0.60), MakePctBtn("70% / 30%", 0.70), _chkManualWeights, _chkAppendNote, _chkConfirmCustomFardos]);
        splitTbl.Controls.Add(presetFlow, 0, 2);
        splitTbl.SetColumnSpan(presetFlow, 4);
        grpSplit.Controls.Add(splitTbl);

        // 3. Asignación del folio del 2do ticket
        var grpNewFol = new GroupBox
        {
            Text = "Folio del Nuevo Ticket Generado (Respetando En Proceso)",
            Dock = DockStyle.Fill,
            BackColor = AppTheme.SurfaceCard,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.NavyDark,
            Padding = new Padding(10, 4, 10, 4)
        };
        var folRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        folRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118f));
        folRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128f));
        folRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        folRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _chkAutoFolio.Text = "Folio automático libre";
        _chkAutoFolio.AutoSize = true;
        _chkAutoFolio.Margin = new Padding(2, 10, 10, 0);
        _chkAutoFolio.CheckedChanged += async (_, _) =>
        {
            _numNewFolio.Enabled = !_chkAutoFolio.Checked;
            await RefreshFolioSuggestionAsync();
        };

        _numNewFolio.Dock = DockStyle.Fill;
        _numNewFolio.Margin = new Padding(0, 7, 8, 0);
        _numNewFolio.ValueChanged += async (_, _) => await ValidateManualNewFolioAsync();

        var btnCheckFol = new Button { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 8, 4) };
        AppTheme.StyleSecondaryButton(btnCheckFol, AppIcon.Search, "Validar Folio", "Validar disponibilidad sin colisión", null, _tt);
        btnCheckFol.Click += async (_, _) => await RefreshFolioSuggestionAsync();

        _lblFolioStatus.Dock = DockStyle.Fill;
        _lblFolioStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblFolioStatus.Font = AppTheme.FontBodyBold;
        _lblFolioStatus.ForeColor = AppTheme.AccentEmerald;
        _lblFolioStatus.AutoEllipsis = true;
        _lblFolioStatus.Text = "Calculando próximo folio disponible...";

        folRow.Controls.Add(_chkAutoFolio, 0, 0);
        folRow.Controls.Add(_numNewFolio, 1, 0);
        folRow.Controls.Add(btnCheckFol, 2, 0);
        folRow.Controls.Add(_lblFolioStatus, 3, 0);
        grpNewFol.Controls.Add(folRow);

        // 4. Tarjetas comparativas de resultado
        var compare = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        compare.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        compare.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        compare.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        compare.Controls.Add(BuildPartCard($"Ticket 1 — Origen ({TicketCustomizationHelper.FormatFolioHeader(_source.FolioPesaje)})", AppTheme.NavyPrimary, _numBruto1, _numTara1, _numNeto1, _numFardos1, readonlyNetoCopy: true), 0, 0);
        compare.Controls.Add(BuildPartCard("Ticket 2 — Nuevo Folio Asignado", AppTheme.AccentEmerald, _numBruto2, _numTara2, _numNeto2, _numFardos2, readonlyNetoCopy: false), 1, 0);

        body.Controls.Add(grpOrig, 0, 0);
        body.Controls.Add(grpSplit, 0, 1);
        body.Controls.Add(grpNewFol, 0, 2);
        body.Controls.Add(compare, 0, 3);
        Controls.AddRange([body, footer, header]);
    }

    private static GroupBox BuildPartCard(string title, Color accent, NumericUpDown bruto, NumericUpDown tara, NumericUpDown netoRef, NumericUpDown fardosRef, bool readonlyNetoCopy)
    {
        var grp = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            BackColor = AppTheme.SurfaceCard,
            Font = AppTheme.FontBodyBold,
            ForeColor = accent,
            Padding = new Padding(12, 6, 12, 4),
            Margin = new Padding(0, 4, readonlyNetoCopy ? 6 : 0, 0)
        };
        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 24f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 24f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 30f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 22f));

        bruto.Dock = DockStyle.Fill; bruto.Margin = new Padding(0, 3, 0, 3);
        bruto.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        tara.Dock = DockStyle.Fill; tara.Margin = new Padding(0, 3, 0, 3);
        tara.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

        var lblNetoVal = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = accent,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var lblFardosVal = new Label
        {
            Dock = DockStyle.Fill,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        void SyncNetoLbl() => lblNetoVal.Text = $"{(int)netoRef.Value:N0} kg";
        void SyncFardosLbl() => lblFardosVal.Text = $"{(int)fardosRef.Value:N0} fardos";
        netoRef.ValueChanged += (_, _) => SyncNetoLbl();
        fardosRef.ValueChanged += (_, _) => SyncFardosLbl();
        SyncNetoLbl();
        SyncFardosLbl();

        tbl.Controls.Add(MakeFieldLbl("Bruto (kg):"), 0, 0); tbl.Controls.Add(bruto, 1, 0);
        tbl.Controls.Add(MakeFieldLbl("Tara (kg):"), 0, 1); tbl.Controls.Add(tara, 1, 1);
        tbl.Controls.Add(MakeFieldLbl("Neto Final:"), 0, 2); tbl.Controls.Add(lblNetoVal, 1, 2);
        tbl.Controls.Add(MakeFieldLbl("Cant. Fardos:"), 0, 3); tbl.Controls.Add(lblFardosVal, 1, 3);
        grp.Controls.Add(tbl);
        return grp;
    }

    private static Label MakeFieldLbl(string text)
        => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = AppTheme.FontBodyBold, ForeColor = AppTheme.TextPrimary };

    private void InitializeValues()
    {
        int maxPart1 = Math.Max(1, _origNeto - 1);
        _numNeto1.Maximum = maxPart1;
        _numNeto2.Maximum = maxPart1;
        int initialPart1 = Math.Clamp((int)Math.Round(_origNeto * 0.60, MidpointRounding.AwayFromZero), 1, maxPart1);
        ApplyRatioValue(initialPart1);
        UpdateWeightEditability();
    }

    private void ApplyRatio(double ratio)
    {
        if (_origNeto < 2) return;
        int p1 = Math.Clamp((int)Math.Round(_origNeto * ratio, MidpointRounding.AwayFromZero), 1, _origNeto - 1);
        ApplyRatioValue(p1);
    }

    private void OnNeto1Changed()
    {
        if (_suppressSync || _origNeto < 2) return;
        ApplyRatioValue((int)_numNeto1.Value);
    }

    private void ApplyRatioValue(int neto1)
    {
        if (_origNeto < 2) return;
        _suppressSync = true;
        try
        {
            int clamped = Math.Clamp(neto1, 1, _origNeto - 1);
            (int b1, int t1, int n1, int b2, int t2, int n2) = TicketCustomizationHelper.ComputeSplitWeights(
                _source.Bruto, _source.Tara, _origNeto, clamped);

            _numNeto1.Value = n1;
            _numNeto2.Value = n2;
            if (!_chkManualWeights.Checked)
            {
                _numBruto1.Value = Math.Clamp(b1, 0, 400_000);
                _numTara1.Value = Math.Clamp(t1, 0, 400_000);
                _numBruto2.Value = Math.Clamp(b2, 0, 400_000);
                _numTara2.Value = Math.Clamp(t2, 0, 400_000);
            }
            if (_chkProportionalFardos.Checked)
            {
                ApplyProportionalFardos(n1);
            }
            UpdateFardosValidationUi();
        }
        finally
        {
            _suppressSync = false;
        }
    }

    private void ApplyProportionalFardos(int neto1)
    {
        bool prev = _suppressSync;
        _suppressSync = true;
        try
        {
            (int f1, int f2) = TicketCustomizationHelper.ComputeSplitFardos(_source.Fardos, neto1, _origNeto);
            _numFardos1.Value = Math.Clamp(f1, 0, 999_999);
            _numFardos2.Value = Math.Clamp(f2, 0, 999_999);
        }
        finally
        {
            _suppressSync = prev;
        }
    }

    private void OnFardosEdited(bool fromFirst)
    {
        if (_suppressSync) return;
        if (_chkProportionalFardos.Checked)
        {
            _suppressSync = true;
            try
            {
                if (fromFirst && _source.Fardos > 0)
                {
                    int f1 = Math.Clamp((int)_numFardos1.Value, 0, _source.Fardos);
                    _numFardos2.Value = _source.Fardos - f1;
                }
                else if (!fromFirst && _source.Fardos > 0)
                {
                    int f2 = Math.Clamp((int)_numFardos2.Value, 0, _source.Fardos);
                    _numFardos1.Value = _source.Fardos - f2;
                }
            }
            finally
            {
                _suppressSync = false;
            }
        }
        UpdateFardosValidationUi();
    }

    private void UpdateFardosValidationUi()
    {
        int sumF = (int)_numFardos1.Value + (int)_numFardos2.Value;
        bool mismatch = _source.Fardos > 0 && sumF != _source.Fardos;
        _chkConfirmCustomFardos.Visible = mismatch;
        _chkConfirmCustomFardos.ForeColor = mismatch ? AppTheme.AccentDanger : AppTheme.TextPrimary;
        if (!mismatch)
        {
            _chkConfirmCustomFardos.Checked = false;
        }
    }

    private void UpdateWeightEditability()
    {
        bool manual = _chkManualWeights.Checked;
        _numBruto1.ReadOnly = !manual;
        _numTara1.ReadOnly = !manual;
        _numBruto2.ReadOnly = !manual;
        _numTara2.ReadOnly = !manual;
        if (!manual)
        {
            ApplyRatioValue((int)_numNeto1.Value);
        }
    }

    private async Task RefreshFolioSuggestionAsync()
    {
        if (_chkAutoFolio.Checked)
        {
            int nextFree = await _services.Weighing.GetNextSuggestedFolioAsync();
            _numNewFolio.Value = Math.Clamp(nextFree, 1, 99_999_999);
            _lblFolioStatus.Text = $"Auto-asignado {TicketCustomizationHelper.FormatFolioHeader(nextFree)} (sin conflicto con histórico ni En Proceso).";
            _lblFolioStatus.ForeColor = AppTheme.AccentEmerald;
        }
        else
        {
            await ValidateManualNewFolioAsync();
        }
    }

    private async Task ValidateManualNewFolioAsync()
    {
        if (_chkAutoFolio.Checked) return;
        try
        {
            FolioAvailabilityResult res = await _services.Weighing.CheckFolioAvailabilityAsync((int)_numNewFolio.Value);
            _lblFolioStatus.Text = res.Mensaje;
            _lblFolioStatus.ForeColor = res.Disponible ? AppTheme.AccentEmerald : AppTheme.AccentDanger;
        }
        catch (Exception ex)
        {
            _lblFolioStatus.Text = $"Error verificando folio: {ex.Message}";
            _lblFolioStatus.ForeColor = AppTheme.AccentAmber;
        }
    }

    private async Task ConfirmSplitAsync()
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AgregarRegistro) ||
            !PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ModificarRegistro))
        {
            MessageBox.Show(this, "Requiere permisos de Agregar y Modificar para dividir tickets.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_origNeto < 2)
        {
            MessageBox.Show(this, "El ticket debe tener al menos 2 kg de peso neto para poder dividirse.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int n1 = (int)_numNeto1.Value;
        int n2 = (int)_numNeto2.Value;
        if (n1 <= 0 || n2 <= 0 || n1 + n2 != _origNeto)
        {
            MessageBox.Show(this, $"La suma Neto 1 ({n1:N0}) + Neto 2 ({n2:N0}) debe ser igual a {_origNeto:N0} kg.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int f1 = (int)_numFardos1.Value;
        int f2 = (int)_numFardos2.Value;
        if (_source.Fardos > 0 && f1 + f2 != _source.Fardos && !_chkConfirmCustomFardos.Checked)
        {
            _chkConfirmCustomFardos.Visible = true;
            MessageBox.Show(
                this,
                $"Los fardos ingresados ({f1:N0} + {f2:N0} = {f1 + f2:N0}) no coinciden con el original ({_source.Fardos:N0}).\r\n" +
                "Ajuste los valores o marque 'Confirmar suma distinta fardos' para evitar pérdida o duplicación silenciosa.",
                "Validación de Fardos",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        int? customNew = _chkAutoFolio.Checked ? null : (int)_numNewFolio.Value;
        try
        {
            Result = await _services.Weighing.SplitTicketAsync(new TicketSplitRequest
            {
                OriginalFolio = _source.FolioPesaje,
                NetoParte1 = n1,
                NetoParte2 = n2,
                BrutoParte1 = (int)_numBruto1.Value,
                TaraParte1 = (int)_numTara1.Value,
                BrutoParte2 = (int)_numBruto2.Value,
                TaraParte2 = (int)_numTara2.Value,
                FardosParte1 = f1,
                FardosParte2 = f2,
                ConfirmarFardosPersonalizados = _chkConfirmCustomFardos.Checked,
                CustomNewFolio = customNew,
                AppendSplitNote = _chkAppendNote.Checked
            }, _services.UsuarioActual?.Id ?? 0);

            MessageBox.Show(
                this,
                $"Ticket dividido correctamente:\r\n- {TicketCustomizationHelper.FormatFolioHeader(Result.OriginalFolio)}: Neto {Result.NetoParte1:N0} kg  |  Fardos: {Result.FardosParte1:N0}\r\n- {TicketCustomizationHelper.FormatFolioHeader(Result.NewFolio)}: Neto {Result.NetoParte2:N0} kg  |  Fardos: {Result.FardosParte2:N0}",
                "División Exitosa",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            AppLogger.Error("SplitTicketDialog.ConfirmSplitAsync", $"Error dividiendo folio {_source.FolioPesaje:D6}", ex);
            MessageBox.Show(this, $"No se pudo dividir el ticket:\r\n{ex.Message}", "Error al Dividir", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
