using FirebirdSql.Data.FirebirdClient;

namespace PesajeProCamion.Data.Configuration;

/// <summary>Configuración de conexión a la base de datos Firebird 2.5+.</summary>
public sealed class DatabaseSettings
{
    public string DataSource { get; set; } = "localhost";
    public int Port { get; set; } = 3050;
    public string DatabasePath { get; set; } = GetDefaultDatabasePath();
    public string UserId { get; set; } = "SYSDBA";
    public string Password { get; set; } = "masterkey";
    public string Charset { get; set; } = "UTF8";
    public FbServerType ServerType { get; set; } = FbServerType.Default;

    public static string GetDefaultDatabasePath()
    {
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(programData, "PesajeProCamion", "Data", "WMDBSPC.FDB");
    }

    public string BuildConnectionString()
    {
        var csb = new FbConnectionStringBuilder
        {
            DataSource = DataSource,
            Port = Port,
            Database = DatabasePath,
            UserID = UserId,
            Password = Password,
            Charset = Charset,
            Dialect = 3,
            ServerType = ServerType,
            WireCrypt = FbWireCrypt.Disabled,
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 50,
            ConnectionTimeout = 15
        };
        return csb.ToString();
    }
}
