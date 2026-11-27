using System.Data;
using System.Globalization;
using FirebirdSql.Data.FirebirdClient;
using PesajeProCamion.Data.Configuration;

namespace PesajeProCamion.Data.Infrastructure;

/// <summary>Fábrica centralizada de conexiones, transacciones y lectura segura de parámetros/campos Firebird.</summary>
public sealed class DatabaseSessionFactory
{
    private readonly DatabaseSettings _settings;

    public DatabaseSessionFactory(DatabaseSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    public DatabaseSettings Settings => _settings;

    public async Task<FbConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new FbConnection(_settings.BuildConnectionString());
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
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
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
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
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
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
