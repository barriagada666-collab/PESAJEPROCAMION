using System.Drawing;
using System.Windows.Forms;
using PesajeProCamion.App.UI.Theme;

namespace PesajeProCamion.App.UI.Dialogs;

/// <summary>Diálogo limpio para ingreso de peso manual cuando está autorizado por modo prueba o contingencia.</summary>
public sealed class ManualWeightDialog : Form
{
    private readonly NumericUpDown _numPeso = new();

    public ManualWeightDialog(int initialWeight = 0)
    {
        Text = "Ingresar Peso Manual";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(340, 145);
        BackColor = AppTheme.BackgroundLight;

        var lbl = new Label { Text = "PESO (kg)", Location = new Point(22, 16), AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = AppTheme.NavyDark };
        _numPeso.Location = new Point(22, 42);
        _numPeso.Size = new Size(294, 42);
        _numPeso.BackColor = AppTheme.LcdBackground;
        _numPeso.ForeColor = AppTheme.LcdDigitOn;
        _numPeso.Font = new Font("Consolas", 22f, FontStyle.Bold);
        _numPeso.TextAlign = HorizontalAlignment.Right;
        _numPeso.Maximum = 200_000;
        _numPeso.Minimum = 0;
        _numPeso.ThousandsSeparator = true;
        _numPeso.Value = Math.Clamp(initialWeight, 0, 200_000);

        var btnOk = new Button { Location = new Point(122, 96), Size = new Size(94, 34) };
        AppTheme.StylePrimaryButton(btnOk, AppIcon.Check, "Aceptar", "Confirmar peso ingresado");
        btnOk.Click += (_, _) => { WeightKg = (int)_numPeso.Value; DialogResult = DialogResult.OK; Close(); };

        var btnCancel = new Button { Location = new Point(222, 96), Size = new Size(94, 34) };
        AppTheme.StyleSecondaryButton(btnCancel, AppIcon.Cancel, "Cancelar", "Cancelar");
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        AcceptButton = btnOk;
        CancelButton = btnCancel;
        Controls.AddRange([lbl, _numPeso, btnOk, btnCancel]);
    }

    public int WeightKg { get; private set; }
}

/// <summary>Diálogo limpio para reimpresión de ticket por folio.</summary>
public sealed class FolioTicketDialog : Form
{
    private readonly NumericUpDown _numFolio = new();

    public FolioTicketDialog()
    {
        Text = "Buscar Folio de Pesaje";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(330, 140);
        BackColor = AppTheme.BackgroundLight;

        var lbl = new Label { Text = "FOLIO DE PESAJE", Location = new Point(22, 16), AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = AppTheme.NavyDark };
        _numFolio.Location = new Point(22, 42);
        _numFolio.Size = new Size(284, 36);
        _numFolio.BackColor = AppTheme.LcdBackground;
        _numFolio.ForeColor = AppTheme.LcdDigitOn;
        _numFolio.Font = new Font("Consolas", 18f, FontStyle.Bold);
        _numFolio.TextAlign = HorizontalAlignment.Right;
        _numFolio.Minimum = 1;
        _numFolio.Maximum = 99_999_999;

        var btnOk = new Button { Location = new Point(112, 92), Size = new Size(96, 34) };
        AppTheme.StylePrimaryButton(btnOk, AppIcon.Search, "Buscar", "Buscar e imprimir folio");
        btnOk.Click += (_, _) => { Folio = (int)_numFolio.Value; DialogResult = DialogResult.OK; Close(); };

        var btnCancel = new Button { Location = new Point(214, 92), Size = new Size(92, 34) };
        AppTheme.StyleSecondaryButton(btnCancel, AppIcon.Cancel, "Cancelar", "Cancelar");
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        AcceptButton = btnOk;
        CancelButton = btnCancel;
        Controls.AddRange([lbl, _numFolio, btnOk, btnCancel]);
    }

    public int Folio { get; private set; }
}

/// <summary>Diálogo limpio para elegir cantidad de copias del Ticket de Pesaje (igual que mockup Ticket de Pesaje).</summary>
public sealed class TicketCopiesDialog : Form
{
    private readonly NumericUpDown _numCopias = new();

    public TicketCopiesDialog(int initialCopies = 1)
    {
        Text = "Ticket de Pesaje";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(340, 148);
        BackColor = AppTheme.BackgroundLight;
        AppTheme.ApplyFormIcon(this);

        Controls.Add(new Label
        {
            Text = "Nro de Copias:",
            Location = new Point(44, 30),
            AutoSize = true,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.TextPrimary
        });

        _numCopias.SetBounds(178, 25, 92, 30);
        _numCopias.Minimum = 1;
        _numCopias.Maximum = 20;
        _numCopias.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _numCopias.Value = Math.Clamp(initialCopies, 1, 20);

        var btnOk = new Button { Location = new Point(44, 86), Size = new Size(120, 36) };
        AppTheme.StylePrimaryButton(btnOk, AppIcon.Print, "Imprimir", "Previsualizar e imprimir copias seleccionadas");
        btnOk.Click += (_, _) => { Copies = (short)_numCopias.Value; DialogResult = DialogResult.OK; Close(); };

        var btnCancel = new Button { Location = new Point(176, 86), Size = new Size(118, 36) };
        AppTheme.StyleSecondaryButton(btnCancel, AppIcon.Cancel, "Cancelar", "Cancelar impresión");
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        AcceptButton = btnOk;
        CancelButton = btnCancel;
        Controls.AddRange([_numCopias, btnOk, btnCancel]);
    }

    public short Copies { get; private set; } = 1;
}
