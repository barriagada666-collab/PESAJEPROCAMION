using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Domain.Diagnostics;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;
using PesajeProCamion.Domain.Services;
using PesajeProCamion.Domain.Validation;
using PesajeProCamion.Hardware.Printing;

namespace PesajeProCamion.App.UI.Management;

/// <summary>Edición de pesaje histórico con recálculo automático de neto, impresión y auditoría.</summary>
public sealed class ModPesajeDialog : Form
{
    private readonly AppServices _services;
    private readonly Pesaje _pesaje;
    private readonly TextBox _txtPatente = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 8 };
    private readonly TextBox _txtAcoplado = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 8 };
    private readonly ComboBox _cmbTipo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtCliente = new() { MaxLength = 10 };
    private readonly TextBox _txtProducto = new() { MaxLength = 12, CharacterCasing = CharacterCasing.Upper };
    private readonly TextBox _txtOrigen = new() { MaxLength = 10, CharacterCasing = CharacterCasing.Upper };
    private readonly TextBox _txtDestino = new() { MaxLength = 10, CharacterCasing = CharacterCasing.Upper };
    private readonly TextBox _txtTransp = new() { MaxLength = 10 };
    private readonly TextBox _txtChofer = new() { MaxLength = 35 };
    private readonly TextBox _txtGuia = new() { MaxLength = 12, CharacterCasing = CharacterCasing.Upper };
    private readonly NumericUpDown _numNetoInf = new() { Maximum = 200_000, ThousandsSeparator = true };
    private readonly NumericUpDown _numFardos = new() { Minimum = 0, Maximum = 999_999, ThousandsSeparator = true };
    private readonly CheckBox _chkMostrarFardos = new();
    private readonly Label _lblFardos = new() { Text = "Cant. Fardos", AutoSize = true, Font = new Font("Segoe UI", 8.75f, FontStyle.Bold) };
    private readonly NumericUpDown _numBruto = new() { Maximum = 200_000, ThousandsSeparator = true };
    private readonly NumericUpDown _numTara = new() { Maximum = 200_000, ThousandsSeparator = true };
    private readonly NumericUpDown _numNeto = new() { Maximum = 200_000, ThousandsSeparator = true, ReadOnly = true };
    private readonly TextBox _txtObs1 = new() { MaxLength = 120 };
    private readonly TextBox _txtObs2 = new() { MaxLength = 120 };

    public ModPesajeDialog(AppServices services, Pesaje pesaje)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(pesaje);
        _services = services;
        _pesaje = pesaje;

        Text = $"Modificar Pesaje — {TicketCustomizationHelper.FormatFolioHeader(pesaje.FolioPesaje)}{(pesaje.Anulado ? " [ANULADO]" : string.Empty)}";
        ClientSize = new Size(660, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AppTheme.BackgroundLight;
        BuildUi();
        Bind();
    }

    private void BuildUi()
    {
        _cmbTipo.Items.AddRange(["1 - Recepción", "2 - Despacho", "3 - Mov. Interno"]);
        int y = 18;
        AddField("Patente", _txtPatente, 20, y, 120);
        AddField("Acoplado", _txtAcoplado, 240, y, 110);
        AddField("Tipo", _cmbTipo, 420, y, 190);

        y += 36;
        AddField("Cliente (RUT)", _txtCliente, 20, y, 140);
        AddField("Producto", _txtProducto, 330, y, 160);

        y += 36;
        AddField("Origen", _txtOrigen, 20, y, 140);
        AddField("Destino", _txtDestino, 330, y, 160);

        y += 36;
        AddField("Transportista", _txtTransp, 20, y, 140);
        AddField("Chofer", _txtChofer, 330, y, 240);

        y += 36;
        AddField("Guía Despacho", _txtGuia, 20, y, 140);
        AddField("Neto Inf. (kg)", _numNetoInf, 330, y, 130);

        y += 36;
        _lblFardos.Location = new Point(20, y + 3);
        _numFardos.Location = new Point(118, y);
        _numFardos.Width = 115;
        _chkMostrarFardos.Text = "Mostrar Fardos (sin borrar dato en BD)";
        _chkMostrarFardos.Location = new Point(250, y + 2);
        _chkMostrarFardos.AutoSize = true;
        _chkMostrarFardos.Font = new Font("Segoe UI", 8.75f, FontStyle.Bold);
        _chkMostrarFardos.CheckedChanged += (_, _) =>
        {
            _services.TicketFields.MostrarFardos = _chkMostrarFardos.Checked;
            AppConfigStore.SaveTicketFields(_services.TicketFields);
            _lblFardos.Visible = _chkMostrarFardos.Checked;
            _numFardos.Visible = _chkMostrarFardos.Checked;
        };
        Controls.AddRange([_lblFardos, _numFardos, _chkMostrarFardos]);

        y += 40;
        _numBruto.Font = new Font("Segoe UI", 9.75f, FontStyle.Bold);
        _numTara.Font = new Font("Segoe UI", 9.75f, FontStyle.Bold);
        _numNeto.Font = new Font("Segoe UI", 11.25f, FontStyle.Bold);
        _numNeto.ForeColor = AppTheme.NavyDark;
        _numNeto.BackColor = Color.FromArgb(236, 246, 255);
        AddField("Peso Bruto (kg)", _numBruto, 20, y, 125);
        AddField("Peso Tara (kg)", _numTara, 230, y, 125);
        AddField("Peso Neto (kg)", _numNeto, 435, y, 135);
        _numBruto.ValueChanged += (_, _) => RecalcNeto();
        _numTara.ValueChanged += (_, _) => RecalcNeto();

        y += 40;
        AddField("Observación 1", _txtObs1, 20, y, 470);
        y += 36;
        AddField("Observación 2", _txtObs2, 20, y, 470);

        y += 40;
        var lblAudit = new Label
        {
            Location = new Point(20, y),
            Size = new Size(595, 42),
            ForeColor = AppTheme.TextMuted,
            Font = new Font("Segoe UI", 8.5f),
            Text = $"Ingreso: {_pesaje.FechaIngreso:dd/MM/yyyy} {_pesaje.HoraIngreso} ({_pesaje.NombreUsuarioIngreso})  |  Salida: {_pesaje.FechaSalida:dd/MM/yyyy} {_pesaje.HoraSalida} ({_pesaje.NombreUsuarioSalida})"
        };
        Controls.Add(lblAudit);

        y += 50;
        var btnPrint = new Button { Location = new Point(20, y), Size = new Size(120, 34) };
        AppTheme.StyleSecondaryButton(btnPrint, AppIcon.Print, "Imprimir", "Imprimir ticket");
        btnPrint.Click += (_, _) =>
        {
            using PrintDocument doc = TicketPrinterService.CreateCommercialTicketDocument(_pesaje, _services.BasculaActual, _services.ConfigActual, 1, IconCatalog.GetCorporateLogo(), _services.TicketFields);
            using var prev = new PrintPreviewDialog { Document = doc, Width = 780, Height = 580 };
            prev.ShowDialog(this);
        };

        var btnAnular = new Button { Location = new Point(148, y), Size = new Size(145, 34) };
        AppTheme.StyleDangerButton(btnAnular, AppIcon.Delete, _pesaje.Anulado ? "Reactivar" : "Anular Pesaje", "Anular o reactivar este folio");
        btnAnular.Click += async (_, _) => await ToggleAnularAsync();

        var btnSave = new Button { Location = new Point(366, y), Size = new Size(128, 36) };
        AppTheme.StylePrimaryButton(btnSave, AppIcon.Save, "Guardar", "Guardar modificaciones");
        btnSave.Click += async (_, _) => await SaveAsync();

        var btnClose = new Button { Location = new Point(502, y), Size = new Size(116, 36) };
        AppTheme.StyleSecondaryButton(btnClose, AppIcon.Cancel, "Cerrar", "Cerrar");
        btnClose.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange([btnPrint, btnAnular, btnSave, btnClose]);
    }

    private void AddField(string label, Control ctl, int x, int y, int width)
    {
        Controls.Add(new Label { Text = label, Location = new Point(x, y + 3), AutoSize = true, Font = new Font("Segoe UI", 8.75f, FontStyle.Bold) });
        ctl.Location = new Point(x + 98, y);
        ctl.Width = width;
        Controls.Add(ctl);
    }

    private void Bind()
    {
        _txtPatente.Text = _pesaje.Patente;
        _txtAcoplado.Text = _pesaje.Acoplado;
        _cmbTipo.SelectedIndex = Math.Clamp((int)_pesaje.TipoPesaje - 1, 0, 2);
        _txtCliente.Text = _pesaje.Cliente;
        _txtProducto.Text = _pesaje.Producto;
        _txtOrigen.Text = _pesaje.Origen;
        _txtDestino.Text = _pesaje.Destino;
        _txtTransp.Text = _pesaje.Transportista;
        _txtChofer.Text = _pesaje.Chofer;
        _txtGuia.Text = _pesaje.GuiaDespacho;
        _numNetoInf.Value = Math.Clamp(_pesaje.NetoInformado, 0, 200_000);
        _numFardos.Value = Math.Clamp(_pesaje.Fardos, 0, 999_999);
        _chkMostrarFardos.Checked = _services.TicketFields.MostrarFardos;
        _lblFardos.Visible = _chkMostrarFardos.Checked;
        _numFardos.Visible = _chkMostrarFardos.Checked;
        _numBruto.Value = Math.Clamp(_pesaje.Bruto, 0, 200_000);
        _numTara.Value = Math.Clamp(_pesaje.Tara, 0, 200_000);
        _numNeto.Value = Math.Clamp(_pesaje.Neto, 0, 200_000);
        _txtObs1.Text = _pesaje.Observacion1;
        _txtObs2.Text = _pesaje.Observacion2;
    }

    private void RecalcNeto()
    {
        _numNeto.Value = _pesaje.Anulado ? 0 : WeighingCalculator.CalcularNeto((int)_numBruto.Value, (int)_numTara.Value);
    }

    private async Task SaveAsync()
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.ModificarRegistro))
        {
            MessageBox.Show(this, "No está autorizado para modificar pesajes.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _pesaje.Patente = ChileanValidators.NormalizarPatente(_txtPatente.Text);
        _pesaje.Acoplado = ChileanValidators.NormalizarPatente(_txtAcoplado.Text);
        _pesaje.TipoPesaje = (TipoPesaje)(_cmbTipo.SelectedIndex + 1);
        _pesaje.Cliente = _txtCliente.Text.Trim();
        _pesaje.Producto = _txtProducto.Text.Trim();
        _pesaje.Origen = _txtOrigen.Text.Trim();
        _pesaje.Destino = _txtDestino.Text.Trim();
        _pesaje.Transportista = _txtTransp.Text.Trim();
        _pesaje.Chofer = _txtChofer.Text.Trim();
        _pesaje.GuiaDespacho = _txtGuia.Text.Trim();
        _pesaje.NetoInformado = (int)_numNetoInf.Value;
        _pesaje.Fardos = (int)_numFardos.Value;
        _pesaje.Bruto = (int)_numBruto.Value;
        _pesaje.Tara = (int)_numTara.Value;
        _pesaje.Observacion1 = _txtObs1.Text.Trim();
        _pesaje.Observacion2 = _txtObs2.Text.Trim();

        try
        {
            await _services.Weighing.ModificarPesajeAsync(_pesaje, _services.UsuarioActual?.Id ?? 0);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            AppLogger.Error("ModPesajeDialog.SaveAsync", $"Error guardando folio {_pesaje.FolioPesaje:D6}", ex);
            MessageBox.Show(this, $"No se pudo guardar el pesaje:\r\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ToggleAnularAsync()
    {
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, AccionAutorizacion.AnularPesaje))
        {
            MessageBox.Show(this, "No está autorizado para anular/reactivar pesajes.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        await _services.Weighing.ToggleAnularPesajeAsync(_pesaje.FolioPesaje, _services.UsuarioActual?.Id ?? 0);
        DialogResult = DialogResult.OK;
        Close();
    }
}
