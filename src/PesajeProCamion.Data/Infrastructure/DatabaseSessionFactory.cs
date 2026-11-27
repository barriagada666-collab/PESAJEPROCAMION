using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using FirebirdSql.Data.FirebirdClient;
using PesajeProCamion.Data.Configuration;
using PesajeProCamion.Domain.Diagnostics;

namespace PesajeProCamion.Data.Infrastructure;

public sealed record DatabaseCapabilities(
    bool HasAuditLog,
    bool HasGenPesaje,
    bool HasGenMop,
    bool HasGenUsuario,
    bool HasGenLogIngreso,
    bool HasFoliosId,
    bool HasConfigId,
    bool HasDemoIdColumn,
    bool HasLogIngresoId,
    bool HasTranAnulAuditColumns,
    bool TranAnulGdespachoIsInteger,
    int PasswordMaxLength,
    int ObservacionMaxLength,
    int ChoferPesajeMaxLength,
    int UsernameMaxLength)
{
    public bool IsLegacySchema => !HasAuditLog || !HasGenPesaje || !HasLogIngresoId || PasswordMaxLength < 60;
}

/// <summary>Fábrica centralizada de conexiones, detección de capacidades (moderna/remota legacy), transacciones y lectura segura.</summary>
public sealed class DatabaseSessionFactory
{
    private static readonly ConcurrentDictionary<string, DatabaseCapabilities> CapabilitiesCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DatabaseSettings _settings;

    public DatabaseSessionFactory(DatabaseSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    public DatabaseSettings Settings => _settings;

    public static void InvalidateCapabilitiesCache() => CapabilitiesCache.Clear();

    public async Task<FbConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new FbConnection(_settings.BuildConnectionString());
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }

    public async Task<DatabaseCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        string key = _settings.BuildConnectionString();
        if (CapabilitiesCache.TryGetValue(key, out DatabaseCapabilities? cached))
        {
            return cached;
        }
        await using FbConnection conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        DatabaseCapabilities caps = await DetectCapabilitiesAsync(conn, ct).ConfigureAwait(false);
        CapabilitiesCache[key] = caps;
        return caps;
    }

    public static async Task<DatabaseCapabilities> DetectCapabilitiesAsync(FbConnection conn, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conn);
        var generators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var gc = new FbCommand("SELECT TRIM(RDB$GENERATOR_NAME) FROM RDB$GENERATORS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0", conn))
        await using (FbDataReader gr = await gc.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await gr.ReadAsync(ct).ConfigureAwait(false))
            {
                generators.Add(gr.GetString(0).Trim());
            }
        }

        bool hasAudit = await TableExistsAsync(conn, "DBAUDITLOG", ct).ConfigureAwait(false);
        bool hasFoliosId = await ColumnExistsAsync(conn, "DBFOLIOS", "ID", ct).ConfigureAwait(false);
        bool hasConfigId = await ColumnExistsAsync(conn, "DBCONFIG", "ID", ct).ConfigureAwait(false);
        bool hasDemoId = await ColumnExistsAsync(conn, "DEMO1", "ID_MUESTRA", ct).ConfigureAwait(false);
        bool hasLogId = await ColumnExistsAsync(conn, "DBLOGSINGRESO", "ID_LOG", ct).ConfigureAwait(false);
        bool hasTranAnulAudit = await ColumnExistsAsync(conn, "DBTRAN_ANUL", "USUAANUL", ct).ConfigureAwait(false);
        bool tranAnulGdInt = await IsIntegerColumnAsync(conn, "DBTRAN_ANUL", "GDESPACHO", ct).ConfigureAwait(false);
        int pwLen = await GetColumnLengthAsync(conn, "DBUSUARIOS", "PASS_WORD", 10, ct).ConfigureAwait(false);
        int obsLen = await GetColumnLengthAsync(conn, "DBPESAJES", "OBSERVA1", 50, ct).ConfigureAwait(false);
        int choferLen = await GetColumnLengthAsync(conn, "DBPESAJES", "CHOFER", 12, ct).ConfigureAwait(false);
        int userLen = await GetColumnLengthAsync(conn, "DBUSUARIOS", "NOMBRE", 20, ct).ConfigureAwait(false);

        return new DatabaseCapabilities(
            HasAuditLog: hasAudit,
            HasGenPesaje: generators.Contains("GEN_FLPESAJE"),
            HasGenMop: generators.Contains("GEN_FLPESAJEMOP"),
            HasGenUsuario: generators.Contains("GEN_FLUSUARIO"),
            HasGenLogIngreso: generators.Contains("GEN_LOGINGRESO"),
            HasFoliosId: hasFoliosId,
            HasConfigId: hasConfigId,
            HasDemoIdColumn: hasDemoId,
            HasLogIngresoId: hasLogId,
            HasTranAnulAuditColumns: hasTranAnulAudit,
            TranAnulGdespachoIsInteger: tranAnulGdInt,
            PasswordMaxLength: pwLen,
            ObservacionMaxLength: obsLen,
            ChoferPesajeMaxLength: choferLen,
            UsernameMaxLength: userLen);
    }

    public async Task ExecuteInTransactionAsync(Func<FbConnection, FbTransaction, Task> action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using FbConnection conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using FbTransaction tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        try
        {
            await action(conn, tx).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            AppLogger.Error("SQL", "Transacción revertida por error de base de datos.", ex);
            throw;
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<FbConnection, FbTransaction, Task<T>> func, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        await using FbConnection conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using FbTransaction tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        try
        {
            T result = await func(conn, tx).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            AppLogger.Error("SQL", "Transacción revertida por error de base de datos.", ex);
            throw;
        }
    }

    private static async Task<bool> TableExistsAsync(FbConnection conn, string table, CancellationToken ct)
    {
        await using var cmd = new FbCommand("SELECT COUNT(*) FROM RDB$RELATIONS WHERE TRIM(RDB$RELATION_NAME) = @T", conn);
        AddParam(cmd, "@T", table);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<bool> ColumnExistsAsync(FbConnection conn, string table, string col, CancellationToken ct)
    {
        await using var cmd = new FbCommand("SELECT COUNT(*) FROM RDB$RELATION_FIELDS WHERE TRIM(RDB$RELATION_NAME) = @T AND TRIM(RDB$FIELD_NAME) = @C", conn);
        AddParam(cmd, "@T", table);
        AddParam(cmd, "@C", col);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<int> GetColumnLengthAsync(FbConnection conn, string table, string col, int fallback, CancellationToken ct)
    {
        await using var cmd = new FbCommand("""
            SELECT FIRST 1 COALESCE(F.RDB$CHARACTER_LENGTH, F.RDB$FIELD_LENGTH)
            FROM RDB$RELATION_FIELDS RF
            JOIN RDB$FIELDS F ON F.RDB$FIELD_NAME = RF.RDB$FIELD_SOURCE
            WHERE TRIM(RF.RDB$RELATION_NAME) = @T AND TRIM(RF.RDB$FIELD_NAME) = @C
            """, conn);
        AddParam(cmd, "@T", table);
        AddParam(cmd, "@C", col);
        object? val = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return val is null or DBNull ? fallback : Convert.ToInt32(val, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> IsIntegerColumnAsync(FbConnection conn, string table, string col, CancellationToken ct)
    {
        await using var cmd = new FbCommand("""
            SELECT FIRST 1 F.RDB$FIELD_TYPE
            FROM RDB$RELATION_FIELDS RF
            JOIN RDB$FIELDS F ON F.RDB$FIELD_NAME = RF.RDB$FIELD_SOURCE
            WHERE TRIM(RF.RDB$RELATION_NAME) = @T AND TRIM(RF.RDB$FIELD_NAME) = @C
            """, conn);
        AddParam(cmd, "@T", table);
        AddParam(cmd, "@C", col);
        object? val = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (val is null or DBNull) return false;
        int fType = Convert.ToInt32(val, CultureInfo.InvariantCulture);
        return fType is 7 or 8 or 16; // SMALLINT(7), INTEGER(8), BIGINT(16)
    }

    public static string FitLength(string? value, int maxLength)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        return maxLength > 0 && trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    public static void AddParam(FbCommand cmd, string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    public static string GetStr(IDataRecord r, string col)
    {
        int idx = r.GetOrdinal(col);
        return r.IsDBNull(idx) ? string.Empty : Convert.ToString(r.GetValue(idx), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }

    public static string? GetNullableStr(IDataRecord r, string col)
    {
        int idx = r.GetOrdinal(col);
        if (r.IsDBNull(idx))
        {
            return null;
        }
        string? s = Convert.ToString(r.GetValue(idx), CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public static int GetInt(IDataRecord r, string col, int fallback = 0)
    {
        int idx = r.GetOrdinal(col);
        return r.IsDBNull(idx) ? fallback : Convert.ToInt32(r.GetValue(idx), CultureInfo.InvariantCulture);
    }

    public static short GetShort(IDataRecord r, string col, short fallback = 0)
    {
        int idx = r.GetOrdinal(col);
        return r.IsDBNull(idx) ? fallback : Convert.ToInt16(r.GetValue(idx), CultureInfo.InvariantCulture);
    }

    public static double GetDouble(IDataRecord r, string col, double fallback = 0.0)
    {
        int idx = r.GetOrdinal(col);
        return r.IsDBNull(idx) ? fallback : Convert.ToDouble(r.GetValue(idx), CultureInfo.InvariantCulture);
    }

    public static bool GetFlag(IDataRecord r, string col, bool fallback = false)
    {
        string s = GetStr(r, col);
        return s.Length == 0 ? fallback : s == "1" || s.Equals("S", StringComparison.OrdinalIgnoreCase) || s.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
    }

    public static DateTime? GetDate(IDataRecord r, string col)
    {
        int idx = r.GetOrdinal(col);
        if (r.IsDBNull(idx))
        {
            return null;
        }
        DateTime dt = Convert.ToDateTime(r.GetValue(idx), CultureInfo.InvariantCulture);
        return dt.Year <= 1900 ? null : dt.Date;
    }
}
