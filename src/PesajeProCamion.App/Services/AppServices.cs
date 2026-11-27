using PesajeProCamion.Data.Configuration;
using PesajeProCamion.Data.Infrastructure;
using PesajeProCamion.Data.Repositories;
using PesajeProCamion.Domain.Entities;
using PesajeProCamion.Hardware.Scale;

namespace PesajeProCamion.App.Services;

/// <summary>Contenedor de servicios de sesión activa compartidos por todos los módulos de la aplicación.</summary>
public sealed class AppServices : IDisposable
{
    public AppServices(DatabaseSettings settings, bool testMode = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        DatabaseSettings = settings;
        TestMode = testMode;
        SessionFactory = new DatabaseSessionFactory(settings);
        Catalogs = new CatalogRepository(SessionFactory);
        Security = new SecurityRepository(SessionFactory);
        Weighing = new WeighingRepository(SessionFactory);
        Mop = new MopRepository(SessionFactory);
        Scale = new ScaleReaderService();
    }

    public DatabaseSettings DatabaseSettings { get; private set; }
    public bool TestMode { get; }
    public DatabaseSessionFactory SessionFactory { get; private set; }
    public CatalogRepository Catalogs { get; private set; }
    public SecurityRepository Security { get; private set; }
    public WeighingRepository Weighing { get; private set; }
    public MopRepository Mop { get; private set; }
    public ScaleReaderService Scale { get; }

    public Usuario? UsuarioActual { get; set; }
    public BasculaInfo BasculaActual { get; set; } = new();
    public ConfiguracionSistema ConfigActual { get; set; } = new();
    public int SistemaActivoId { get; set; } = 2; // 2 = Pesaje, 1 = Gestión

    public void UpdateDatabaseSettings(DatabaseSettings newSettings)
    {
        ArgumentNullException.ThrowIfNull(newSettings);
        DatabaseSettings = newSettings;
        SessionFactory = new DatabaseSessionFactory(newSettings);
        Catalogs = new CatalogRepository(SessionFactory);
        Security = new SecurityRepository(SessionFactory);
        Weighing = new WeighingRepository(SessionFactory);
        Mop = new MopRepository(SessionFactory);
        AppConfigStore.SaveDatabaseSettings(newSettings);
    }

    public async Task RefreshMetadataAsync(CancellationToken ct = default)
    {
        BasculaActual = await Catalogs.GetBasculaAsync(1, ct).ConfigureAwait(false);
        ConfigActual = await Catalogs.GetConfigAsync(ct).ConfigureAwait(false);
        Scale.UpdateConfig(ConfigActual);
    }

    public async Task RefreshMetadataSafeAsync(CancellationToken ct = default)
    {
        try
        {
            await RefreshMetadataAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Domain.Diagnostics.AppLogger.Warn("AppServices", $"Metadata parcial no cargada: {ex.Message}");
        }
    }

    public async Task InitializeScaleConnectionAsync(CancellationToken ct = default)
    {
        ConfigActual = await Catalogs.GetConfigAsync(ct).ConfigureAwait(false);
        HardwareSetup setup = await Catalogs.GetHardwareSetupAsync("BASCULA", ct).ConfigureAwait(false);
        if (TestMode || !Scale.TryOpenSerial(setup, ConfigActual, out _))
        {
            Scale.EnableSimulation(ConfigActual, 0);
        }
    }

    public void Dispose() => Scale.Dispose();
}
