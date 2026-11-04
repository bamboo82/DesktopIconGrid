using System.Collections.Concurrent;
using System.Threading;

namespace DesktopIconGrid.Services;

/// <summary>
/// Runs shell icon lookups one at a time on a dedicated STA thread. One slow or missing drive then delays
/// only the icons behind it in this queue instead of tying up thread-pool threads the sync upload and the
/// process scan need, and shell namespace extensions (cloud drives, archivers) get the apartment they expect.
/// </summary>
public sealed class IconWorker : IDisposable
{
    private static readonly Lazy<IconWorker> SharedInstance = new(() => new IconWorker());

    private readonly BlockingCollection<Action> _queue = [];
    private readonly Thread _thread;

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

    /// <summary>Number of lookups waiting (for diagnostics).</summary>
    public int Pending => _queue.Count;

    /// <summary>Queues <paramref name="work"/> and completes when it ran; the task faults when the work throws.</summary>
    public Task<T> RunAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() =>
            {
                try
                {
                    completion.SetResult(work());
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });
        }
        catch (InvalidOperationException)
        {
            completion.SetCanceled(); // disposed
        }

        return completion.Task;
    }

    private void Run()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }

    public void Dispose() => _queue.CompleteAdding();
}
