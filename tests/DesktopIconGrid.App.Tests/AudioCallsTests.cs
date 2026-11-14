using System.Collections.Concurrent;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// The audio tiles' calls and the slow reads off the UI thread (review 27 G2), on the shared WPF thread so that what
/// they show comes back to one thread, as on the bar.
/// </summary>
[Collection(WpfWindowsCollection.Name)]
public class AudioCallsTests(WpfThread wpf)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly AudioReading Loud = new((false, 80));

    [Fact]
    public void Notches_that_come_while_a_step_runs_are_summed_into_one_next_call()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var gate = new ManualResetEventSlim();
        var steps = new ConcurrentQueue<int>();
        var shown = new ConcurrentQueue<AudioReading>();
        AudioCalls? calls = null;
        wpf.Invoke(() =>
        {
            calls = new AudioCalls(() => Loud, notches =>
            {
                steps.Enqueue(notches);
                gate.Wait();
                return new AudioReading((false, 50 + notches));
            }, shown.Enqueue);
            calls.Step(1); // runs, and waits for the gate
            calls.Step(1);
            calls.Step(-3);
            calls.Step(4); // these three wait, summed: 2
        });

        gate.Set();
        Assert.True(SpinWait.SpinUntil(() => shown.Count == 2, Patience));
        Assert.Equal([1, 2], steps.ToArray());
        Assert.Equal([new AudioReading((false, 51)), new AudioReading((false, 52))], shown.ToArray());
        wpf.Invoke(() => Assert.Equal(3, calls!.Changes)); // two calls started, and the calls are over
    }

    [Fact]
    public void A_call_with_no_answer_shows_the_device_as_unreachable_until_the_answer_comes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var before = AudioCalls.NoAnswerAfter;
        AudioCalls.NoAnswerAfter = TimeSpan.FromMilliseconds(100);
        try
        {
            var gate = new ManualResetEventSlim();
            var shown = new ConcurrentQueue<AudioReading>();
            wpf.Invoke(() => new AudioCalls(() => { gate.Wait(); return Loud; }, step: null, shown.Enqueue).Toggle());

            Assert.True(SpinWait.SpinUntil(() => !shown.IsEmpty, Patience));
            Assert.Equal([AudioReading.Unreadable], shown.ToArray());

            gate.Set();
            Assert.True(SpinWait.SpinUntil(() => shown.Count == 2, Patience));
            Assert.Equal([AudioReading.Unreadable, Loud], shown.ToArray());
        }
        finally
        {
            AudioCalls.NoAnswerAfter = before;
        }
    }

    [Fact]
    public void A_slow_read_shows_no_answer_after_the_wait_and_is_not_shown_over_a_change_made_while_it_ran()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var before = SystemMonitor.NoAnswerAfter;
        SystemMonitor.NoAnswerAfter = TimeSpan.FromMilliseconds(100);
        SystemMonitor? monitor = null;
        GaugeState? state = null;
        var gate = new ManualResetEventSlim();
        var returned = new ManualResetEventSlim();
        var changes = 0;
        string Summary()
        {
            var summary = string.Empty;
            wpf.Invoke(() => summary = state!.Summary);
            return summary;
        }

        try
        {
            wpf.Invoke(() =>
            {
                monitor = new SystemMonitor(System.Windows.Threading.Dispatcher.CurrentDispatcher);
                state = new GaugeState("T", GaugeStyle.Ring, 101, 101, "start");
                monitor.RegisterSlow(
                    state,
                    () =>
                    {
                        gate.Wait();
                        returned.Set();
                        return 42.0;
                    },
                    value => (value, "read"),
                    everyTicks: 1,
                    recordsPercent: false,
                    whenNoAnswer: () => (null, "no answer"),
                    changes: () => changes);
            });

            Assert.True(SpinWait.SpinUntil(() => Summary() == "no answer", Patience));

            wpf.Invoke(() => changes++); // a click's call changed the value while the read was under way
            gate.Set();
            Assert.True(returned.Wait(Patience));
            Thread.Sleep(200); // the read's end is queued on the WPF thread
            wpf.Settle();
            Assert.Equal("no answer", Summary()); // not the older reading

            // The next read, with nothing changed while it ran, is shown (the monitor ticks every 2 s).
            Assert.True(SpinWait.SpinUntil(() => Summary() == "read", Patience));
        }
        finally
        {
            wpf.Invoke(() =>
            {
                state?.Detach();
                monitor?.Dispose();
            });
            SystemMonitor.NoAnswerAfter = before;
        }
    }
}
