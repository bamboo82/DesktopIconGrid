using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;
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
    private bool _scanning;

    public ProcessTracker(Dispatcher dispatcher, string logDirectory)
    {
        _dispatcher = dispatcher;
        LogDirectory = logDirectory;
        _scanTimer = new DispatcherTimer(ScanInterval, DispatcherPriority.Background, (_, _) => _ = ScanAsync(), dispatcher);
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

    /// <summary>
    /// Follows a process started from the shortcut until it exits. With <paramref name="disposeOnExit"/> the
    /// tracker disposes the <see cref="Process"/> afterwards; a log session that still reads its output owns it instead.
    /// </summary>
    public void Track(Guid id, Process process, bool disposeOnExit = true)
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
            if (disposeOnExit)
            {
                process.Dispose();
            }

            return;
        }

        if (!_launched.TryGetValue(id, out var pids))
        {
            _launched[id] = pids = [];
        }

        pids.Add(pid);
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

            AppLog.Default.Debug("Launch", $"Process {pid} exited{(exitCode.HasValue ? $" with code {exitCode.Value}" : string.Empty)}");
            _dispatcher.BeginInvoke(() =>
            {
                pids.Remove(pid);
                if (disposeOnExit)
                {
                    process.Dispose();
                }

                RunningChanged?.Invoke(this, id);
            });
        };

        if (process.HasExited)
        {
            // Exited may already have fired before the handler was attached.
            pids.Remove(pid);
        }

        RunningChanged?.Invoke(this, id);
    }

    /// <summary>Begins writing the process's output to a new log file for the shortcut. The session owns the process.</summary>
    public ProcessLogSession StartLog(ShortcutItem shortcut, Process process, ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        Directory.CreateDirectory(LogDirectory);
        var prefix = shortcut.Id.ToString("N");
        TrimOldLogs(prefix);

        var path = Path.Combine(LogDirectory, $"{prefix}_{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var session = new ProcessLogSession(shortcut.Id, shortcut.Name, path, process, startInfo, _dispatcher);
        if (_logs.Remove(shortcut.Id, out var previous))
        {
            previous.Dispose();
        }

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
            _ = ScanAsync();
        }
    }

    /// <summary>Enumerates processes on a worker thread (it can take tens of milliseconds) and compares on the UI thread.</summary>
    private async Task ScanAsync()
    {
        if (_scanning)
        {
            return;
        }

        _scanning = true;
        try
        {
            var names = await Task.Run(static () =>
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Process[] processes;
                try
                {
                    processes = Process.GetProcesses();
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    return null;
                }

                foreach (var process in processes)
                {
                    try
                    {
                        set.Add(process.ProcessName);
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

                return set;
            });

            if (names is null)
            {
                return;
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
        finally
        {
            _scanning = false;
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

    private void TrimOldLogs(string prefix)
    {
        try
        {
            var files = new DirectoryInfo(LogDirectory)
                .GetFiles($"{prefix}_*.log")
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

/// <summary>
/// One captured run: writes the output to a log file and keeps the last lines in memory for the viewer.
/// Owns the <see cref="Process"/>: it waits for the output streams to drain after exit, then disposes it.
/// </summary>
public sealed class ProcessLogSession : IDisposable
{
    private const int MaxLinesInMemory = 5000;

    private readonly object _lock = new();
    private readonly List<string> _lines = [];
    private readonly Dispatcher _dispatcher;
    private readonly Process _process;
    private StreamWriter? _writer;
    private bool _processDisposed;

    public ProcessLogSession(Guid itemId, string itemName, string logPath, Process process, ProcessStartInfo startInfo, Dispatcher dispatcher)
    {
        ItemId = itemId;
        ItemName = itemName;
        LogPath = logPath;
        StartedAt = DateTime.Now;
        _dispatcher = dispatcher;
        _process = process;

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
        process.Exited += (_, _) => _ = Task.Run(FinishAsync);

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

    /// <summary>Raised on the UI thread when the process exited and its output has been fully read.</summary>
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
            if (!_processDisposed && IsFinished)
            {
                _processDisposed = true;
                _process.Dispose();
            }
        }
    }

    /// <summary>Runs off the UI thread after exit: drains the redirected streams, records the exit code, closes the file.</summary>
    private void FinishAsync()
    {
        int? exitCode = null;
        try
        {
            _process.WaitForExit(); // without a timeout this also waits for the async output readers to reach end of stream
            exitCode = _process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or SystemException)
        {
        }

        ExitCode = exitCode;
        IsFinished = true;
        Append($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}] exited with code {(exitCode.HasValue ? exitCode.Value.ToString(CultureInfo.InvariantCulture) : "?")}");
        Dispose();
        _dispatcher.BeginInvoke(() => Finished?.Invoke(this, EventArgs.Empty));
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
