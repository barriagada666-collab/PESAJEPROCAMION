using System.Text.Json;
using FirebirdSql.Data.FirebirdClient;
using PesajeProCamion.Data.Configuration;
using PesajeProCamion.Domain.Diagnostics;
using PesajeProCamion.Domain.Entities;

namespace PesajeProCamion.App.Services;

/// <summary>
/// Persistencia de configuración Firebird compatible con instalación corporativa (%ProgramData%)
/// y modo portable cero-dependencias (archivo appsettings.json junto al ejecutable).
/// </summary>
public sealed class AppConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static bool IsPortableMode =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag")) ||
        File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.json")) ||
        !AppContext.BaseDirectory.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);

    public static string ConfigFilePath
    {
        get
        {
            string localPortable = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(localPortable) || File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag")))
            {
                return localPortable;
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PesajeProCamion", "appsettings.json");
        }
    }

    public static DatabaseSettings LoadDatabaseSettings()
    {
        foreach (string path in GetReadCandidates())
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }
                string json = File.ReadAllText(path);
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
                        Dialect = cfg.Dialect is 1 or 3 ? cfg.Dialect : 3,
                        ConnectionTimeout = cfg.ConnectionTimeout > 0 ? cfg.ConnectionTimeout : 10,
                        CommandTimeout = cfg.CommandTimeout > 0 ? cfg.CommandTimeout : 30,
                        RawNetworkPath = cfg.RawNetworkPath,
                        ServerType = FbServerType.Default
                    };
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                AppLogger.Warn("Config", $"No se pudo leer '{path}': {ex.Message}");
            }
        }

        return new DatabaseSettings();
    }

    public static void SaveDatabaseSettings(DatabaseSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        StoredConfig current = ReadStoredConfigOrDefault();
        current.DataSource = settings.DataSource;
        current.Port = settings.Port;
        current.DatabasePath = settings.DatabasePath;
        current.UserId = settings.UserId;
        current.Password = settings.Password;
        current.Charset = settings.Charset;
        current.Dialect = settings.Dialect;
        current.ConnectionTimeout = settings.ConnectionTimeout;
        current.CommandTimeout = settings.CommandTimeout;
        current.RawNetworkPath = settings.RawNetworkPath;
        SaveStoredConfig(current);
    }

    public static TicketFieldVisibility LoadTicketFields()
    {
        try
        {
            return ReadStoredConfigOrDefault().TicketFields?.Clone() ?? TicketFieldVisibility.Default;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Config", "Error cargando preferencias de visualización del ticket.", ex);
            return TicketFieldVisibility.Default;
        }
    }

    public static void SaveTicketFields(TicketFieldVisibility fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        try
        {
            StoredConfig current = ReadStoredConfigOrDefault();
            current.TicketFields = fields.Clone();
            SaveStoredConfig(current);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Config", "Error guardando preferencias de visualización del ticket.", ex);
            throw;
        }
    }

    private static StoredConfig ReadStoredConfigOrDefault()
    {
        foreach (string path in GetReadCandidates())
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }
                StoredConfig? cfg = JsonSerializer.Deserialize<StoredConfig>(File.ReadAllText(path));
                if (cfg is not null)
                {
                    return cfg;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                AppLogger.Warn("Config", $"No se pudo leer '{path}': {ex.Message}");
            }
        }
        return new StoredConfig();
    }

    private static void SaveStoredConfig(StoredConfig payload)
    {
        string targetPath = ConfigFilePath;
        try
        {
            WritePayloadFile(targetPath, payload);
        }
        catch (UnauthorizedAccessException)
        {
            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PesajeProCamion", "appsettings.json");
            WritePayloadFile(fallback, payload);
        }
    }

    private static IEnumerable<string> GetReadCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PesajeProCamion", "appsettings.json");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PesajeProCamion", "appsettings.json");
    }

    private static void WritePayloadFile(string path, StoredConfig payload)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
    }

    private sealed class StoredConfig
    {
        public string DataSource { get; set; } = "localhost";
        public int Port { get; set; } = 3050;
        public string DatabasePath { get; set; } = DatabaseSettings.GetDefaultDatabasePath();
        public string UserId { get; set; } = "SYSDBA";
        public string Password { get; set; } = "masterkey";
        public string Charset { get; set; } = "UTF8";
        public int Dialect { get; set; } = 3;
        public int ConnectionTimeout { get; set; } = 10;
        public int CommandTimeout { get; set; } = 30;
        public string? RawNetworkPath { get; set; }
        public TicketFieldVisibility? TicketFields { get; set; }
    }
}
