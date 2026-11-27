using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using FirebirdSql.Data.FirebirdClient;
using PesajeProCamion.Data.Infrastructure;
using PesajeProCamion.Domain.Diagnostics;

namespace PesajeProCamion.Data.Configuration;

public sealed record DatabaseProbeResult(
    bool Success,
    DatabaseSettings ResolvedSettings,
    bool TcpReachable,
    long LatencyMs,
    string ServerVersion,
    string Empresa,
    string BasculaDescripcion,
    int TotalPesajes,
    bool IsLegacySchema,
    string Summary,
    IReadOnlyList<string> AttemptLog,
    string? ErrorMessage);

/// <summary>
/// Configuración de conexión Firebird 2.5+ / ODS 9.1 remoto y local con resolución automática de rutas UNC de red.
/// </summary>
public sealed class DatabaseSettings
{
    static DatabaseSettings()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public string DataSource { get; set; } = "localhost";
    public int Port { get; set; } = 3050;
    public string DatabasePath { get; set; } = GetDefaultDatabasePath();
    public string UserId { get; set; } = "SYSDBA";
    public string Password { get; set; } = "masterkey";
    public string Charset { get; set; } = "UTF8";
    public int Dialect { get; set; } = 3;
    public int ConnectionTimeout { get; set; } = 10;
    public int CommandTimeout { get; set; } = 30;
    public string? RawNetworkPath { get; set; }

    public int EffectiveCommandTimeout => Math.Clamp(CommandTimeout, 5, 300);
    public FbServerType ServerType { get; set; } = FbServerType.Default;

    public static string GetDefaultDatabasePath()
    {
        string portableMarker = Path.Combine(AppContext.BaseDirectory, "Data", "WMDBSPC.FDB");
        if (File.Exists(portableMarker))
        {
            return portableMarker;
        }
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(programData, "PesajeProCamion", "Data", "WMDBSPC.FDB");
    }

    public DatabaseSettings Clone() => new()
    {
        DataSource = DataSource,
        Port = Port,
        DatabasePath = DatabasePath,
        UserId = UserId,
        Password = Password,
        Charset = Charset,
        Dialect = Dialect,
        ConnectionTimeout = ConnectionTimeout,
        CommandTimeout = CommandTimeout,
        RawNetworkPath = RawNetworkPath,
        ServerType = ServerType
    };

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
            Dialect = Dialect is 1 or 3 ? Dialect : 3,
            ServerType = ServerType,
            WireCrypt = FbWireCrypt.Disabled,
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 50,
            ConnectionTimeout = Math.Clamp(ConnectionTimeout, 2, 60)
        };
        return csb.ToString();
    }
}

/// <summary>
/// Resuelve rutas UNC de red (ej: \\192.168.2.56\bdwm\placilla2\WMDBSPC.GDB) o sintaxis host:/ruta
/// hacia los parámetros TCP reales del servidor Firebird y verifica conectividad, esquema y versión.
/// </summary>
public static class RemoteDatabaseResolver
{
    public static (string? ExtractedHost, IReadOnlyList<string> PathCandidates) BuildCandidates(string inputHost, string rawDbPath)
    {
        string trimmed = rawDbPath.Trim().Trim('"');
        string? host = string.IsNullOrWhiteSpace(inputHost) ? null : inputHost.Trim();
        var candidates = new List<string>();

        // 1. Sintaxis UNC Windows: \\host\share\carpeta\archivo.gdb
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            string withoutPrefix = trimmed[2..];
            string[] parts = withoutPrefix.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                host = parts[0];
                string share = parts[1];
                string relUnderShare = string.Join('/', parts.Skip(2));
                string relBackslash = string.Join('\\', parts.Skip(2));

                // Priorizar rutas relativas tipo Firebird/BDE (/share/...) y unidades típicas en el servidor remoto
                candidates.Add($"/{share}/{relUnderShare}");
                candidates.Add($@"C:\{share}\{relBackslash}");
                candidates.Add($@"D:\{share}\{relBackslash}");
                candidates.Add($@"E:\{share}\{relBackslash}");
                candidates.Add($"/{relUnderShare}");
                candidates.Add($@"C:\{relBackslash}");
            }
        }
        // 2. Sintaxis Firebird/BDE clásica: host:/ruta/archivo.gdb o host:C:\ruta\archivo.gdb
        else if (trimmed.Contains(':') && trimmed.IndexOf(':') > 1)
        {
            int colonIdx = trimmed.IndexOf(':');
            host = trimmed[..colonIdx].Trim();
            candidates.Add(trimmed[(colonIdx + 1)..].Trim());
        }
        else
        {
            candidates.Add(trimmed);
        }

        return (host, candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static async Task<bool> CheckTcpPortAsync(string host, int port, int timeoutMs = 2500, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }
        try
        {
            using var tcp = new TcpClient();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(timeoutMs);
            await tcp.ConnectAsync(host, port, linkedCts.Token).ConfigureAwait(false);
            return tcp.Connected;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    public static async Task<DatabaseProbeResult> ResolveAndProbeAsync(
        DatabaseSettings baseSettings,
        string? rawInputPath = null,
        bool autoDetectDialectAndCharset = true,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(baseSettings);
        string inputPath = string.IsNullOrWhiteSpace(rawInputPath) ? baseSettings.DatabasePath : rawInputPath.Trim();
        (string? extractedHost, IReadOnlyList<string> pathCandidates) = BuildCandidates(baseSettings.DataSource, inputPath);

        string effectiveHost = string.IsNullOrWhiteSpace(extractedHost) ? baseSettings.DataSource : extractedHost;
        var logs = new List<string>();
        var sw = Stopwatch.StartNew();

        bool isUnc = inputPath.Trim().StartsWith(@"\\", StringComparison.Ordinal);
        if (isUnc)
        {
            logs.Add($"Ruta UNC detectada -> Servidor '{effectiveHost}', candidatos servidor: {string.Join(" | ", pathCandidates)}");
        }

        bool tcpOk = await CheckTcpPortAsync(effectiveHost, baseSettings.Port, 2500, ct).ConfigureAwait(false);
        logs.Add(tcpOk
            ? $"TCP {effectiveHost}:{baseSettings.Port} accesible."
            : $"TCP {effectiveHost}:{baseSettings.Port} NO responde (verifique firewall/servicio Firebird).");

        if (!tcpOk && !string.Equals(effectiveHost, "localhost", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(effectiveHost, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            sw.Stop();
            string err = $"No se pudo establecer conexión TCP con {effectiveHost}:{baseSettings.Port}.";
            AppLogger.Warn("RemoteDB", err, string.Join(Environment.NewLine, logs));
            return new DatabaseProbeResult(false, baseSettings.Clone(), false, sw.ElapsedMilliseconds, "-", "-", "-", 0, false, err, logs, err);
        }

        int[] dialects = autoDetectDialectAndCharset
            ? (baseSettings.Dialect == 1 ? [1, 3] : (inputPath.EndsWith(".GDB", StringComparison.OrdinalIgnoreCase) ? [1, 3] : [3, 1]))
            : [baseSettings.Dialect];

        string[] charsets = autoDetectDialectAndCharset
            ? (string.Equals(baseSettings.Charset, "UTF8", StringComparison.OrdinalIgnoreCase)
                ? ["UTF8", "WIN1252", "NONE"]
                : [baseSettings.Charset, "UTF8", "NONE"])
            : [baseSettings.Charset];

        string? lastError = null;
        foreach (string pathCandidate in pathCandidates)
        {
            foreach (int dialect in dialects)
            {
                foreach (string charset in charsets.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();
                    DatabaseSettings trial = baseSettings.Clone();
                    trial.DataSource = effectiveHost;
                    trial.DatabasePath = pathCandidate;
                    trial.Dialect = dialect;
                    trial.Charset = charset;
                    trial.ConnectionTimeout = 4;
                    trial.CommandTimeout = 8;
                    if (isUnc)
                    {
                        trial.RawNetworkPath = inputPath;
                    }

                    try
                    {
                        await using var conn = new FbConnection(trial.BuildConnectionString());
                        await conn.OpenAsync(ct).ConfigureAwait(false);

                        DatabaseCapabilities caps = await DatabaseSessionFactory.DetectCapabilitiesAsync(conn, ct).ConfigureAwait(false);
                        int pesajesCount = 0;
                        string empresa = "-";
                        string bascDesc = "-";

                        await using (var cmd1 = new FbCommand("SELECT FIRST 1 NOMBRE, DESCRIP FROM DBBASCULAS", conn))
                        await using (FbDataReader r1 = await cmd1.ExecuteReaderAsync(ct).ConfigureAwait(false))
                        {
                            if (await r1.ReadAsync(ct).ConfigureAwait(false))
                            {
                                empresa = r1.IsDBNull(0) ? "-" : r1.GetString(0).Trim();
                                bascDesc = r1.IsDBNull(1) ? "-" : r1.GetString(1).Trim();
                            }
                        }

                        await using (var cmd2 = new FbCommand("SELECT COUNT(*) FROM DBPESAJES WHERE FLPESAJE IS NOT NULL", conn))
                        {
                            pesajesCount = Convert.ToInt32(await cmd2.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
                        }

                        sw.Stop();
                        trial.ConnectionTimeout = baseSettings.ConnectionTimeout;
                        trial.CommandTimeout = baseSettings.CommandTimeout;
                        string modeDesc = caps.IsLegacySchema ? "Esquema Legacy ODS 9 / Dialecto 1 Compatible" : "Esquema Moderno .NET/Firebird UTF8";
                        logs.Add($"ÉXITO: {effectiveHost}:{trial.Port} -> {pathCandidate} (Dialect={dialect}, Charset={charset}, {sw.ElapsedMilliseconds} ms)");
                        logs.Add($"Servidor: {conn.ServerVersion} | {modeDesc} | Empresa: {empresa} | Pesajes: {pesajesCount:N0}");
                        AppLogger.Info("RemoteDB", $"Conexión probada: {effectiveHost}:{trial.Port}/{pathCandidate} ({pesajesCount} pesajes)");

                        return new DatabaseProbeResult(
                            true,
                            trial,
                            tcpOk,
                            sw.ElapsedMilliseconds,
                            conn.ServerVersion ?? "Firebird",
                            empresa,
                            bascDesc,
                            pesajesCount,
                            caps.IsLegacySchema,
                            $"Conectado ({sw.ElapsedMilliseconds} ms) - {empresa} ({pesajesCount:N0} pesajes)",
                            logs,
                            null);
                    }
                    catch (Exception ex) when (ex is FbException or ArgumentException)
                    {
                        lastError = ex.Message;
                        logs.Add($"Intento [{pathCandidate} | D={dialect} | C={charset}]: {ex.Message.Replace('\r', ' ').Replace('\n', ' ')}");
                        // Si la ruta en sí no existe (no error de charset/dialect), no repetir charsets innecesariamente
                        if (ex.Message.Contains("I/O error", StringComparison.OrdinalIgnoreCase) ||
                            ex.Message.Contains("unavailable database", StringComparison.OrdinalIgnoreCase) ||
                            ex.Message.Contains("Error while trying to open file", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                    }
                }
            }
        }

        sw.Stop();
        string failMsg = lastError ?? "No se pudo conectar a la base de datos con los parámetros indicados.";
        AppLogger.ErrorDetail("RemoteDB", $"Fallo al conectar a {effectiveHost}:{baseSettings.Port}/{inputPath}", string.Join(Environment.NewLine, logs));
        return new DatabaseProbeResult(false, baseSettings.Clone(), tcpOk, sw.ElapsedMilliseconds, "-", "-", "-", 0, false, failMsg, logs, failMsg);
    }
}
