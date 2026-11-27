using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PesajeProCamion.Domain.Diagnostics;

public enum AppLogLevel
{
    Info,
    Warn,
    Error,
    Sql,
    Serial,
    Debug,
    Critical
}

public sealed record AppLogEntry(
    DateTime Timestamp,
    AppLogLevel Level,
    string Source,
    string Message,
    string? Detail,
    string? CorrelationId = null,
    int RepeatCount = 1);

/// <summary>
/// Sistema centralizado de trazabilidad y logs de errores con buffer en memoria, sanitización de secretos,
/// IDs de correlación, deduplicación de errores repetidos y archivo diario con rotación por tamaño y retención.
/// </summary>
public static partial class AppLogger
{
    private const int MaxMemoryEntries = 1000;
    private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromSeconds(5);
    private static readonly Lock SyncLock = new();
    private static readonly List<AppLogEntry> Entries = [];
    private static string? _logDirectory;
    private static DateTime _lastRetentionCheckDate = DateTime.MinValue;

    public static event EventHandler<AppLogEntry>? EntryLogged;
    public static bool IsSilentTestHost { get; set; }
    public static long MaxFileSizeBytes { get; set; } = 5L * 1024L * 1024L;
    public static int RetentionDays { get; set; } = 30;
    public static string? LastWriteWarning { get; private set; }

    public static string LogDirectory
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_logDirectory))
            {
                return _logDirectory;
            }
            _logDirectory = ResolveDefaultLogDirectory();
            return _logDirectory;
        }
    }

    public static string CurrentLogFilePath =>
        Path.Combine(LogDirectory, $"pesaje_{DateTime.Today:yyyyMMdd}.log");

    public static string NewCorrelationId() => $"OP-{Guid.NewGuid():N}"[..11].ToUpperInvariant();

    public static void ConfigureDirectory(string directory)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            lock (SyncLock)
            {
                _logDirectory = directory;
                _lastRetentionCheckDate = DateTime.MinValue;
            }
        }
    }

    public static void ResetDirectory()
    {
        lock (SyncLock)
        {
            _logDirectory = null;
            _lastRetentionCheckDate = DateTime.MinValue;
        }
    }

    public static void Info(string source, string message, string? detail = null)
        => Log(AppLogLevel.Info, source, message, detail);

    public static void Debug(string source, string message, string? detail = null)
        => Log(AppLogLevel.Debug, source, message, detail);

    public static void Warn(string source, string message, string? detail = null)
        => Log(AppLogLevel.Warn, source, message, detail, NewCorrelationId());

    public static void Error(string source, string message, Exception? ex = null)
        => Log(AppLogLevel.Error, source, message, ex?.ToString(), NewCorrelationId());

    public static string ErrorCorrelated(string source, string message, Exception? ex = null)
    {
        string corrId = NewCorrelationId();
        Log(AppLogLevel.Error, source, message, ex?.ToString(), corrId);
        return corrId;
    }

    public static string Critical(string source, string message, Exception? ex = null)
    {
        string corrId = NewCorrelationId();
        Log(AppLogLevel.Critical, source, message, ex?.ToString(), corrId);
        return corrId;
    }

    public static void ErrorDetail(string source, string message, string? detail)
        => Log(AppLogLevel.Error, source, message, detail, NewCorrelationId());

    public static void Sql(string source, string message, string? detail = null)
        => Log(AppLogLevel.Sql, source, message, detail);

    public static void Serial(string source, string message, string? detail = null)
        => Log(AppLogLevel.Serial, source, message, detail);

    public static string SanitizeText(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }
        string masked = CredentialPairRegex().Replace(input, "$1=***REDACTED***");
        return TokenRegex().Replace(masked, "***REDACTED***");
    }

    public static void Log(AppLogLevel level, string source, string message, string? detail = null, string? correlationId = null)
    {
        DateTime now = DateTime.Now;
        string cleanSource = string.IsNullOrWhiteSpace(source) ? "App" : source.Trim();
        string cleanMessage = SanitizeText(message);
        string? cleanDetail = detail is null ? null : SanitizeText(detail);
        string? effectiveCorr = string.IsNullOrWhiteSpace(correlationId)
            ? (level is AppLogLevel.Warn or AppLogLevel.Error or AppLogLevel.Critical ? NewCorrelationId() : null)
            : correlationId.Trim();

        AppLogEntry entry;
        bool shouldWriteToDisk = true;
        lock (SyncLock)
        {
            if (Entries.Count > 0)
            {
                AppLogEntry tail = Entries[^1];
                if (tail.Level == level &&
                    string.Equals(tail.Source, cleanSource, StringComparison.Ordinal) &&
                    string.Equals(tail.Message, cleanMessage, StringComparison.Ordinal) &&
                    string.Equals(tail.Detail, cleanDetail, StringComparison.Ordinal) &&
                    (now - tail.Timestamp) <= DeduplicationWindow)
                {
                    entry = tail with { Timestamp = now, RepeatCount = tail.RepeatCount + 1 };
                    Entries[^1] = entry;
                    shouldWriteToDisk = entry.RepeatCount % 10 == 0;
                    goto PersistAndEmit;
                }
            }

            entry = new AppLogEntry(now, level, cleanSource, cleanMessage, cleanDetail, effectiveCorr, 1);
            Entries.Add(entry);
            if (Entries.Count > MaxMemoryEntries)
            {
                Entries.RemoveRange(0, Entries.Count - MaxMemoryEntries);
            }

        PersistAndEmit:
            if (shouldWriteToDisk)
            {
                WriteToFileLocked(entry);
            }
        }

        EntryLogged?.Invoke(null, entry);
    }

    public static IReadOnlyList<AppLogEntry> GetEntries()
    {
        lock (SyncLock)
        {
            var snapshot = new List<AppLogEntry>(Entries.Count);
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                snapshot.Add(Entries[i]);
            }
            return snapshot;
        }
    }

    public static void ClearMemory()
    {
        lock (SyncLock)
        {
            Entries.Clear();
        }
    }

    public static void ExportToFile(string filePath, AppLogLevel? minLevel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        List<AppLogEntry> items;
        lock (SyncLock)
        {
            items = minLevel.HasValue ? Entries.Where(e => e.Level == minLevel.Value).ToList() : [.. Entries];
        }

        var sb = new StringBuilder();
        foreach (AppLogEntry e in items)
        {
            sb.AppendLine(FormatEntry(e));
        }
        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static int PruneExpiredLogs(string? directory = null)
    {
        string targetDir = string.IsNullOrWhiteSpace(directory) ? LogDirectory : directory;
        if (!Directory.Exists(targetDir))
        {
            return 0;
        }
        int retention = Math.Clamp(RetentionDays, 1, 365);
        DateTime cutoff = DateTime.Today.AddDays(-retention);
        int deleted = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(targetDir, "pesaje_*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastWriteWarning = SanitizeText($"No se pudieron depurar logs antiguos: {ex.Message}");
        }
        return deleted;
    }

    private static string FormatEntry(AppLogEntry e)
    {
        string corr = string.IsNullOrWhiteSpace(e.CorrelationId) ? "-" : e.CorrelationId;
        string rep = e.RepeatCount > 1 ? $" (x{e.RepeatCount})" : string.Empty;
        string baseLine = string.Create(CultureInfo.InvariantCulture,
            $"[{e.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{e.Level,-8}] [{corr}] [{e.Source}] {e.Message}{rep}");
        return string.IsNullOrWhiteSpace(e.Detail)
            ? baseLine
            : $"{baseLine}{Environment.NewLine}    {e.Detail.Replace(Environment.NewLine, Environment.NewLine + "    ", StringComparison.Ordinal)}";
    }

    private static void WriteToFileLocked(AppLogEntry entry)
    {
        try
        {
            string dir = LogDirectory;
            Directory.CreateDirectory(dir);
            if (_lastRetentionCheckDate != DateTime.Today)
            {
                _lastRetentionCheckDate = DateTime.Today;
                PruneExpiredLogs(dir);
            }

            string filePath = CurrentLogFilePath;
            RotateFileIfNeeded(filePath);
            File.AppendAllText(filePath, FormatEntry(entry) + Environment.NewLine, Encoding.UTF8);
            LastWriteWarning = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastWriteWarning = SanitizeText($"Fallo escritura en LogDirectory: {ex.Message}");
            TryWriteFallbackTempLog(entry);
        }
    }

    private static void RotateFileIfNeeded(string filePath)
    {
        long maxBytes = Math.Max(512L, MaxFileSizeBytes);
        if (!File.Exists(filePath))
        {
            return;
        }
        var info = new FileInfo(filePath);
        if (info.Length < maxBytes)
        {
            return;
        }

        string dir = Path.GetDirectoryName(filePath) ?? LogDirectory;
        string baseName = Path.GetFileNameWithoutExtension(filePath);
        for (int idx = 1; idx <= 999; idx++)
        {
            string candidate = Path.Combine(dir, $"{baseName}_{idx:D3}.log");
            if (!File.Exists(candidate))
            {
                File.Move(filePath, candidate);
                return;
            }
        }
    }

    private static void TryWriteFallbackTempLog(AppLogEntry entry)
    {
        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "PesajeProCamion", "Logs");
            Directory.CreateDirectory(tempDir);
            string tempPath = Path.Combine(tempDir, $"pesaje_fallback_{DateTime.Today:yyyyMMdd}.log");
            File.AppendAllText(tempPath, FormatEntry(entry) + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception fallbackEx) when (fallbackEx is IOException or UnauthorizedAccessException)
        {
            LastWriteWarning = SanitizeText($"Log primary y fallback inaccesibles: {fallbackEx.Message}");
        }
    }

    [GeneratedRegex(@"(?i)(Password|PWD|Clave|Pass_word)\s*=\s*[^;\r\n""']+")]
    private static partial Regex CredentialPairRegex();

    [GeneratedRegex(@"(?i)(Bearer\s+[A-Za-z0-9._\-]+|ghp_[A-Za-z0-9]{10,}|github_pat_[A-Za-z0-9_]{10,})")]
    private static partial Regex TokenRegex();

    private static string ResolveDefaultLogDirectory()
    {
        string baseDir = AppContext.BaseDirectory;
        string portableMarker = Path.Combine(baseDir, "appsettings.json");
        string portableDir = Path.Combine(baseDir, "Logs");
        if (File.Exists(portableMarker) || IsDirectoryWritable(baseDir))
        {
            try
            {
                Directory.CreateDirectory(portableDir);
                return portableDir;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        string programData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PesajeProCamion", "Logs");
        try
        {
            Directory.CreateDirectory(programData);
            return programData;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PesajeProCamion", "Logs");
            Directory.CreateDirectory(localAppData);
            return localAppData;
        }
    }

    private static bool IsDirectoryWritable(string dir)
    {
        try
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (dir.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(programFilesX86) && dir.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
            return Directory.Exists(dir);
        }
        catch (IOException)
        {
            return false;
        }
    }
}
