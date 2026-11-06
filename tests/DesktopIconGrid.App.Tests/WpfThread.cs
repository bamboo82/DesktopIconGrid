using System.Windows;
using System.Windows.Threading;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// An STA thread with a running dispatcher and an <see cref="Application"/> that holds the app's styles, for tests that
/// build real windows from the app's markup: its <c>StaticResource</c> lookups need the styles when the window loads.
/// WPF allows one Application per process, and app code reads <see cref="Application.Current"/> (themes, icons), so
/// the tests that use this thread share it and run in a collection of their own, after all the others
/// (<see cref="WpfWindowsCollection"/>).
/// </summary>
public sealed class WpfThread : IDisposable
{
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;

    public WpfThread()
    {
        var started = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/DesktopIconGrid;component/Resources/Styles.xaml", UriKind.Absolute),
                });
                started.SetResult(Dispatcher.CurrentDispatcher);
            }
            catch (Exception ex)
            {
                started.SetException(ex);
                return;
            }

            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WPF test thread",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _dispatcher = started.Task.GetAwaiter().GetResult();
    }

    /// <summary>Runs <paramref name="action"/> on the WPF thread; what it throws (a failed assertion too) comes back here.</summary>
    public void Invoke(Action action) => _dispatcher.Invoke(action);

    /// <summary>Lets the WPF thread work through what is queued down to <paramref name="priority"/> (layout, Loaded callbacks).</summary>
    public void Settle(DispatcherPriority priority = DispatcherPriority.ApplicationIdle) => _dispatcher.Invoke(() => { }, priority);

    public void Dispose()
    {
        _dispatcher.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(10));
    }
}

/// <summary>Tests that build real windows on the shared <see cref="WpfThread"/>. They run on their own, after the parallel ones.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfWindowsCollection : ICollectionFixture<WpfThread>
{
    public const string Name = "WPF windows";
}
