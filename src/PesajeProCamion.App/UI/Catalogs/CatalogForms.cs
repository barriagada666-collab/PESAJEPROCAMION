using System.Drawing;
using System.Windows.Forms;
using PesajeProCamion.App.Services;
using PesajeProCamion.App.UI.Dialogs;
using PesajeProCamion.App.UI.Theme;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Domain.Validation;

namespace PesajeProCamion.App.UI.Catalogs;

/// <summary>Catálogo de Clientes con validación automática de RUT chileno Módulo 11.</summary>
public sealed class ClientesForm : CatalogCrudBaseForm<Cliente>
{
    private readonly TextBox _txtRut;
    private readonly TextBox _txtDv;
    private readonly TextBox _txtNombre;
    private readonly TextBox _txtGiro;
    private readonly TextBox _txtDir;
    private readonly TextBox _txtComuna;
    private readonly TextBox _txtCiudad;
    private readonly TextBox _txtTel;
    private readonly TextBox _txtFax;
    private readonly TextBox _txtCasilla;

    public ClientesForm(AppServices services) : base(services, "Catálogo de Clientes", 660, 440)
    {
        AddFieldLabel("RUT (sin puntos)", 18, 16);
        _txtRut = AddTextBox(150, 14, 150, 10);
        AddFieldLabel("-", 308, 16);
        _txtDv = AddTextBox(326, 14, 38, 1, CharacterCasing.Upper);
        _txtRut.Leave += (_, _) =>
        {
            string dv = ChileanValidators.CalcularDigitoVerificador(_txtRut.Text);
            if (!string.IsNullOrEmpty(dv) && string.IsNullOrWhiteSpace(_txtDv.Text))
            {
                _txtDv.Text = dv;
            }
        };

        AddFieldLabel("Razón Social / Nombre", 18, 52);
        _txtNombre = AddTextBox(150, 50, 470, 60, CharacterCasing.Upper);
        AddFieldLabel("Giro", 18, 88);
        _txtGiro = AddTextBox(150, 86, 470, 40, CharacterCasing.Upper);
        AddFieldLabel("Dirección", 18, 124);
        _txtDir = AddTextBox(150, 122, 470, 60, CharacterCasing.Upper);
        AddFieldLabel("Comuna", 18, 160);
        _txtComuna = AddTextBox(150, 158, 210, 40, CharacterCasing.Upper);
        AddFieldLabel("Ciudad", 374, 160);
        _txtCiudad = AddTextBox(434, 158, 186, 30, CharacterCasing.Upper);
        AddFieldLabel("Teléfono", 18, 196);
        _txtTel = AddTextBox(150, 194, 210, 25);
        AddFieldLabel("Fax", 374, 196);
        _txtFax = AddTextBox(434, 194, 186, 25);
        AddFieldLabel("Casilla", 18, 232);
        _txtCasilla = AddTextBox(150, 230, 210, 25);
    }

    protected override Task<List<Cliente>> FetchAllAsync() => Services.Catalogs.GetClientesAsync();
    protected override Task PersistEntityAsync(Cliente entity, short usuarioId) => Services.Catalogs.SaveClienteAsync(entity, usuarioId);
    protected override Task DeleteByKeyAsync(string key) => Services.Catalogs.DeleteClienteAsync(key);
    protected override string GetItemKey(Cliente item) => item.Rut;
    protected override void SetKeyEditable(bool editable) => _txtRut.ReadOnly = !editable;

    protected override void ClearInputs()
    {
        _txtRut.Text = _txtDv.Text = _txtNombre.Text = _txtGiro.Text = _txtDir.Text = string.Empty;
        _txtComuna.Text = _txtCiudad.Text = _txtTel.Text = _txtFax.Text = _txtCasilla.Text = string.Empty;
    }

    protected override void BindItem(Cliente item)
    {
        _txtRut.Text = item.Rut;
        _txtDv.Text = item.Digito;
        _txtNombre.Text = item.Nombre;
        _txtGiro.Text = item.Giro;
        _txtDir.Text = item.Direccion;
        _txtComuna.Text = item.Comuna;
        _txtCiudad.Text = item.Ciudad;
        _txtTel.Text = item.Telefono;
        _txtFax.Text = item.Fax;
        _txtCasilla.Text = item.Casilla;
    }

    protected override Cliente BuildEntityFromInputs() => new()
    {
        Rut = _txtRut.Text.Trim(),
        Digito = _txtDv.Text.Trim().ToUpperInvariant(),
        Nombre = _txtNombre.Text.Trim(),
        Giro = _txtGiro.Text.Trim(),
        Direccion = _txtDir.Text.Trim(),
        Comuna = _txtComuna.Text.Trim(),
        Ciudad = _txtCiudad.Text.Trim(),
        Telefono = _txtTel.Text.Trim(),
        Fax = _txtFax.Text.Trim(),
        Casilla = _txtCasilla.Text.Trim()
    };

    protected override string? ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(_txtRut.Text)) return "Debe ingresar el RUT del cliente.";
        if (string.IsNullOrWhiteSpace(_txtNombre.Text)) return "Debe ingresar el nombre del cliente.";
        if (!ChileanValidators.ValidarRut(_txtRut.Text, _txtDv.Text))
        {
            return $"El dígito verificador '{_txtDv.Text}' no corresponde al RUT '{_txtRut.Text}' (esperado: {ChileanValidators.CalcularDigitoVerificador(_txtRut.Text)}).";
        }
        return null;
    }

    protected override Cliente? ShowLookupDialog(IReadOnlyList<Cliente> items)
    {
        using var dlg = new CatalogLookupDialog<Cliente>(
            "Listado de Clientes", items,
            c => $"{c.Rut} {c.Nombre} {c.Ciudad}",
            ("RUT", c => $"{c.Rut}-{c.Digito}", 90),
            ("Nombre", c => c.Nombre, 200),
            ("Giro", c => c.Giro, 130),
            ("Ciudad", c => c.Ciudad, 110));
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedItem : null;
    }
}

/// <summary>Catálogo de Camiones con normalización de patente y gestión de tara registrada.</summary>
public sealed class CamionesForm : CatalogCrudBaseForm<Camion>
{
    private readonly TextBox _txtPatente;
    private readonly TextBox _txtAcoplado;
    private readonly TextBox _txtTransp;
    private readonly TextBox _txtChofer;
    private readonly NumericUpDown _numTmop = new();
    private readonly NumericUpDown _numLargo = new();
    private readonly NumericUpDown _numTara = new();
    private readonly CheckBox _chkTaraReg = new();
    private readonly NumericUpDown _numDias = new();
    private readonly DateTimePicker _dtTaraReg = new();

    public CamionesForm(AppServices services) : base(services, "Catálogo de Camiones", 640, 420)
    {
        AddFieldLabel("Patente", 18, 16);
        _txtPatente = AddTextBox(150, 14, 150, 8, CharacterCasing.Upper);
        AddFieldLabel("Acoplado", 330, 16);
        _txtAcoplado = AddTextBox(420, 14, 150, 8, CharacterCasing.Upper);

        AddFieldLabel("Transportista (RUT)", 18, 54);
        _txtTransp = AddTextBox(150, 52, 150, 10, CharacterCasing.Upper);
        var btnLkTr = new Button { Location = new Point(306, 50), Size = new Size(34, 26) };
        AppTheme.StyleSecondaryButton(btnLkTr, AppIcon.Search, string.Empty, "Buscar transportista", null, Tip);
        btnLkTr.Click += async (_, _) =>
        {
            List<Transportista> trs = await Services.Catalogs.GetTransportistasAsync();
            using var dlg = new CatalogLookupDialog<Transportista>("Transportistas", trs, t => $"{t.Rut} {t.Nombre}", ("RUT", t => t.Rut, 90), ("Nombre", t => t.Nombre, 220));
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null) _txtTransp.Text = dlg.SelectedItem.Rut;
        };
        FormSurface.Controls.Add(btnLkTr);

        AddFieldLabel("Chofer (RUT/Nombre)", 18, 92);
        _txtChofer = AddTextBox(150, 90, 260, 35, CharacterCasing.Upper);
        var btnLkCh = new Button { Location = new Point(416, 88), Size = new Size(34, 26) };
        AppTheme.StyleSecondaryButton(btnLkCh, AppIcon.Search, string.Empty, "Buscar chofer", null, Tip);
        btnLkCh.Click += async (_, _) =>
        {
            List<Chofer> chs = await Services.Catalogs.GetChoferesAsync();
            using var dlg = new CatalogLookupDialog<Chofer>("Choferes", chs, c => $"{c.Rut} {c.Nombre}", ("RUT", c => c.Rut, 90), ("Nombre", c => c.Nombre, 220));
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedItem is not null) _txtChofer.Text = dlg.SelectedItem.Rut;
        };
        FormSurface.Controls.Add(btnLkCh);

        AddFieldLabel("Tipo MOP", 18, 130);
        _numTmop.SetBounds(150, 128, 100, 24);
        _numTmop.Maximum = 9999;
        AddFieldLabel("Largo (m)", 280, 130);
        _numLargo.SetBounds(360, 128, 90, 24);
        _numLargo.Maximum = 99;

        AddFieldLabel("Tara Actual (kg)", 18, 168);
        _numTara.SetBounds(150, 166, 130, 24);
        _numTara.Maximum = 100_000;
        _numTara.ThousandsSeparator = true;

        _chkTaraReg.Text = "Permitir usar Tara Registrada";
        _chkTaraReg.Location = new Point(150, 206);
        _chkTaraReg.AutoSize = true;

        AddFieldLabel("Fecha Registro", 18, 242);
        _dtTaraReg.SetBounds(150, 240, 150, 24);
        _dtTaraReg.Format = DateTimePickerFormat.Short;
        AddFieldLabel("Vigencia (días)", 320, 242);
        _numDias.SetBounds(435, 240, 90, 24);
        _numDias.Maximum = 3650;

        FormSurface.Controls.AddRange([_numTmop, _numLargo, _numTara, _chkTaraReg, _dtTaraReg, _numDias]);
    }

    protected override Task<List<Camion>> FetchAllAsync() => Services.Catalogs.GetCamionesAsync();
    protected override Task PersistEntityAsync(Camion entity, short usuarioId) => Services.Catalogs.SaveCamionAsync(entity, usuarioId);
    protected override Task DeleteByKeyAsync(string key) => Services.Catalogs.DeleteCamionAsync(key);
    protected override string GetItemKey(Camion item) => item.Patente;
    protected override void SetKeyEditable(bool editable) => _txtPatente.ReadOnly = !editable;

    protected override void ClearInputs()
    {
        _txtPatente.Text = _txtAcoplado.Text = _txtTransp.Text = _txtChofer.Text = string.Empty;
        _numTmop.Value = _numLargo.Value = _numTara.Value = _numDias.Value = 0;
        _chkTaraReg.Checked = false;
        _dtTaraReg.Value = DateTime.Today;
    }

    protected override void BindItem(Camion item)
    {
        _txtPatente.Text = item.Patente;
        _txtAcoplado.Text = item.Acoplado;
        _txtTransp.Text = item.Transportista;
        _txtChofer.Text = item.Chofer;
        _numTmop.Value = Math.Clamp((int)item.TipoMop, 0, 9999);
        _numLargo.Value = Math.Clamp((int)item.Largo, 0, 99);
        _numTara.Value = Math.Clamp(item.Tara, 0, 100_000);
        _chkTaraReg.Checked = item.TaraRegistrada;
        _dtTaraReg.Value = item.FechaTaraRegistrada ?? DateTime.Today;
        _numDias.Value = Math.Clamp((int)item.DiasValidez, 0, 3650);
    }

    protected override Camion BuildEntityFromInputs() => new()
    {
        Patente = ChileanValidators.NormalizarPatente(_txtPatente.Text),
        Acoplado = ChileanValidators.NormalizarPatente(_txtAcoplado.Text),
        Transportista = _txtTransp.Text.Trim(),
        Chofer = _txtChofer.Text.Trim(),
        TipoMop = (short)_numTmop.Value,
        Largo = (short)_numLargo.Value,
        Tara = (int)_numTara.Value,
        TaraRegistrada = _chkTaraReg.Checked,
        FechaTaraRegistrada = _chkTaraReg.Checked ? _dtTaraReg.Value.Date : null,
        DiasValidez = (short)_numDias.Value,
        FechaTara = DateTime.Today,
        HoraTara = DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)
    };

    protected override string? ValidateInputs()
    {
        string pat = ChileanValidators.NormalizarPatente(_txtPatente.Text);
        if (!ChileanValidators.EsPatenteValida(pat)) return "Debe ingresar una patente válida (4 a 8 caracteres alfanuméricos).";
        return null;
    }

    protected override Camion? ShowLookupDialog(IReadOnlyList<Camion> items)
    {
        using var dlg = new CatalogLookupDialog<Camion>(
            "Listado de Camiones", items,
            c => $"{c.Patente} {c.Acoplado} {c.Transportista}",
            ("Patente", c => c.Patente, 90),
            ("Acoplado", c => c.Acoplado, 80),
            ("Tara (kg)", c => $"{c.Tara:N0}", 85),
            ("Tara Reg.", c => c.TaraRegistrada ? "Sí" : "No", 70),
            ("Tipo MOP", c => c.TipoMop, 70));
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedItem : null;
    }
}

/// <summary>Catálogo de Productos.</summary>
public sealed class ProductosForm : CatalogCrudBaseForm<Producto>
{
    private readonly TextBox _txtCodigo;
    private readonly TextBox _txtDescrip;

    public ProductosForm(AppServices services) : base(services, "Catálogo de Productos", 560, 260)
    {
        AddFieldLabel("Código Producto", 18, 22);
        _txtCodigo = AddTextBox(160, 20, 170, 12, CharacterCasing.Upper);
        AddFieldLabel("Descripción", 18, 64);
        _txtDescrip = AddTextBox(160, 62, 355, 60, CharacterCasing.Upper);
    }

    protected override Task<List<Producto>> FetchAllAsync() => Services.Catalogs.GetProductosAsync();
    protected override Task PersistEntityAsync(Producto entity, short usuarioId) => Services.Catalogs.SaveProductoAsync(entity, usuarioId);
    protected override Task DeleteByKeyAsync(string key) => Services.Catalogs.DeleteProductoAsync(key);
    protected override string GetItemKey(Producto item) => item.Codigo;
    protected override void SetKeyEditable(bool editable) => _txtCodigo.ReadOnly = !editable;
    protected override void ClearInputs() => _txtCodigo.Text = _txtDescrip.Text = string.Empty;
    protected override void BindItem(Producto item) { _txtCodigo.Text = item.Codigo; _txtDescrip.Text = item.Descripcion; }
    protected override Producto BuildEntityFromInputs() => new() { Codigo = _txtCodigo.Text.Trim(), Descripcion = _txtDescrip.Text.Trim() };
    protected override string? ValidateInputs() => string.IsNullOrWhiteSpace(_txtCodigo.Text) || string.IsNullOrWhiteSpace(_txtDescrip.Text)
        ? "Debe ingresar el código y la descripción del producto." : null;
    protected override Producto? ShowLookupDialog(IReadOnlyList<Producto> items)
    {
        using var dlg = new CatalogLookupDialog<Producto>("Listado de Productos", items, p => $"{p.Codigo} {p.Descripcion}", ("Código", p => p.Codigo, 90), ("Descripción", p => p.Descripcion, 260));
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedItem : null;
    }
}

/// <summary>Catálogo de Orígenes (etiquetas corregidas respecto a legacy).</summary>
public sealed class OrigenesForm : CatalogCrudBaseForm<Origen>
{
    private readonly TextBox _txtCodigo;
    private readonly TextBox _txtDescrip;

    public OrigenesForm(AppServices services) : base(services, "Catálogo de Orígenes", 560, 260)
    {
        AddFieldLabel("Código Origen", 18, 22);
        _txtCodigo = AddTextBox(160, 20, 150, 10, CharacterCasing.Upper);
        AddFieldLabel("Descripción Origen", 18, 64);
        _txtDescrip = AddTextBox(160, 62, 355, 40, CharacterCasing.Upper);
    }

    protected override Task<List<Origen>> FetchAllAsync() => Services.Catalogs.GetOrigenesAsync();
    protected override Task PersistEntityAsync(Origen entity, short usuarioId) => Services.Catalogs.SaveOrigenAsync(entity, usuarioId);
    protected override Task DeleteByKeyAsync(string key) => Services.Catalogs.DeleteOrigenAsync(key);
    protected override string GetItemKey(Origen item) => item.Codigo;
    protected override void SetKeyEditable(bool editable) => _txtCodigo.ReadOnly = !editable;
    protected override void ClearInputs() => _txtCodigo.Text = _txtDescrip.Text = string.Empty;
    protected override void BindItem(Origen item) { _txtCodigo.Text = item.Codigo; _txtDescrip.Text = item.Descripcion; }
    protected override Origen BuildEntityFromInputs() => new() { Codigo = _txtCodigo.Text.Trim(), Descripcion = _txtDescrip.Text.Trim() };
    protected override string? ValidateInputs() => string.IsNullOrWhiteSpace(_txtCodigo.Text) || string.IsNullOrWhiteSpace(_txtDescrip.Text)
        ? "Debe ingresar código y descripción de origen." : null;
    protected override Origen? ShowLookupDialog(IReadOnlyList<Origen> items)
    {
        using var dlg = new CatalogLookupDialog<Origen>("Listado de Orígenes", items, o => $"{o.Codigo} {o.Descripcion}", ("Código", o => o.Codigo, 80), ("Descripción", o => o.Descripcion, 260));
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedItem : null;
    }
}

/// <summary>Catálogo de Destinos (etiquetas corregidas respecto a legacy).</summary>
public sealed class DestinosForm : CatalogCrudBaseForm<Destino>
{
    private readonly TextBox _txtCodigo;
    private readonly TextBox _txtDescrip;

    public DestinosForm(AppServices services) : base(services, "Catálogo de Destinos", 560, 260)
    {
        AddFieldLabel("Código Destino", 18, 22);
        _txtCodigo = AddTextBox(160, 20, 150, 10, CharacterCasing.Upper);
        AddFieldLabel("Descripción Destino", 18, 64);
        _txtDescrip = AddTextBox(160, 62, 355, 40, CharacterCasing.Upper);
    }

    protected override Task<List<Destino>> FetchAllAsync() => Services.Catalogs.GetDestinosAsync();
    protected override Task PersistEntityAsync(Destino entity, short usuarioId) => Services.Catalogs.SaveDestinoAsync(entity, usuarioId);
    protected override Task DeleteByKeyAsync(string key) => Services.Catalogs.DeleteDestinoAsync(key);
    protected override string GetItemKey(Destino item) => item.Codigo;
    protected override void SetKeyEditable(bool editable) => _txtCodigo.ReadOnly = !editable;
    protected override void ClearInputs() => _txtCodigo.Text = _txtDescrip.Text = string.Empty;
    protected override void BindItem(Destino item) { _txtCodigo.Text = item.Codigo; _txtDescrip.Text = item.Descripcion; }
    protected override Destino BuildEntityFromInputs() => new() { Codigo = _txtCodigo.Text.Trim(), Descripcion = _txtDescrip.Text.Trim() };
    protected override string? ValidateInputs() => string.IsNullOrWhiteSpace(_txtCodigo.Text) || string.IsNullOrWhiteSpace(_txtDescrip.Text)
        ? "Debe ingresar código y descripción de destino." : null;
    protected override Destino? ShowLookupDialog(IReadOnlyList<Destino> items)
    {
        using var dlg = new CatalogLookupDialog<Destino>("Listado de Destinos", items, d => $"{d.Codigo} {d.Descripcion}", ("Código", d => d.Codigo, 80), ("Descripción", d => d.Descripcion, 260));
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedItem : null;
    }
}

/// <summary>Catálogo de Choferes.</summary>
public sealed class ChoferesForm : CatalogCrudBaseForm<Chofer>
{
    private readonly TextBox _txtRut;
    private readonly TextBox _txtDv;
    private readonly TextBox _txtNombre;

    public ChoferesForm(AppServices services) : base(services, "Catálogo de Choferes", 580, 270)
    {
        AddFieldLabel("RUT Chofer", 18, 22);
        _txtRut = AddTextBox(140, 20, 140, 12);
        AddFieldLabel("-", 286, 22);
        _txtDv = AddTextBox(304, 20, 38, 1, CharacterCasing.Upper);
        _txtRut.Leave += (_, _) =>
        {
            string dv = ChileanValidators.CalcularDigitoVerificador(_txtRut.Text);
            if (!string.IsNullOrEmpty(dv) && string.IsNullOrWhiteSpace(_txtDv.Text)) _txtDv.Text = dv;
        };
        AddFieldLabel("Nombre Completo", 18, 64);
        _txtNombre = AddTextBox(140, 62, 390, 45, CharacterCasing.Upper);
    }

    protected override Task<List<Chofer>> FetchAllAsync() => Services.Catalogs.GetChoferesAsync();
    protected override Task PersistEntityAsync(Chofer entity, short usuarioId) => Services.Catalogs.SaveChoferAsync(entity, usuarioId);
    protected override Task DeleteByKeyAsync(string key) => Services.Catalogs.DeleteChoferAsync(key);
    protected override string GetItemKey(Chofer item) => item.Rut;
    protected override void SetKeyEditable(bool editable) => _txtRut.ReadOnly = !editable;
    protected override void ClearInputs() => _txtRut.Text = _txtDv.Text = _txtNombre.Text = string.Empty;
    protected override void BindItem(Chofer item) { _txtRut.Text = item.Rut; _txtDv.Text = item.Digito; _txtNombre.Text = item.Nombre; }
    protected override Chofer BuildEntityFromInputs() => new() { Rut = _txtRut.Text.Trim(), Digito = _txtDv.Text.Trim().ToUpperInvariant(), Nombre = _txtNombre.Text.Trim() };
    protected override string? ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(_txtRut.Text) || string.IsNullOrWhiteSpace(_txtNombre.Text)) return "Debe ingresar RUT y nombre del chofer.";
        return ChileanValidators.ValidarRut(_txtRut.Text, _txtDv.Text) ? null : "El dígito verificador del RUT del chofer es incorrecto.";
    }
    protected override Chofer? ShowLookupDialog(IReadOnlyList<Chofer> items)
    {
        using var dlg = new CatalogLookupDialog<Chofer>("Listado de Choferes", items, c => $"{c.Rut} {c.Nombre}", ("RUT", c => $"{c.Rut}-{c.Digito}", 100), ("Nombre", c => c.Nombre, 260));
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedItem : null;
    }
}

/// <summary>Catálogo de Transportistas.</summary>
public sealed class TransportistasForm : CatalogCrudBaseForm<Transportista>
{
    private readonly TextBox _txtRut;
    private readonly TextBox _txtDv;
    private readonly TextBox _txtNombre;
    private readonly TextBox _txtDir;
    private readonly TextBox _txtCiudad;
    private readonly TextBox _txtTel;
    private readonly TextBox _txtFax;

    public TransportistasForm(AppServices services) : base(services, "Catálogo de Transportistas", 620, 360)
    {
        AddFieldLabel("RUT Transportista", 18, 20);
        _txtRut = AddTextBox(150, 18, 140, 10);
        AddFieldLabel("-", 296, 20);
        _txtDv = AddTextBox(314, 18, 38, 1, CharacterCasing.Upper);
        _txtRut.Leave += (_, _) =>
        {
            string dv = ChileanValidators.CalcularDigitoVerificador(_txtRut.Text);
            if (!string.IsNullOrEmpty(dv) && string.IsNullOrWhiteSpace(_txtDv.Text)) _txtDv.Text = dv;
        };
        AddFieldLabel("Nombre / Razón", 18, 56);
        _txtNombre = AddTextBox(150, 54, 420, 45, CharacterCasing.Upper);
        AddFieldLabel("Dirección", 18, 92);
        _txtDir = AddTextBox(150, 90, 420, 60, CharacterCasing.Upper);
        AddFieldLabel("Ciudad", 18, 128);
        _txtCiudad = AddTextBox(150, 126, 200, 30, CharacterCasing.Upper);
        AddFieldLabel("Teléfono", 18, 164);
        _txtTel = AddTextBox(150, 162, 180, 25);
        AddFieldLabel("Fax", 350, 164);
        _txtFax = AddTextBox(390, 162, 180, 25);
    }

    protected override Task<List<Transportista>> FetchAllAsync() => Services.Catalogs.GetTransportistasAsync();
    protected override Task PersistEntityAsync(Transportista entity, short usuarioId) => Services.Catalogs.SaveTransportistaAsync(entity, usuarioId);
    protected override Task DeleteByKeyAsync(string key) => Services.Catalogs.DeleteTransportistaAsync(key);
    protected override string GetItemKey(Transportista item) => item.Rut;
    protected override void SetKeyEditable(bool editable) => _txtRut.ReadOnly = !editable;
    protected override void ClearInputs() => _txtRut.Text = _txtDv.Text = _txtNombre.Text = _txtDir.Text = _txtCiudad.Text = _txtTel.Text = _txtFax.Text = string.Empty;
    protected override void BindItem(Transportista item)
    {
        _txtRut.Text = item.Rut; _txtDv.Text = item.Digito; _txtNombre.Text = item.Nombre;
        _txtDir.Text = item.Direccion; _txtCiudad.Text = item.Ciudad; _txtTel.Text = item.Telefono; _txtFax.Text = item.Fax;
    }
    protected override Transportista BuildEntityFromInputs() => new()
    {
        Rut = _txtRut.Text.Trim(), Digito = _txtDv.Text.Trim().ToUpperInvariant(), Nombre = _txtNombre.Text.Trim(),
        Direccion = _txtDir.Text.Trim(), Ciudad = _txtCiudad.Text.Trim(), Telefono = _txtTel.Text.Trim(), Fax = _txtFax.Text.Trim()
    };
    protected override string? ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(_txtRut.Text) || string.IsNullOrWhiteSpace(_txtNombre.Text)) return "Debe ingresar RUT y nombre del transportista.";
        return ChileanValidators.ValidarRut(_txtRut.Text, _txtDv.Text) ? null : "El dígito verificador del transportista es incorrecto.";
    }
    protected override Transportista? ShowLookupDialog(IReadOnlyList<Transportista> items)
    {
        using var dlg = new CatalogLookupDialog<Transportista>("Listado de Transportistas", items, t => $"{t.Rut} {t.Nombre}", ("RUT", t => $"{t.Rut}-{t.Digito}", 100), ("Nombre", t => t.Nombre, 220), ("Ciudad", t => t.Ciudad, 120));
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedItem : null;
    }
}
