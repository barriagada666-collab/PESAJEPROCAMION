using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace PesajeProCamion.Domain.Diagnostics;

public enum AppLogLevel
{
    Info,
    Warn,
    Error,
    Sql,
    Serial
}

public sealed record AppLogEntry(
    DateTime Timestamp,
    AppLogLevel Level,
    string Source,
    string Message,
    string? Detail);

/// <summary>
/// Sistema centralizado de trazabilidad y logs de errores con buffer en memoria y archivo diario rotativo
/// (compatible con ejecución instalada y modo portable sin dependencias).
/// </summary>
public static class AppLogger
{
    private const int MaxMemoryEntries = 1000;
    private static readonly Lock SyncLock = new();
    private static readonly ConcurrentQueue<AppLogEntry> Entries = new();
    private static string? _logDirectory;

    public static event EventHandler<AppLogEntry>? EntryLogged;
    public static bool IsSilentTestHost { get; set; }

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

    public static void ConfigureDirectory(string directory)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            _logDirectory = directory;
        }
    }

    public static void Info(string source, string message, string? detail = null)
        => Log(AppLogLevel.Info, source, message, detail);

    public static void Warn(string source, string message, string? detail = null)
        => Log(AppLogLevel.Warn, source, message, detail);

    public static void Error(string source, string message, Exception? ex = null)
        => Log(AppLogLevel.Error, source, message, ex?.ToString());

    public static void ErrorDetail(string source, string message, string? detail)
        => Log(AppLogLevel.Error, source, message, detail);

    public static void Sql(string source, string message, string? detail = null)
        => Log(AppLogLevel.Sql, source, message, detail);

    public static void Serial(string source, string message, string? detail = null)
        => Log(AppLogLevel.Serial, source, message, detail);

    public static void Log(AppLogLevel level, string source, string message, string? detail = null)
    {
        var entry = new AppLogEntry(DateTime.Now, level, source, message, detail);
        Entries.Enqueue(entry);
        while (Entries.Count > MaxMemoryEntries)
        {
            Entries.TryDequeue(out _);
        }

        WriteToFile(entry);
        EntryLogged?.Invoke(null, entry);
    }

    public static IReadOnlyList<AppLogEntry> GetEntries()
        => [.. Entries.Reverse()];

    public static void ClearMemory()
    {
        while (Entries.TryDequeue(out _))
        {
        }
    }

    public static void ExportToFile(string filePath, AppLogLevel? minLevel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        IEnumerable<AppLogEntry> items = Entries.ToArray();
        if (minLevel.HasValue)
        {
            items = items.Where(e => e.Level == minLevel.Value);
        }

        var sb = new StringBuilder();
        foreach (AppLogEntry e in items)
        {
            sb.AppendLine(FormatEntry(e));
        }
        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string FormatEntry(AppLogEntry e)
    {
        string baseLine = string.Create(CultureInfo.InvariantCulture,
            $"[{e.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{e.Level,-6}] [{e.Source}] {e.Message}");
        return string.IsNullOrWhiteSpace(e.Detail)
            ? baseLine
            : $"{baseLine}{Environment.NewLine}    {e.Detail.Replace(Environment.NewLine, Environment.NewLine + "    ", StringComparison.Ordinal)}";
    }

    private static void WriteToFile(AppLogEntry entry)
    {
        try
        {
            lock (SyncLock)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(CurrentLogFilePath, FormatEntry(entry) + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Evitar recursión infinita si el sistema de archivos impide escribir en el directorio actual
        }
    }

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
