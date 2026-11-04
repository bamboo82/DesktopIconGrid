using System.Globalization;

namespace DesktopIconGrid.Core.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

public sealed record LogEntry(DateTimeOffset Time, LogLevel Level, string Category, string Message)
{
    public string Format() =>
        $"{Time.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} [{Level,-7}] {Category}: {Message}";
}

/// <summary>
/// The application's event log: what the app did and what happened to it (sign-ins, sync, uploads,
/// downloads, launches, errors). Keeps the newest entries in memory for the log viewer and appends
/// them to a text file that is rotated when it grows too large. Safe to call from any thread.
/// </summary>
public sealed class AppLog
{
    public const int DefaultCapacity = 2000;
    public const long DefaultMaxFileBytes = 2 * 1024 * 1024;

    private readonly object _lock = new();
    private readonly Queue<LogEntry> _entries = new();
    private readonly Func<DateTimeOffset> _clock;
    private string? _filePath;
    private long _maxFileBytes = DefaultMaxFileBytes;

    public AppLog(int capacity = DefaultCapacity, Func<DateTimeOffset>? clock = null)
    {
        Capacity = Math.Max(10, capacity);
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>The log the whole app writes to.</summary>
    public static AppLog Default { get; } = new();

    public int Capacity { get; }

    public string? FilePath => _filePath;

    /// <summary>Raised on the thread that logged the entry.</summary>
    public event EventHandler<LogEntry>? EntryAdded;

    /// <summary>Starts appending entries to <paramref name="path"/>; the file is rotated to <c>*.previous.log</c> past <paramref name="maxFileBytes"/>.</summary>
    public void AttachFile(string path, long maxFileBytes = DefaultMaxFileBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_lock)
        {
            _filePath = Path.GetFullPath(path);
            _maxFileBytes = Math.Max(16 * 1024, maxFileBytes);
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        }
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_lock)
        {
            return _entries.ToArray();
        }
    }

    public void Debug(string category, string message) => Add(LogLevel.Debug, category, message);

    public void Info(string category, string message) => Add(LogLevel.Info, category, message);

    public void Warning(string category, string message) => Add(LogLevel.Warning, category, message);

    public void Error(string category, string message, Exception? exception = null) =>
        Add(LogLevel.Error, category, exception is null ? message : $"{message} — {exception.GetType().Name}: {exception.Message}");

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }

    private void Add(LogLevel level, string category, string message)
    {
        var entry = new LogEntry(_clock(), level, category?.Trim() ?? string.Empty, message ?? string.Empty);
        lock (_lock)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > Capacity)
            {
                _entries.Dequeue();
            }

            WriteToFile(entry);
        }

        EntryAdded?.Invoke(this, entry);
    }

    private void WriteToFile(LogEntry entry)
    {
        if (_filePath is null)
        {
            return;
        }

        try
        {
            var info = new FileInfo(_filePath);
            if (info.Exists && info.Length > _maxFileBytes)
            {
                File.Move(_filePath, Path.ChangeExtension(_filePath, ".previous.log"), overwrite: true);
            }

            File.AppendAllText(_filePath, entry.Format() + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the app down; the in-memory log still has the entry.
        }
    }
}
