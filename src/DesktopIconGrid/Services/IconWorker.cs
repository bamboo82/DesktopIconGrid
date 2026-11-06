using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;

namespace DesktopIconGrid.Services;

/// <summary>
/// Runs shell icon lookups one at a time on a dedicated STA thread that pumps messages (a WPF
/// <see cref="Dispatcher"/>), so namespace extensions that marshal back to the calling apartment during
/// <c>IShellItemImageFactory.GetImage</c> complete instead of deadlocking. Lookups on local fixed drives go
/// before lookups on network or removable storage, so a sleeping NAS never delays the local icons queued after
/// it. A lookup that overruns its deadline retires the shared worker: a fresh one takes over the waiting lookups
/// (including any that arrive on the old worker afterwards) and the stuck thread is left alone until the
/// process exits.
/// </summary>
public sealed class IconWorker : IDisposable
{
    /// <summary>A lookup that takes longer than this leaves its placeholder and retires the shared worker.</summary>
    public static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(20);

    private static readonly object SharedLock = new();
    private static volatile IconWorker? _shared;

    private readonly ConcurrentQueue<WorkItem> _local = new();
    private readonly ConcurrentQueue<WorkItem> _slow = new();
    private readonly TaskCompletionSource<Dispatcher> _dispatcher = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private int _running;
    private volatile bool _retired;
    private volatile bool _disposed;

    public IconWorker()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Icon lookups" };
        if (OperatingSystem.IsWindows())
        {
            _thread.SetApartmentState(ApartmentState.STA);
        }

        _thread.Start();
    }

    /// <summary>The app-wide worker; replaced by a fresh one when a lookup hangs.</summary>
    public static IconWorker Shared
    {
        get
        {
            if (_shared is { } current)
            {
                return current;
            }

            lock (SharedLock)
            {
                return _shared ??= new IconWorker();
            }
        }
    }

    /// <summary>Number of lookups waiting or running (for diagnostics).</summary>
    public int Pending => _local.Count + _slow.Count + Volatile.Read(ref _running);

    /// <summary>True once a lookup on this worker hung and a successor took over; anything queued here is forwarded.</summary>
    public bool IsRetired => _retired;

    /// <summary>
    /// True for paths on network shares and on drives that are not fixed disks (removable, optical, mapped network
    /// letters); their lookups queue behind the local ones, and some callers count them as there without a look. A
    /// letter with no drive behind it is not slow: nothing is there, and a look says so at once. Does no I/O.
    /// </summary>
    public static bool IsOnSlowStorage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        return expanded.StartsWith(@"\\", StringComparison.Ordinal)
            || DriveTypeOf(expanded) is { } type && type is not (DriveType.Fixed or DriveType.Ram or DriveType.NoRootDirectory);
    }

    /// <summary>
    /// The type of the drive whose letter starts <paramref name="path"/>, from GetDriveType, which does no I/O
    /// (<see cref="DriveType.NoRootDirectory"/> for a letter with no drive behind it); <see langword="null"/> for a
    /// UNC path, a path without a drive letter, or off Windows.
    /// </summary>
    public static DriveType? DriveTypeOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (!OperatingSystem.IsWindows() || expanded.Length < 2 || expanded[1] != ':' || !char.IsAsciiLetter(expanded[0]))
        {
            return null;
        }

        try
        {
            return new DriveInfo(expanded[..1]).DriveType;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Queues <paramref name="work"/> and completes with its result; faults when the work throws, cancels when the worker was disposed.</summary>
    public Task<T> RunAsync<T>(Func<T> work, bool slow = false)
    {
        ArgumentNullException.ThrowIfNull(work);
        var item = new WorkItem(() => work(), new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously));
        (slow ? _slow : _local).Enqueue(item);
        if (_retired || _disposed)
        {
            // Shared was read before this worker was retired: the item belongs to the successor now.
            ForwardQueued();
        }
        else
        {
            _ = PostAsync();
        }

        return UnwrapAsync<T>(item.Completion.Task);
    }

    /// <summary>
    /// Like <see cref="RunAsync{T}(Func{T}, bool)"/>, but gives up after <paramref name="timeout"/> and returns
    /// <see langword="default"/>. A hung namespace extension cannot be interrupted, so when this is the shared
    /// worker it is retired and a fresh one takes over the lookups still waiting; the late result is discarded.
    /// </summary>
    public async Task<T?> RunAsync<T>(Func<T> work, TimeSpan timeout, string? label = null, bool slow = false)
    {
        var lookup = RunAsync(work, slow);
        var finished = await Task.WhenAny(lookup, Task.Delay(timeout)).ConfigureAwait(false);
        if (finished != lookup)
        {
            AppLog.Default.Warning("Icons", $"Icon lookup{(label is null ? string.Empty : $" for \"{label}\"")} did not finish within {timeout.TotalSeconds:0} s; showing a placeholder");
            _ = lookup.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); // observe a late failure
            Retire(label);
            return default;
        }

        return await lookup.ConfigureAwait(false);
    }

    private static async Task<T> UnwrapAsync<T>(Task<object?> task) => (T)(await task.ConfigureAwait(false))!;

    /// <summary>Asks the dispatcher to run one waiting lookup. A dispatcher that has shut down forwards the queue instead of faulting.</summary>
    private async Task PostAsync()
    {
        try
        {
            var dispatcher = await _dispatcher.Task.ConfigureAwait(false);
            await dispatcher.InvokeAsync(ProcessNext);
        }
        catch (OperationCanceledException)
        {
            // The dispatcher shut down (worker retired and idle, or disposed); nothing was lost, the queue moves on.
            ForwardQueued();
        }
    }

    /// <summary>Runs one waiting lookup, local ones first. Posted once per queued item.</summary>
    private void ProcessNext()
    {
        if (_retired)
        {
            ForwardQueued();
            ShutdownWhenIdle();
            return;
        }

        if (!_local.TryDequeue(out var item) && !_slow.TryDequeue(out item))
        {
            return; // handed over to a successor
        }

        Interlocked.Exchange(ref _running, 1);
        try
        {
            item.Completion.TrySetResult(item.Work());
        }
        catch (Exception ex)
        {
            item.Completion.TrySetException(ex);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }

        if (_retired)
        {
            ForwardQueued();
            ShutdownWhenIdle(); // the hung lookup came back after a successor took over
        }
    }

    /// <summary>Marks the shared worker dead, hands its waiting lookups to a fresh worker and makes that the shared one.</summary>
    private void Retire(string? label)
    {
        lock (SharedLock)
        {
            if (_retired || !ReferenceEquals(_shared, this))
            {
                return; // only the shared worker is replaced; a private one just reports the timeout
            }

            _retired = true;
            var successor = new IconWorker();
            _shared = successor;
            var moved = MoveQueuedTo(successor);
            AppLog.Default.Warning("Icons", $"Icon worker retired because the lookup{(label is null ? string.Empty : $" for \"{label}\"")} is stuck; a new worker took over {moved} waiting lookup(s)");
        }
    }

    /// <summary>
    /// Moves whatever is still queued here to the shared worker and posts it there. Called whenever this worker
    /// can no longer run anything: after retirement (lookups can still arrive from callers that read
    /// <see cref="Shared"/> just before) and after its dispatcher shut down. A disposed private worker cancels
    /// its queue instead.
    /// </summary>
    private void ForwardQueued()
    {
        if (_disposed)
        {
            FailQueued();
            return;
        }

        IconWorker successor;
        lock (SharedLock)
        {
            // Retire swaps the shared worker and drains under this lock, so once we hold it either the successor is
            // in place or no retirement happened (then the queue belongs here and the dispatcher will get to it).
            if (_shared is not { } current || ReferenceEquals(current, this))
            {
                return;
            }

            successor = current;
        }

        MoveQueuedTo(successor);
    }

    private int MoveQueuedTo(IconWorker successor)
    {
        var moved = 0;
        while (_local.TryDequeue(out var item))
        {
            successor._local.Enqueue(item);
            moved++;
        }

        while (_slow.TryDequeue(out var item))
        {
            successor._slow.Enqueue(item);
            moved++;
        }

        for (var i = 0; i < moved; i++)
        {
            _ = successor.PostAsync();
        }

        return moved;
    }

    private void FailQueued()
    {
        while (_local.TryDequeue(out var item))
        {
            item.Completion.TrySetCanceled();
        }

        while (_slow.TryDequeue(out var item))
        {
            item.Completion.TrySetCanceled();
        }
    }

    private static void ShutdownWhenIdle() => Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);

    private void Run()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        _dispatcher.SetResult(dispatcher);
        Dispatcher.Run(); // pumps messages until BeginInvokeShutdown
    }

    public void Dispose()
    {
        _disposed = true;
        if (_dispatcher.Task.IsCompletedSuccessfully)
        {
            _dispatcher.Task.Result.BeginInvokeShutdown(DispatcherPriority.Normal);
        }

        FailQueued();
    }

    private sealed record WorkItem(Func<object?> Work, TaskCompletionSource<object?> Completion);
}
