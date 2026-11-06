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
/// followed until they exit, and executables are also matched by full image path against the
/// running processes every few seconds (so a program started elsewhere still shows as running,
/// while a same-named program somewhere else does not). Also owns the output logs.
/// </summary>
public sealed class ProcessTracker : IDisposable
{
    private const int LogFilesToKeepPerShortcut = 10;
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(4);

    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<Guid, HashSet<int>> _launched = [];
    private readonly Dictionary<Guid, WatchTarget> _watchedPaths = [];
    private readonly Dictionary<Guid, ProcessLogSession> _logs = [];
    private readonly DispatcherTimer _scanTimer;
    private IReadOnlyDictionary<string, IReadOnlyList<int>> _runningPaths = NothingRunning;
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

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<int>> NothingRunning =
        new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);

    public bool IsRunning(Guid id) =>
        (_launched.TryGetValue(id, out var pids) && pids.Count > 0)
        || (_watchedPaths.TryGetValue(id, out var target) && target.MatchesAny(_runningPaths.Keys));

    /// <summary>
    /// The processes behind a shortcut, for switching to its windows: the ones started from the bar that still run,
    /// plus those the last scan found at its program's path (or in its Store package).
    /// </summary>
    /// <summary>
    /// Only the processes started from this shortcut that still run: what a shortcut with parameters switches to, since
    /// another copy of the same program may have been started with other parameters.
    /// </summary>
    public IReadOnlySet<int> LaunchedProcessIdsOf(Guid id) =>
        _launched.TryGetValue(id, out var launched) ? new HashSet<int>(launched) : [];

    public IReadOnlySet<int> ProcessIdsOf(Guid id)
    {
        var result = new HashSet<int>();
        if (_launched.TryGetValue(id, out var launched))
        {
            result.UnionWith(launched);
        }

        if (_watchedPaths.TryGetValue(id, out var target))
        {
            foreach (var (path, processIds) in _runningPaths)
            {
                if (target.Matches(path))
                {
                    result.UnionWith(processIds);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Starts matching the shortcut's program against the running processes: its executable by full path; for a
    /// Store app (an app-execution alias, a <c>shell:AppsFolder</c> entry, or an inbox program that Windows 11
    /// replaced) its package. No effect for documents, links and other shell locations.
    /// </summary>
    public void Watch(ShortcutItem shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        var target = WatchTargetOf(shortcut.EffectiveLocation);
        if (target is null)
        {
            _watchedPaths.Remove(shortcut.Id);
        }
        else
        {
            _watchedPaths[shortcut.Id] = target;
        }

        UpdateTimer();
    }

    public void Unwatch(Guid id)
    {
        _watchedPaths.Remove(id);
        UpdateTimer();
    }

    public void UnwatchAll()
    {
        _watchedPaths.Clear();
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
        if (_watchedPaths.Count == 0)
        {
            _scanTimer.Stop();
            _runningPaths = NothingRunning;
        }
        else if (!_scanTimer.IsEnabled)
        {
            _scanTimer.Start();
            _ = ScanAsync();
        }
    }

    /// <summary>
    /// Snapshots the running executables on a worker thread (it can take tens of milliseconds) and compares on the
    /// tracker's dispatcher, where the watch list and the badges live. The way back is explicit rather than through
    /// whatever synchronization context the caller had: under a test runner that context is not the dispatcher, and
    /// the snapshot was then applied, and RunningChanged raised, on a worker thread.
    /// </summary>
    private async Task ScanAsync()
    {
        if (_scanning)
        {
            return;
        }

        _scanning = true;
        IReadOnlyDictionary<string, IReadOnlyList<int>>? snapshot = null;
        try
        {
            snapshot = await Task.Run(static () => RunningProcesses.Snapshot()).ConfigureAwait(false);
        }
        finally
        {
            _ = _dispatcher.BeginInvoke(() =>
            {
                _scanning = false;
                if (snapshot is not null)
                {
                    ApplySnapshot(snapshot);
                }
            });
        }
    }

    /// <summary>Takes a new snapshot of the running processes (path → process ids) and reports badges that changed.</summary>
    internal void ApplySnapshot(IReadOnlyDictionary<string, IReadOnlyList<int>> snapshot)
    {
        var previous = _runningPaths;
        _runningPaths = snapshot;
        foreach (var (id, target) in _watchedPaths)
        {
            if (target.MatchesAny(previous.Keys) != target.MatchesAny(snapshot.Keys))
            {
                RunningChanged?.Invoke(this, id);
            }
        }
    }

    /// <summary>
    /// What the running-process scan looks for on behalf of a shortcut: its executable by full path; every
    /// executable in a folder (a Store app's package folder behind an alias, since <c>wt.exe</c> may only start the
    /// real process, <c>WindowsTerminal.exe</c>, from the same folder); or any process of a package family.
    /// </summary>
    internal sealed record WatchTarget(string? File = null, string? Folder = null, string? PackageFamily = null)
    {
        public bool Matches(string path) =>
            (File is not null && string.Equals(path, File, StringComparison.OrdinalIgnoreCase))
            || (Folder is not null && IsUnder(path, Folder))
            || (PackageFamily is not null && PackagedApps.IsInFamily(path, PackageFamily));

        public bool MatchesAny(IEnumerable<string> runningPaths) => runningPaths.Any(Matches);

        private static bool IsUnder(string path, string folder) =>
            path.Length > folder.Length
            && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
            && (path[folder.Length] == System.IO.Path.DirectorySeparatorChar || path[folder.Length] == System.IO.Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Where Windows keeps app-execution aliases (Store apps, winget, Windows Terminal): 0-byte reparse points
    /// whose real process has another image path. Settable for tests.
    /// </summary>
    internal static string AppAliasDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "Microsoft", "WindowsApps");

    /// <summary>
    /// What to watch for a shortcut: its executable, or the package folder behind an app-execution alias;
    /// <see langword="null"/> for documents, links, folders, shell locations, an alias whose reparse data cannot be
    /// read (launched-process tracking still applies) and the Windows shell itself.
    /// </summary>
    internal static WatchTarget? WatchTargetOf(string? location)
    {
        if (PackagedApps.FamilyOfAppsFolderItem(Environment.ExpandEnvironmentVariables(location?.Trim() ?? string.Empty)) is { } family)
        {
            return new WatchTarget(PackageFamily: family);
        }

        var full = FullExecutablePath(location);
        if (full is null)
        {
            return null;
        }

        if (!IsAppAlias(full))
        {
            return new WatchTarget(File: full, PackageFamily: PackagedApps.ReplacementFamily(full, WindowsFolders));
        }

        var target = AppExecutionAlias.ResolveTarget(full);
        var folder = target is null ? null : Path.GetDirectoryName(target);
        return string.IsNullOrEmpty(folder) ? null : new WatchTarget(Folder: folder.TrimEnd(Path.DirectorySeparatorChar));
    }

    /// <summary>Where the inbox programs live: the Windows folder, System32 and SysWOW64.</summary>
    private static readonly string[] WindowsFolders =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
    ];

    /// <summary>
    /// The full path of the executable a shortcut starts, for executables and bare commands; <see langword="null"/>
    /// for documents, links, folders, shell locations, app-execution aliases and the Windows shell itself (which is
    /// always running).
    /// </summary>
    internal static string? ExecutablePathOf(string? location)
    {
        var full = FullExecutablePath(location);
        return full is null || IsAppAlias(full) ? null : full;
    }

    /// <summary>Like <see cref="ExecutablePathOf"/>, but app-execution aliases are returned (their own path).</summary>
    private static string? FullExecutablePath(string? location)
    {
        var expanded = Environment.ExpandEnvironmentVariables(location?.Trim() ?? string.Empty);
        if (expanded.Length == 0 || LaunchPlanner.IsUri(expanded) || LaunchPlanner.IsSpecial(expanded) || LaunchPlanner.IsShellNamespace(expanded))
        {
            return null;
        }

        var extension = Path.GetExtension(expanded);
        var looksExecutable = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                              || extension.Equals(".com", StringComparison.OrdinalIgnoreCase)
                              || (extension.Length == 0 && !Directory.Exists(expanded));
        if (!looksExecutable)
        {
            return null;
        }

        string? full;
        try
        {
            var found = ExecutableLocator.Find(expanded);
            full = found is null ? null : Path.GetFullPath(found);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }

        if (full is null || string.Equals(Path.GetFileName(full), "explorer.exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return full;
    }

    private static bool IsAppAlias(string path)
    {
        var aliases = AppAliasDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return aliases.Length > 0
               && path.Length > aliases.Length
               && path.StartsWith(aliases, StringComparison.OrdinalIgnoreCase)
               && (path[aliases.Length] == Path.DirectorySeparatorChar || path[aliases.Length] == Path.AltDirectorySeparatorChar);
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
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // No process handle any more; the exit code stays unknown.
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
