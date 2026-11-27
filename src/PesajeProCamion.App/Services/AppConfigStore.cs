using System.Text.Json;
using FirebirdSql.Data.FirebirdClient;
using PesajeProCamion.Data.Configuration;

namespace PesajeProCamion.App.Services;

/// <summary>Persistencia local de configuración de conexión Firebird y preferencias de ejecución.</summary>
public sealed class AppConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ConfigFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PesajeProCamion", "appsettings.json");

    public static DatabaseSettings LoadDatabaseSettings()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                StoredConfig? cfg = JsonSerializer.Deserialize<StoredConfig>(json);
                if (cfg is not null && !string.IsNullOrWhiteSpace(cfg.DatabasePath))
                {
                    return new DatabaseSettings
                    {
                        DataSource = cfg.DataSource,
                        Port = cfg.Port,
                        DatabasePath = cfg.DatabasePath,
                        UserId = cfg.UserId,
                        Password = cfg.Password,
                        Charset = string.IsNullOrWhiteSpace(cfg.Charset) ? "UTF8" : cfg.Charset,
                        ServerType = FbServerType.Default
                    };
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fallback a configuración por defecto si el archivo local está dañado
        }

        return new DatabaseSettings();
    }

    public static void SaveDatabaseSettings(DatabaseSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? dir = Path.GetDirectoryName(ConfigFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var payload = new StoredConfig
        {
            DataSource = settings.DataSource,
            Port = settings.Port,
            DatabasePath = settings.DatabasePath,
            UserId = settings.UserId,
            Password = settings.Password,
            Charset = settings.Charset
        };
        File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(payload, JsonOptions));
    }

    private sealed class StoredConfig
    {
        public string DataSource { get; set; } = "localhost";
        public int Port { get; set; } = 3050;
        public string DatabasePath { get; set; } = DatabaseSettings.GetDefaultDatabasePath();
        public string UserId { get; set; } = "SYSDBA";
        public string Password { get; set; } = "masterkey";
        public string Charset { get; set; } = "UTF8";
    }
}
