using System.Threading;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;

namespace DesktopIconGrid.Services;

/// <summary>
/// Runs shell icon lookups one at a time on a dedicated STA thread that pumps messages (a WPF
/// <see cref="Dispatcher"/>), so namespace extensions that marshal back to the calling apartment during
/// <c>IShellItemImageFactory.GetImage</c> complete instead of deadlocking. One slow or missing drive then
/// delays only the icons behind it in this queue, not the thread pool the sync upload and the process scan use.
/// </summary>
public sealed class IconWorker : IDisposable
{
    /// <summary>A lookup that takes longer than this leaves its placeholder; the queue carries on when it returns.</summary>
    public static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(20);

    private static readonly Lazy<IconWorker> SharedInstance = new(() => new IconWorker());

    private readonly Thread _thread;
    private readonly TaskCompletionSource<Dispatcher> _dispatcher = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _pending;

    public IconWorker()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Icon lookups" };
        if (OperatingSystem.IsWindows())
        {
            _thread.SetApartmentState(ApartmentState.STA);
        }

        _thread.Start();
    }

    /// <summary>The app-wide worker.</summary>
    public static IconWorker Shared => SharedInstance.Value;

    /// <summary>Number of lookups queued or running (for diagnostics).</summary>
    public int Pending => Volatile.Read(ref _pending);

    /// <summary>Queues <paramref name="work"/> and completes with its result; faults when the work throws, cancels when the worker was disposed.</summary>
    public async Task<T> RunAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var dispatcher = await _dispatcher.Task.ConfigureAwait(false);
        Interlocked.Increment(ref _pending);
        try
        {
            return await dispatcher.InvokeAsync(work).Task.ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _pending);
        }
    }

    /// <summary>
    /// Like <see cref="RunAsync{T}(Func{T})"/>, but gives up after <paramref name="timeout"/> and returns
    /// <see langword="default"/>: a hung namespace extension cannot be interrupted, so the lookup keeps running on
    /// the worker and its late result is discarded.
    /// </summary>
    public async Task<T?> RunAsync<T>(Func<T> work, TimeSpan timeout, string? label = null)
    {
        var lookup = RunAsync(work);
        var finished = await Task.WhenAny(lookup, Task.Delay(timeout)).ConfigureAwait(false);
        if (finished != lookup)
        {
            AppLog.Default.Warning("Icons", $"Icon lookup{(label is null ? string.Empty : $" for \"{label}\"")} did not finish within {timeout.TotalSeconds:0} s; showing a placeholder");
            _ = lookup.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); // observe a late failure
            return default;
        }

        return await lookup.ConfigureAwait(false);
    }

    private void Run()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        _dispatcher.SetResult(dispatcher);
        Dispatcher.Run(); // pumps messages until BeginInvokeShutdown
    }

    public void Dispose()
    {
        if (_dispatcher.Task.IsCompletedSuccessfully)
        {
            _dispatcher.Task.Result.BeginInvokeShutdown(DispatcherPriority.Normal);
        }
    }
}
