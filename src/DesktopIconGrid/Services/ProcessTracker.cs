using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Threading;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

/// <summary>
/// Knows which shortcuts currently have a running program: processes started from the bar are
/// followed until they exit, and executables are also matched by process name every few seconds
/// (so a program started elsewhere still shows as running). Also owns the output logs.
/// </summary>
public sealed class ProcessTracker : IDisposable
{
    private const int LogFilesToKeepPerShortcut = 10;
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(4);

    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<Guid, HashSet<int>> _launched = [];
    private readonly Dictionary<Guid, string> _watchedProcessNames = [];
    private readonly Dictionary<Guid, ProcessLogSession> _logs = [];
    private readonly DispatcherTimer _scanTimer;
    private HashSet<string> _runningNames = new(StringComparer.OrdinalIgnoreCase);

    public ProcessTracker(Dispatcher dispatcher, string logDirectory)
    {
        _dispatcher = dispatcher;
        LogDirectory = logDirectory;
        _scanTimer = new DispatcherTimer(ScanInterval, DispatcherPriority.Background, (_, _) => Scan(), dispatcher);
        _scanTimer.Stop();
    }

    public string LogDirectory { get; }

    /// <summary>Raised on the UI thread with the id of a shortcut whose running state may have changed.</summary>
    public event EventHandler<Guid>? RunningChanged;

    public bool IsRunning(Guid id) =>
        (_launched.TryGetValue(id, out var pids) && pids.Count > 0)
        || (_watchedProcessNames.TryGetValue(id, out var name) && _runningNames.Contains(name));

    /// <summary>Starts matching the shortcut's executable by process name (no effect for documents, links and shell folders).</summary>
    public void Watch(ShortcutItem shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        var name = ProcessNameOf(shortcut.Location);
        if (name is null)
        {
            _watchedProcessNames.Remove(shortcut.Id);
        }
        else
        {
            _watchedProcessNames[shortcut.Id] = name;
        }

        UpdateTimer();
    }

    public void Unwatch(Guid id)
    {
        _watchedProcessNames.Remove(id);
        UpdateTimer();
    }

    public void UnwatchAll()
    {
        _watchedProcessNames.Clear();
        UpdateTimer();
    }

    /// <summary>Follows a process started from the shortcut until it exits.</summary>
    public void Track(Guid id, Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        int pid;
        try
        {
            process.EnableRaisingEvents = true;
            pid = process.Id;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            process.Dispose();
            return;
        }

        if (!_launched.TryGetValue(id, out var pids))
        {
            _launched[id] = pids = [];
        }

        pids.Add(pid);
        process.Exited += (_, _) => _dispatcher.BeginInvoke(() =>
        {
            pids.Remove(pid);
            process.Dispose();
            RunningChanged?.Invoke(this, id);
        });

        if (process.HasExited)
        {
            // Exited may already have fired before the handler was attached.
            pids.Remove(pid);
        }

        RunningChanged?.Invoke(this, id);
    }

    /// <summary>Begins writing the process's output to a new log file for the shortcut.</summary>
    public ProcessLogSession StartLog(ShortcutItem shortcut, Process process, ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        Directory.CreateDirectory(LogDirectory);
        var safeName = SafeFileName(shortcut.Name.Length == 0 ? "shortcut" : shortcut.Name);
        TrimOldLogs(safeName);

        var path = Path.Combine(LogDirectory, $"{safeName}_{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var session = new ProcessLogSession(shortcut.Id, shortcut.Name, path, process, startInfo, _dispatcher);
        _logs[shortcut.Id] = session;
        return session;
    }

    public ProcessLogSession? LatestLog(Guid id) => _logs.GetValueOrDefault(id);

    public void Dispose()
    {
        _scanTimer.Stop();
        foreach (var session in _logs.Values)
        {
            session.Dispose();
        }

        _logs.Clear();
    }

    private void UpdateTimer()
    {
        if (_watchedProcessNames.Count == 0)
        {
            _scanTimer.Stop();
            _runningNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        else if (!_scanTimer.IsEnabled)
        {
            _scanTimer.Start();
            Scan();
        }
    }

    private void Scan()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return;
        }

        foreach (var process in processes)
        {
            try
            {
                names.Add(process.ProcessName);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // The process went away while we looked at it.
            }
            finally
            {
                process.Dispose();
            }
        }

        var previous = _runningNames;
        _runningNames = names;
        foreach (var (id, name) in _watchedProcessNames)
        {
            if (previous.Contains(name) != names.Contains(name))
            {
                RunningChanged?.Invoke(this, id);
            }
        }
    }

    /// <summary>The process name (file name without extension) for executables and bare commands; null otherwise.</summary>
    internal static string? ProcessNameOf(string? location)
    {
        var expanded = Environment.ExpandEnvironmentVariables(location?.Trim() ?? string.Empty);
        if (expanded.Length == 0 || LaunchPlanner.IsUri(expanded) || LaunchPlanner.IsSpecial(expanded) || LaunchPlanner.IsShellNamespace(expanded))
        {
            return null;
        }

        var extension = Path.GetExtension(expanded);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".com", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(expanded);
        }

        if (extension.Length == 0 && !Directory.Exists(expanded))
        {
            return Path.GetFileName(expanded);
        }

        return null;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            builder.Append(invalid.Contains(c) ? '_' : c);
        }

        var result = builder.ToString();
        return result.Length > 40 ? result[..40] : result;
    }

    private void TrimOldLogs(string safeName)
    {
        try
        {
            var files = new DirectoryInfo(LogDirectory)
                .GetFiles($"{safeName}_*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(LogFilesToKeepPerShortcut - 1);
            foreach (var file in files)
            {
                file.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Old logs are a convenience; failing to delete them is not worth interrupting a launch.
        }
    }
}

/// <summary>One captured run: writes the output to a log file and keeps the last lines in memory for the viewer.</summary>
public sealed class ProcessLogSession : IDisposable
{
    private const int MaxLinesInMemory = 5000;

    private readonly object _lock = new();
    private readonly List<string> _lines = [];
    private readonly Dispatcher _dispatcher;
    private StreamWriter? _writer;

    public ProcessLogSession(Guid itemId, string itemName, string logPath, Process process, ProcessStartInfo startInfo, Dispatcher dispatcher)
    {
        ItemId = itemId;
        ItemName = itemName;
        LogPath = logPath;
        StartedAt = DateTime.Now;
        _dispatcher = dispatcher;

        try
        {
            _writer = new StreamWriter(logPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _writer = null;
            Append($"[log file could not be created: {ex.Message}]");
        }

        Append($"[{StartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}] {startInfo.FileName} {startInfo.Arguments}".TrimEnd());
        if (!string.IsNullOrEmpty(startInfo.WorkingDirectory))
        {
            Append($"[working directory] {startInfo.WorkingDirectory}");
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Append(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Append("[stderr] " + e.Data);
            }
        };
        process.Exited += (_, _) =>
        {
            int? exitCode = null;
            try
            {
                exitCode = process.ExitCode;
            }
            catch (InvalidOperationException)
            {
            }

            ExitCode = exitCode;
            IsFinished = true;
            Append($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}] exited with code {(exitCode.HasValue ? exitCode.Value.ToString(CultureInfo.InvariantCulture) : "?")}");
            Dispose();
            _dispatcher.BeginInvoke(() => Finished?.Invoke(this, EventArgs.Empty));
        };

        try
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (InvalidOperationException ex)
        {
            Append($"[output could not be read: {ex.Message}]");
        }
    }

    public Guid ItemId { get; }

    public string ItemName { get; }

    public string LogPath { get; }

    public DateTime StartedAt { get; }

    public bool IsFinished { get; private set; }

    public int? ExitCode { get; private set; }

    /// <summary>Raised on the UI thread for every line.</summary>
    public event EventHandler<string>? LineAdded;

    /// <summary>Raised on the UI thread when the process exits.</summary>
    public event EventHandler? Finished;

    public IReadOnlyList<string> Snapshot()
    {
        lock (_lock)
        {
            return _lines.ToArray();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Append(string line)
    {
        lock (_lock)
        {
            _lines.Add(line);
            if (_lines.Count > MaxLinesInMemory)
            {
                _lines.RemoveRange(0, _lines.Count - MaxLinesInMemory);
            }

            try
            {
                _writer?.WriteLine(line);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                _writer = null;
            }
        }

        _dispatcher.BeginInvoke(() => LineAdded?.Invoke(this, line));
    }
}
