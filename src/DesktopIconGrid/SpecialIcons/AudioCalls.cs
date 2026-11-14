using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.SpecialIcons;

/// <summary>
/// The microphone's and speakers' calls from a click or the wheel, off the UI thread (review 27 G2): one at a time, with
/// the notches that come meanwhile summed into the next call, and the tile redrawn from what each call returns. A call
/// that has not answered after <see cref="NoAnswerAfter"/> turns the tile to "could not be reached" until it answers.
/// Made and used on the UI thread.
/// </summary>
internal sealed class AudioCalls(Func<AudioReading> toggle, Func<int, AudioReading>? step, Action<AudioReading> show)
{
    /// <summary>How long a call may take before the tile says the device cannot be reached; tests shorten it.</summary>
    internal static TimeSpan NoAnswerAfter { get; set; } = TimeSpan.FromSeconds(10);

    private bool _busy;
    private bool _toggleWaiting;
    private int _notchesWaiting;

    /// <summary>
    /// Raised when a call starts and when the calls are over: a periodic read that overlapped a call, begun before it or
    /// while it ran, may have read the state before the call changed it, and is not shown over the call's result
    /// (<see cref="SystemMonitor.RegisterSlow{T}"/>'s <c>changes</c>).
    /// </summary>
    public int Changes { get; private set; }

    public void Toggle()
    {
        _toggleWaiting = true;
        RunWaiting();
    }

    public void Step(int notches)
    {
        if (step is null || notches == 0)
        {
            return;
        }

        _notchesWaiting += notches;
        RunWaiting();
    }

    private async void RunWaiting()
    {
        if (_busy)
        {
            return; // the running call's end takes what waits
        }

        _busy = true;
        try
        {
            while (_toggleWaiting || _notchesWaiting != 0)
            {
                Func<AudioReading> call;
                if (_toggleWaiting)
                {
                    _toggleWaiting = false;
                    call = toggle;
                }
                else
                {
                    var notches = _notchesWaiting;
                    _notchesWaiting = 0;
                    call = () => step!(notches);
                }

                Changes++;
                var work = Task.Run(call);
                if (await Task.WhenAny(work, Task.Delay(NoAnswerAfter)) != work)
                {
                    AppLog.Default.Warning("Audio", $"A call to the audio device has not answered in {NoAnswerAfter.TotalSeconds:0} s");
                    show(AudioReading.Unreadable); // and it keeps waiting for the answer
                }

                show(await work);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // async void: nothing may escape to the dispatcher. MicrophoneControl catches at the COM boundary already.
            AppLog.Default.Warning("Audio", $"An audio call failed: {ex.Message}");
            show(AudioReading.Unreadable);
        }
        finally
        {
            _busy = false;
            Changes++;
        }
    }
}
