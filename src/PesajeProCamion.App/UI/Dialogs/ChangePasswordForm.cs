using System.Drawing;
using System.Windows.Forms;
using PesajeProCamion.App.UI.Theme;

namespace PesajeProCamion.App.UI.Dialogs;

/// <summary>Diálogo de actualización segura de contraseña.</summary>
public sealed class ChangePasswordForm : Form
{
    private readonly TextBox _txtPw1 = new();
    private readonly TextBox _txtPw2 = new();
    private readonly Button _btnAceptar = new();
    private readonly Button _btnCancelar = new();

    public ChangePasswordForm()
    {
        Text = "Cambio de Contraseña";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 198);
        MinimumSize = new Size(420, 190);
        BackColor = AppTheme.BackgroundLight;
        AppTheme.ApplyFormIcon(this);

        var lbl1 = new Label { Text = "Nueva Contraseña", Location = new Point(22, 24), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        _txtPw1.Location = new Point(194, 21);
        _txtPw1.Width = 220;
        _txtPw1.UseSystemPasswordChar = true;
        _txtPw1.MaxLength = 32;

        var lbl2 = new Label { Text = "Confirmar Contraseña", Location = new Point(22, 62), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        _txtPw2.Location = new Point(194, 59);
        _txtPw2.Width = 220;
        _txtPw2.UseSystemPasswordChar = true;
        _txtPw2.MaxLength = 32;

        var chkShow = new CheckBox { Text = "Mostrar contraseñas", Location = new Point(194, 94), AutoSize = true, Font = AppTheme.FontBody };
        chkShow.CheckedChanged += (_, _) =>
        {
            _txtPw1.UseSystemPasswordChar = !chkShow.Checked;
            _txtPw2.UseSystemPasswordChar = !chkShow.Checked;
        };

        _btnAceptar.Location = new Point(182, 138);
        _btnAceptar.Size = new Size(112, 36);
        AppTheme.StylePrimaryButton(_btnAceptar, AppIcon.Check, "Aceptar", "Guardar nueva contraseña");
        _btnAceptar.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_txtPw1.Text) || _txtPw1.Text.Length < 4)
            {
                MessageBox.Show(this, "La contraseña debe tener al menos 4 caracteres.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!string.Equals(_txtPw1.Text, _txtPw2.Text, StringComparison.Ordinal))
            {
                MessageBox.Show(this, "Las contraseñas ingresadas no coinciden.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            NuevaPassword = _txtPw1.Text;
            DialogResult = DialogResult.OK;
            Close();
        };

        _btnCancelar.Location = new Point(302, 138);
        _btnCancelar.Size = new Size(112, 36);
        AppTheme.StyleSecondaryButton(_btnCancelar, AppIcon.Cancel, "Cancelar", "Cancelar cambio");
        _btnCancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        AcceptButton = _btnAceptar;
        CancelButton = _btnCancelar;
        Controls.AddRange([lbl1, _txtPw1, lbl2, _txtPw2, chkShow, _btnAceptar, _btnCancelar]);
    }

    public string NuevaPassword { get; private set; } = string.Empty;
}
