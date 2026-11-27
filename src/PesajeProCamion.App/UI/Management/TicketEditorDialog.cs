using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Data.Repositories;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Security;
using PesajeProCamion.Domain.Services;
using PesajeProCamion.Domain.Validation;

namespace PesajeProCamion.App.UI.Management;

public enum TicketEditorMode
{
    CreateNew = 0,
    EditHistorical = 1,
    EditInProcess = 2,
    CompleteInProcess = 3
}

/// <summary>
/// Editor unificado para CREAR nuevos tickets (completos o en proceso) y MODIFICAR tickets existentes
/// sin conflictos de folio y respetando íntegramente los vehículos que permanecen En Proceso.
/// </summary>
public sealed class TicketEditorDialog : Form
{
    private readonly AppServices _services;
    private readonly TicketEditorMode _mode;
    private readonly Pesaje _pesaje;
    private readonly ToolTip _tt = new();

    private readonly CheckBox _chkAutoFolio = new();
    private readonly NumericUpDown _numFolio = new() { Minimum = 1, Maximum = 99_999_999 };
    private readonly Label _lblFolioStatus = new();
    private readonly CheckBox _chkCrearEnProceso = new();
    private readonly ComboBox _cmbTipo = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly TextBox _txtPatente = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 8 };
    private readonly TextBox _txtAcoplado = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 8 };
    private readonly TextBox _txtCliente = new() { MaxLength = 10 };
    private readonly TextBox _txtProducto = new() { MaxLength = 12, CharacterCasing = CharacterCasing.Upper };
    private readonly TextBox _txtOrigen = new() { MaxLength = 10, CharacterCasing = CharacterCasing.Upper };
    private readonly TextBox _txtDestino = new() { MaxLength = 10, CharacterCasing = CharacterCasing.Upper };
    private readonly TextBox _txtTransp = new() { MaxLength = 10 };
    private readonly TextBox _txtChofer = new() { MaxLength = 35 };
    private readonly TextBox _txtGuia = new() { MaxLength = 50, CharacterCasing = CharacterCasing.Upper };
    private readonly NumericUpDown _numNetoInf = new() { Maximum = 200_000, ThousandsSeparator = true };

    private readonly DateTimePicker _dtFechaIng = new() { Format = DateTimePickerFormat.Short };
    private readonly TextBox _txtHoraIng = new() { MaxLength = 5 };
    private readonly DateTimePicker _dtFechaSal = new() { Format = DateTimePickerFormat.Short };
    private readonly TextBox _txtHoraSal = new() { MaxLength = 5 };

    private readonly NumericUpDown _numBruto = new() { Maximum = 200_000, ThousandsSeparator = true };
    private readonly NumericUpDown _numTara = new() { Maximum = 200_000, ThousandsSeparator = true };
    private readonly NumericUpDown _numNeto = new() { Maximum = 200_000, ThousandsSeparator = true, ReadOnly = true };
    private readonly CheckBox _chkTaraReg = new();
    private readonly CheckBox _chkAnulado = new();
    private readonly TextBox _txtObs1 = new() { MaxLength = 120 };
    private readonly TextBox _txtObs2 = new() { MaxLength = 120 };

    public TicketEditorDialog(AppServices services, TicketEditorMode mode, Pesaje? source = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _mode = mode;
        _pesaje = source is not null ? ClonePesaje(source) : new Pesaje
        {
            FechaIngreso = DateTime.Today,
            HoraIngreso = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
            FechaSalida = DateTime.Today,
            HoraSalida = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
            TipoPesaje = TipoPesaje.Recepcion
        };

        Text = mode switch
        {
            TicketEditorMode.CreateNew => "Crear Nuevo Ticket de Pesaje",
            TicketEditorMode.EditInProcess => $"Modificar Ticket EN PROCESO #{_pesaje.FolioPesaje}",
            TicketEditorMode.CompleteInProcess => $"Completar Salida — Ticket En Proceso #{_pesaje.FolioPesaje}",
            _ => $"Modificar Ticket Folio #{_pesaje.FolioPesaje}"
        };

        AppTheme.ConfigureResponsiveDialog(this, 820, 640, 740, 580, resizable: true);
        BuildUi();
        BindFields();
        Load += async (_, _) => await InitializeFolioStateAsync();
    }

    public int SavedFolio => _pesaje.FolioPesaje;

    public static Pesaje ToPesajeView(RegistroTransito tr) => new()
    {
        FolioPesaje = tr.FolioPesaje,
        Patente = tr.Patente,
        Acoplado = tr.Acoplado,
        Producto = tr.Producto,
        NombreProducto = tr.NombreProducto,
        Cliente = tr.Cliente,
        NombreCliente = tr.NombreCliente,
        Origen = tr.Origen,
        Destino = tr.Destino,
        GuiaDespacho = tr.GuiaDespacho,
        NetoInformado = tr.NetoInformado,
        FechaIngreso = tr.FechaIngreso,
        HoraIngreso = tr.HoraIngreso,
        FechaSalida = tr.FechaIngreso,
        HoraSalida = tr.HoraIngreso,
        Bruto = tr.Bruto,
        Tara = tr.Tara,
        Neto = 0,
        TipoMop = tr.TipoMop,
        Largo = tr.Largo,
        MaxBruto = tr.MaxBruto,
        Chofer = tr.Chofer,
        Observacion1 = tr.Observacion1,
        Transportista = tr.Transportista,
        TipoPesaje = tr.TipoPesaje,
        UsuarioIngreso = tr.UsuarioIngreso
    };

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
        var header = new Panel { Dock = DockStyle.Top, Height = 48 };
        header.Paint += (_, e) => AppTheme.PaintTealGradient(e.Graphics, header.ClientRectangle);
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 16, 0),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = AppTheme.FontTitle,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = Text
        });

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = AppTheme.SurfaceCard,
            Padding = new Padding(12, 8, 14, 8)
        };
        var btnClose = new Button { Size = new Size(122, 36) };
        AppTheme.StyleSecondaryButton(btnClose, AppIcon.Cancel, "Cancelar", "Descartar cambios y cerrar", null, _tt);
        btnClose.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        var btnSave = new Button { Size = new Size(184, 36) };
        string saveText = _mode == TicketEditorMode.CreateNew
            ? "Crear Ticket"
            : (_mode == TicketEditorMode.CompleteInProcess ? "Completar y Cerrar" : "Guardar Cambios");
        AppTheme.StylePrimaryButton(btnSave, AppIcon.Save, saveText, "Validar folio sin conflicto y guardar", _tt);
        btnSave.Click += async (_, _) => await SaveAsync();

        footer.Controls.AddRange([btnClose, btnSave]);

        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(14) };

        // Sección Folio & Estado (con protección contra conflictos con folios En Proceso)
        var grpFolio = new GroupBox
        {
            Text = "Control de Folio y Modalidad (Respetando En Proceso)",
            Dock = DockStyle.Top,
            Height = 96,
            BackColor = AppTheme.SurfaceCard,
            Font = AppTheme.FontBodyBold,
            ForeColor = AppTheme.NavyDark
        };
        _chkAutoFolio.Text = "Folio automático seguro";
        _chkAutoFolio.Location = new Point(14, 24);
        _chkAutoFolio.AutoSize = true;
        _chkAutoFolio.Checked = _mode == TicketEditorMode.CreateNew;
        _chkAutoFolio.Enabled = _mode == TicketEditorMode.CreateNew;
        _chkAutoFolio.CheckedChanged += async (_, _) =>
        {
            _numFolio.Enabled = _mode == TicketEditorMode.CreateNew && !_chkAutoFolio.Checked;
            await ValidateFolioLiveAsync();
        };

        grpFolio.Controls.Add(new Label { Text = "Folio N°:", Location = new Point(214, 26), AutoSize = true, Font = AppTheme.FontBodyBold });
        _numFolio.SetBounds(282, 23, 108, 24);
        _numFolio.Enabled = false;
        _numFolio.ValueChanged += async (_, _) => await ValidateFolioLiveAsync();

        var btnCheckFolio = new Button { Location = new Point(398, 20), Size = new Size(146, 30) };
        AppTheme.StyleSecondaryButton(btnCheckFolio, AppIcon.Search, "Validar Folio", "Verificar que el folio no colisione con histórico o En Proceso", null, _tt);
        btnCheckFolio.Visible = _mode == TicketEditorMode.CreateNew;
        btnCheckFolio.Click += async (_, _) => await ValidateFolioLiveAsync();

        _chkCrearEnProceso.Text = "Crear En Proceso (Tránsito)";
        _chkCrearEnProceso.Location = new Point(556, 24);
        _chkCrearEnProceso.AutoSize = true;
        _chkCrearEnProceso.Visible = _mode == TicketEditorMode.CreateNew;
        _chkCrearEnProceso.CheckedChanged += (_, _) => RecalcNeto();

        _lblFolioStatus.Location = new Point(14, 58);
        _lblFolioStatus.Size = new Size(720, 26);
        _lblFolioStatus.Font = AppTheme.FontBodyBold;
        _lblFolioStatus.ForeColor = AppTheme.NavyPrimary;
        _lblFolioStatus.Text = "Verificando disponibilidad de folio...";

        grpFolio.Controls.AddRange([_chkAutoFolio, _numFolio, btnCheckFolio, _chkCrearEnProceso, _lblFolioStatus]);

        // Sección Datos Comerciales y Vehículo
        var grpData = new Panel { Dock = DockStyle.Top, Height = 408, BackColor = AppTheme.SurfaceCard, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(12), Margin = new Padding(0, 10, 0, 0) };
        _cmbTipo.Items.AddRange(["1 - Recepción", "2 - Despacho", "3 - Mov. Interno"]);

        int y = 14;
        AddRowControl(grpData, "Patente *", _txtPatente, 14, y, 110);
        AddLookupButton(grpData, 228, y - 2, "Buscar camión", async () => await PickCamionAsync());
        AddRowControl(grpData, "Acoplado", _txtAcoplado, 276, y, 100);
        AddRowControl(grpData, "Tipo *", _cmbTipo, 480, y, 165);

        y += 36;
        AddRowControl(grpData, "Cliente (RUT)", _txtCliente, 14, y, 120);
        AddLookupButton(grpData, 238, y - 2, "Buscar cliente", async () => await PickClienteAsync());
        AddRowControl(grpData, "Producto", _txtProducto, 360, y, 140);
        AddLookupButton(grpData, 604, y - 2, "Buscar producto", async () => await PickProductoAsync());

        y += 36;
        AddRowControl(grpData, "Origen / Prov.", _txtOrigen, 14, y, 120);
        AddLookupButton(grpData, 238, y - 2, "Buscar origen / proveedor", async () => await PickOrigenAsync());
        AddRowControl(grpData, "Destino", _txtDestino, 360, y, 140);
        AddLookupButton(grpData, 604, y - 2, "Buscar destino", async () => await PickDestinoAsync());

        y += 36;
        AddRowControl(grpData, "Transportista", _txtTransp, 14, y, 120);
        AddLookupButton(grpData, 238, y - 2, "Buscar transportista", async () => await PickTranspAsync());
        AddRowControl(grpData, "Chofer", _txtChofer, 360, y, 220);
        AddLookupButton(grpData, 684, y - 2, "Buscar chofer", async () => await PickChoferAsync());

        y += 36;
        AddRowControl(grpData, "Guía Despacho", _txtGuia, 14, y, 180);
        AddRowControl(grpData, "Neto Inf. (kg)", _numNetoInf, 360, y, 130);

        y += 38;
        AddRowControl(grpData, "Fecha Ing.", _dtFechaIng, 14, y, 102);
        AddRowControl(grpData, "Hora Ing.", _txtHoraIng, 226, y, 56);
        AddRowControl(grpData, "Fecha Sal.", _dtFechaSal, 396, y, 102);
        AddRowControl(grpData, "Hora Sal.", _txtHoraSal, 608, y, 56);
        if (_mode == TicketEditorMode.EditInProcess)
        {
            _dtFechaSal.Enabled = false;
            _txtHoraSal.Enabled = false;
        }

        y += 40;
        AddRowControl(grpData, "Bruto (kg) *", _numBruto, 14, y, 120);
        AddRowControl(grpData, "Tara (kg) *", _numTara, 250, y, 120);
        AddRowControl(grpData, "Neto (kg)", _numNeto, 486, y, 130);
        _numBruto.ValueChanged += (_, _) => RecalcNeto();
        _numTara.ValueChanged += (_, _) => RecalcNeto();

        y += 36;
        _chkTaraReg.Text = "Tara Registrada"; _chkTaraReg.Location = new Point(114, y); _chkTaraReg.AutoSize = true;
        _chkAnulado.Text = "Ticket Anulado"; _chkAnulado.Location = new Point(280, y); _chkAnulado.AutoSize = true;
        _chkAnulado.Visible = _mode is TicketEditorMode.CreateNew or TicketEditorMode.EditHistorical;
        _chkAnulado.CheckedChanged += (_, _) => RecalcNeto();
        grpData.Controls.AddRange([_chkTaraReg, _chkAnulado]);

        y += 32;
        AddRowControl(grpData, "Observación 1", _txtObs1, 14, y, 610);
        y += 34;
        AddRowControl(grpData, "Observación 2", _txtObs2, 14, y, 610);

        body.Controls.Add(grpData);
        body.Controls.Add(grpFolio);
        Controls.AddRange([body, footer, header]);
    }

    private static void AddRowControl(Control parent, string label, Control ctl, int x, int y, int width)
    {
        parent.Controls.Add(new Label { Text = label, Location = new Point(x, y + 4), AutoSize = true, Font = new Font("Segoe UI", 8.75f, FontStyle.Bold) });
        ctl.Location = new Point(x + 98, y);
        ctl.Width = width;
        parent.Controls.Add(ctl);
    }

    private void AddLookupButton(Control parent, int x, int y, string tooltip, Func<Task> onClick)
    {
        var btn = new Button { Location = new Point(x, y), Size = new Size(30, 26) };
        AppTheme.StyleSecondaryButton(btn, AppIcon.Search, string.Empty, tooltip, null, _tt);
        btn.Click += async (_, _) => await onClick();
        parent.Controls.Add(btn);
    }

    private void BindFields()
    {
        if (_pesaje.FolioPesaje > 0)
        {
            _numFolio.Value = Math.Clamp(_pesaje.FolioPesaje, 1, 99_999_999);
        }
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
        _dtFechaIng.Value = _pesaje.FechaIngreso > DateTime.MinValue ? _pesaje.FechaIngreso : DateTime.Today;
        _txtHoraIng.Text = string.IsNullOrWhiteSpace(_pesaje.HoraIngreso) ? DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture) : _pesaje.HoraIngreso;
        _dtFechaSal.Value = _pesaje.FechaSalida > DateTime.MinValue ? _pesaje.FechaSalida : DateTime.Today;
        _txtHoraSal.Text = string.IsNullOrWhiteSpace(_pesaje.HoraSalida) ? DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture) : _pesaje.HoraSalida;
        _numBruto.Value = Math.Clamp(_pesaje.Bruto, 0, 200_000);
        _numTara.Value = Math.Clamp(_pesaje.Tara, 0, 200_000);
        _chkTaraReg.Checked = _pesaje.TaraRegistrada;
        _chkAnulado.Checked = _pesaje.Anulado;
        _txtObs1.Text = _pesaje.Observacion1;
        _txtObs2.Text = _pesaje.Observacion2;
        RecalcNeto();
    }

    private void RecalcNeto()
    {
        if (_mode == TicketEditorMode.EditInProcess || _chkCrearEnProceso.Checked || _chkAnulado.Checked)
        {
            _numNeto.Value = 0;
            return;
        }
        _numNeto.Value = WeighingCalculator.CalcularNeto((int)_numBruto.Value, (int)_numTara.Value);
    }

    private async Task InitializeFolioStateAsync()
    {
        try
        {
            if (_mode == TicketEditorMode.CreateNew)
            {
                int suggested = await _services.Weighing.GetNextSuggestedFolioAsync();
                _numFolio.Value = Math.Clamp(suggested, 1, 99_999_999);
            }
            await ValidateFolioLiveAsync();
        }
        catch (Exception ex)
        {
            _lblFolioStatus.Text = $"Aviso verificando folio: {ex.Message}";
            _lblFolioStatus.ForeColor = AppTheme.AccentAmber;
        }
    }

    private async Task<bool> ValidateFolioLiveAsync()
    {
        if (_mode != TicketEditorMode.CreateNew)
        {
            _lblFolioStatus.Text = _mode == TicketEditorMode.EditInProcess
                ? $"Folio #{_pesaje.FolioPesaje} reservado EN PROCESO (se edita sin alterar ni liberar el folio en tránsito)."
                : (_mode == TicketEditorMode.CompleteInProcess
                    ? $"Folio #{_pesaje.FolioPesaje} se cerrará conservando su número asignado de tránsito."
                    : $"Folio histórico #{_pesaje.FolioPesaje} bloqueado para edición segura (cero colisión).");
            _lblFolioStatus.ForeColor = AppTheme.NavyPrimary;
            return true;
        }

        if (_chkAutoFolio.Checked)
        {
            int nextFree = await _services.Weighing.GetNextSuggestedFolioAsync();
            _numFolio.Value = Math.Clamp(nextFree, 1, 99_999_999);
            _lblFolioStatus.Text = $"Asignación automática libre de conflicto — Próximo folio garantizado: #{nextFree} (respeta histórico y en proceso).";
            _lblFolioStatus.ForeColor = AppTheme.AccentEmerald;
            return true;
        }

        FolioAvailabilityResult res = await _services.Weighing.CheckFolioAvailabilityAsync((int)_numFolio.Value);
        _lblFolioStatus.Text = res.Mensaje;
        _lblFolioStatus.ForeColor = res.Disponible ? AppTheme.AccentEmerald : AppTheme.AccentDanger;
        return res.Disponible;
    }

    private async Task SaveAsync()
    {
        AccionAutorizacion req = _mode == TicketEditorMode.CreateNew ? AccionAutorizacion.AgregarRegistro : AccionAutorizacion.ModificarRegistro;
        if (!PermissionService.EstaAutorizado(_services.UsuarioActual, req))
        {
            MessageBox.Show(this, "No posee permisos suficientes para realizar esta operación.", "Seguridad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string pat = ChileanValidators.NormalizarPatente(_txtPatente.Text);
        if (!ChileanValidators.EsPatenteValida(pat))
        {
            MessageBox.Show(this, "Debe ingresar una patente válida (4 a 8 caracteres).", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int bruto = (int)_numBruto.Value;
        int tara = (int)_numTara.Value;
        bool enProcesoMode = _mode == TicketEditorMode.EditInProcess || (_mode == TicketEditorMode.CreateNew && _chkCrearEnProceso.Checked);
        if (enProcesoMode && bruto <= 0 && tara <= 0)
        {
            MessageBox.Show(this, "Un ticket En Proceso requiere al menos peso Bruto o Tara mayor a cero.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!enProcesoMode && (bruto <= 0 || tara <= 0))
        {
            MessageBox.Show(this, "Un ticket completado requiere Bruto y Tara mayores a 0 kg.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_mode == TicketEditorMode.CreateNew && !_chkAutoFolio.Checked && !await ValidateFolioLiveAsync())
        {
            MessageBox.Show(this, _lblFolioStatus.Text, "Conflicto de Folio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _pesaje.Patente = pat;
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
        _pesaje.FechaIngreso = _dtFechaIng.Value.Date;
        _pesaje.HoraIngreso = string.IsNullOrWhiteSpace(_txtHoraIng.Text) ? DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture) : _txtHoraIng.Text.Trim();
        _pesaje.FechaSalida = _dtFechaSal.Value.Date;
        _pesaje.HoraSalida = string.IsNullOrWhiteSpace(_txtHoraSal.Text) ? DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture) : _txtHoraSal.Text.Trim();
        _pesaje.Bruto = bruto;
        _pesaje.Tara = tara;
        _pesaje.TaraRegistrada = _chkTaraReg.Checked;
        _pesaje.Anulado = _chkAnulado.Checked;
        _pesaje.Observacion1 = _txtObs1.Text.Trim();
        _pesaje.Observacion2 = _txtObs2.Text.Trim();

        short actorId = _services.UsuarioActual?.Id ?? 0;

        try
        {
            switch (_mode)
            {
                case TicketEditorMode.CreateNew:
                    _pesaje.FolioPesaje = _chkAutoFolio.Checked ? 0 : (int)_numFolio.Value;
                    _pesaje.FolioPesaje = await _services.Weighing.CreateManualTicketAsync(_pesaje, _chkCrearEnProceso.Checked, actorId);
                    break;

                case TicketEditorMode.EditHistorical:
                    await _services.Weighing.ModificarPesajeAsync(_pesaje, actorId);
                    break;

                case TicketEditorMode.EditInProcess:
                    await _services.Weighing.ModificarTransitoAsync(new RegistroTransito
                    {
                        FolioPesaje = _pesaje.FolioPesaje,
                        Patente = _pesaje.Patente,
                        Acoplado = _pesaje.Acoplado,
                        Producto = _pesaje.Producto,
                        Cliente = _pesaje.Cliente,
                        Origen = _pesaje.Origen,
                        Destino = _pesaje.Destino,
                        GuiaDespacho = _pesaje.GuiaDespacho,
                        NetoInformado = _pesaje.NetoInformado,
                        FechaIngreso = _pesaje.FechaIngreso,
                        HoraIngreso = _pesaje.HoraIngreso,
                        Bruto = _pesaje.Bruto,
                        Tara = _pesaje.Tara,
                        Chofer = _pesaje.Chofer,
                        Observacion1 = _pesaje.Observacion1,
                        Transportista = _pesaje.Transportista,
                        TipoPesaje = _pesaje.TipoPesaje,
                        UsuarioIngreso = _pesaje.UsuarioIngreso
                    }, actorId);
                    break;

                case TicketEditorMode.CompleteInProcess:
                    _pesaje.UsuarioSalida = actorId;
                    _pesaje.FolioPesaje = await _services.Weighing.CompletarPesajeAsync(_pesaje);
                    break;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Error guardando ticket:\r\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Pesaje ClonePesaje(Pesaje p) => new()
    {
        FolioPesaje = p.FolioPesaje, Patente = p.Patente, Acoplado = p.Acoplado,
        FolioPesajeMop = p.FolioPesajeMop, Producto = p.Producto, NombreProducto = p.NombreProducto,
        Cliente = p.Cliente, NombreCliente = p.NombreCliente, Origen = p.Origen, NombreOrigen = p.NombreOrigen,
        Destino = p.Destino, NombreDestino = p.NombreDestino, GuiaDespacho = p.GuiaDespacho,
        NetoInformado = p.NetoInformado, FechaIngreso = p.FechaIngreso, HoraIngreso = p.HoraIngreso,
        FechaSalida = p.FechaSalida, HoraSalida = p.HoraSalida, FechaModificacion = p.FechaModificacion,
        HoraModificacion = p.HoraModificacion, Bruto = p.Bruto, Tara = p.Tara, Neto = p.Neto,
        TipoMop = p.TipoMop, Largo = p.Largo, MaxBruto = p.MaxBruto, EstadoMop = p.EstadoMop,
        Chofer = p.Chofer, NombreChofer = p.NombreChofer, Observacion1 = p.Observacion1,
        Observacion2 = p.Observacion2, Transportista = p.Transportista, NombreTransportista = p.NombreTransportista,
        TaraRegistrada = p.TaraRegistrada, TipoPesaje = p.TipoPesaje,
        UsuarioIngreso = p.UsuarioIngreso, NombreUsuarioIngreso = p.NombreUsuarioIngreso,
        UsuarioSalida = p.UsuarioSalida, NombreUsuarioSalida = p.NombreUsuarioSalida,
        Anulado = p.Anulado
    };

    private async Task PickCamionAsync()
    {
        List<Camion> list = await _services.Catalogs.GetCamionesAsync();
        using var dlg = new CatalogLookupDialog<Camion>("Seleccionar Camión", list, c => $"{c.Patente} {c.Acoplado}", ("Patente", c => c.Patente, 90), ("Acoplado", c => c.Acoplado, 80), ("Tara", c => $"{c.Tara:N0}", 85));
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null)
        {
            _txtPatente.Text = dlg.SelectedItem.Patente;
            _txtAcoplado.Text = dlg.SelectedItem.Acoplado;
            if (string.IsNullOrWhiteSpace(_txtTransp.Text)) _txtTransp.Text = dlg.SelectedItem.Transportista;
            if (string.IsNullOrWhiteSpace(_txtChofer.Text)) _txtChofer.Text = dlg.SelectedItem.Chofer;
        }
    }

    private async Task PickClienteAsync()
    {
        List<Cliente> list = await _services.Catalogs.GetClientesAsync();
        using var dlg = new CatalogLookupDialog<Cliente>("Seleccionar Cliente", list, c => $"{c.Rut} {c.Nombre}", ("RUT", c => $"{c.Rut}-{c.Digito}", 90), ("Nombre", c => c.Nombre, 220));
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null) _txtCliente.Text = dlg.SelectedItem.Rut;
    }

    private async Task PickProductoAsync()
    {
        List<Producto> list = await _services.Catalogs.GetProductosAsync();
        using var dlg = new CatalogLookupDialog<Producto>("Seleccionar Producto", list, p => $"{p.Codigo} {p.Descripcion}", ("Código", p => p.Codigo, 90), ("Descripción", p => p.Descripcion, 260));
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null) _txtProducto.Text = dlg.SelectedItem.Codigo;
    }

    private async Task PickOrigenAsync()
    {
        List<Origen> list = await _services.Catalogs.GetOrigenesAsync();
        using var dlg = new CatalogLookupDialog<Origen>("Seleccionar Origen / Proveedor", list, o => $"{o.Codigo} {o.Descripcion}", ("Código", o => o.Codigo, 80), ("Descripción", o => o.Descripcion, 260));
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null) _txtOrigen.Text = dlg.SelectedItem.Codigo;
    }

    private async Task PickDestinoAsync()
    {
        List<Destino> list = await _services.Catalogs.GetDestinosAsync();
        using var dlg = new CatalogLookupDialog<Destino>("Seleccionar Destino", list, d => $"{d.Codigo} {d.Descripcion}", ("Código", d => d.Codigo, 80), ("Descripción", d => d.Descripcion, 260));
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null) _txtDestino.Text = dlg.SelectedItem.Codigo;
    }

    private async Task PickTranspAsync()
    {
        List<Transportista> list = await _services.Catalogs.GetTransportistasAsync();
        using var dlg = new CatalogLookupDialog<Transportista>("Seleccionar Transportista", list, t => $"{t.Rut} {t.Nombre}", ("RUT", t => t.Rut, 95), ("Nombre", t => t.Nombre, 240));
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null) _txtTransp.Text = dlg.SelectedItem.Rut;
    }

    private async Task PickChoferAsync()
    {
        List<Chofer> list = await _services.Catalogs.GetChoferesAsync();
        using var dlg = new CatalogLookupDialog<Chofer>("Seleccionar Chofer", list, c => $"{c.Rut} {c.Nombre}", ("RUT", c => c.Rut, 95), ("Nombre", c => c.Nombre, 240));
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null) _txtChofer.Text = dlg.SelectedItem.Rut;
    }
}
