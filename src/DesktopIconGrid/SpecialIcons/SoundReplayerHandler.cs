using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.SpecialIcons;

/// <summary>
/// Press and hold the icon to record from the microphone (only while stopped); release to play the
/// recording back in a loop; a single click stops it. A click while stopped replays the last recording.
/// </summary>
public sealed class SoundReplayerHandler : ISpecialIconHandler
{
    private enum Mode
    {
        Stopped,
        Recording,
        Playing,
    }

    private sealed class ReplayerState
    {
        public Mode Mode;
        public GaugeState? Gauge;
        public SoundPlayer? Player;
        public MciRecorder? Recorder;
        public DateTime StartedAt;
        public DispatcherTimer? Ticker;
    }

    private readonly Func<Window?> _owner;
    private readonly string _soundsDirectory;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<Guid, ReplayerState> _states = [];

    public SoundReplayerHandler(Func<Window?> owner, string soundsDirectory, Dispatcher dispatcher)
    {
        _owner = owner;
        _soundsDirectory = soundsDirectory;
        _dispatcher = dispatcher;
    }

    public string Kind => SpecialIconKinds.SoundReplayer;

    public string DisplayName => Strings.Sound_Name;

    public string Description => Strings.Sound_Desc;

    public SpecialIconItem CreateItem() => new() { Name = Strings.Sound_ItemName, Kind = Kind };

    public GaugeState CreateState(SpecialIconItem item)
    {
        var state = State(item);
        var gauge = new GaugeState("REC", GaugeStyle.Number, warningAt: 50, criticalAt: 90, string.Empty) { IsVisible = state.Mode != Mode.Stopped };
        state.Gauge = gauge;
        ShowMode(state);
        return gauge;
    }

    public ImageSource? GetIcon(SpecialIconItem item) =>
        Application.Current?.TryFindResource("SoundReplayerIconImage") as ImageSource;

    public string? GetToolTip(SpecialIconItem item)
    {
        var path = PathFor(item);
        var recording = File.Exists(path) ? string.Format(Strings.Sound_RecordingOf, DescribeLength(path)) : Strings.Sound_NoRecording;
        return $"{recording}\n{Strings.Sound_ToolTipHint}";
    }

    public bool SupportsPressAndHold(SpecialIconItem item) => true;

    public bool HoldStarted(SpecialIconItem item)
    {
        var state = State(item);
        if (state.Mode != Mode.Stopped)
        {
            return false;
        }

        var recorder = new MciRecorder();
        try
        {
            recorder.Start();
        }
        catch (InvalidOperationException ex)
        {
            recorder.Dispose();
            // Report after the mouse interaction has finished; a modal here would steal the capture mid-press.
            var message = ex.Message;
            _dispatcher.BeginInvoke(() => MessageBox.Show(_owner(), message, DisplayName, MessageBoxButton.OK, MessageBoxImage.Warning));
            return false;
        }

        state.Recorder = recorder;
        state.Mode = Mode.Recording;
        state.StartedAt = DateTime.Now;
        ShowMode(state);
        StartTicker(state);
        return true;
    }

    public void HoldEnded(SpecialIconItem item)
    {
        var state = State(item);
        if (state.Mode != Mode.Recording || state.Recorder is null)
        {
            return;
        }

        StopTicker(state);
        var path = PathFor(item);
        try
        {
            Directory.CreateDirectory(_soundsDirectory);
            state.Recorder.StopAndSave(path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            state.Recorder.Dispose();
            state.Recorder = null;
            state.Mode = Mode.Stopped;
            ShowMode(state);
            MessageBox.Show(_owner(), ex.Message, DisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        state.Recorder.Dispose();
        state.Recorder = null;
        state.Mode = Mode.Stopped;
        Play(state, path);
    }

    public LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers)
    {
        var state = State(item);
        switch (state.Mode)
        {
            case Mode.Playing:
                Stop(state);
                return LaunchResult.Started;
            case Mode.Recording:
                return LaunchResult.Started;
            default:
                var path = PathFor(item);
                if (!File.Exists(path))
                {
                    return LaunchResult.Failure(Strings.Sound_NothingRecorded);
                }

                Play(state, path);
                return LaunchResult.Started;
        }
    }

    public bool ShowProperties(SpecialIconItem item, bool isNew)
    {
        var name = InputWindow.Show(_owner(), Strings.Sound_Name, Strings.Sound_NamePrompt, item.Name, Strings.Sound_NameHint);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        item.Name = name.Trim();
        return true;
    }

    public IReadOnlyList<SpecialIconAction> GetActions(SpecialIconItem item)
    {
        var state = State(item);
        var path = PathFor(item);
        var exists = File.Exists(path);
        return
        [
            new SpecialIconAction(Strings.Common_Stop, () => Stop(state), state.Mode == Mode.Playing),
            new SpecialIconAction(Strings.Sound_Play, () => Play(state, path), state.Mode == Mode.Stopped && exists),
            new SpecialIconAction(Strings.Sound_Delete, () => DeleteRecording(state, path), state.Mode == Mode.Stopped && exists),
            new SpecialIconAction(Strings.Sound_OpenFolder, () => OpenFolder(), Directory.Exists(_soundsDirectory)),
        ];
    }

    private ReplayerState State(SpecialIconItem item)
    {
        if (!_states.TryGetValue(item.Id, out var state))
        {
            state = new ReplayerState();
            _states[item.Id] = state;
        }

        return state;
    }

    private string PathFor(SpecialIconItem item) => Path.Combine(_soundsDirectory, $"{item.Id:N}.wav");

    private void Play(ReplayerState state, string path)
    {
        Stop(state);
        try
        {
            state.Player = new SoundPlayer(path);
            state.Player.Load();
            state.Player.PlayLooping();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            state.Player?.Dispose();
            state.Player = null;
            MessageBox.Show(_owner(), ex.Message, DisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        state.Mode = Mode.Playing;
        state.StartedAt = DateTime.Now;
        ShowMode(state, DescribeLength(path));
        StartTicker(state);
    }

    private void Stop(ReplayerState state)
    {
        StopTicker(state);
        if (state.Player is not null)
        {
            state.Player.Stop();
            state.Player.Dispose();
            state.Player = null;
        }

        if (state.Recorder is not null)
        {
            state.Recorder.Cancel();
            state.Recorder.Dispose();
            state.Recorder = null;
        }

        state.Mode = Mode.Stopped;
        ShowMode(state);
    }

    private void DeleteRecording(ReplayerState state, string path)
    {
        Stop(state);
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(_owner(), ex.Message, DisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowMode(ReplayerState state, string? length = null)
    {
        if (state.Gauge is null)
        {
            return;
        }

        switch (state.Mode)
        {
            case Mode.Recording:
                state.Gauge.LabelText = "REC";
                state.Gauge.IsVisible = true;
                state.Gauge.Update(95, Strings.Sound_RecordingRelease);
                break;
            case Mode.Playing:
                state.Gauge.LabelText = "LOOP";
                state.Gauge.IsVisible = true;
                state.Gauge.Update(10, length is null ? Strings.Sound_Looping : string.Format(Strings.Sound_LoopingFor, length));
                break;
            default:
                state.Gauge.LabelText = null;
                state.Gauge.IsVisible = false;
                state.Gauge.Update(null, string.Empty);
                break;
        }
    }

    private void StartTicker(ReplayerState state)
    {
        StopTicker(state);
        state.Ticker = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            if (state.Gauge is null)
            {
                return;
            }

            var elapsed = DateTime.Now - state.StartedAt;
            var seconds = elapsed.TotalSeconds.ToString("0", CultureInfo.CurrentCulture);
            var text = state.Mode == Mode.Recording
                ? string.Format(Strings.Sound_RecordingElapsed, seconds)
                : string.Format(Strings.Sound_LoopingElapsed, seconds);
            state.Gauge.Update(state.Mode == Mode.Recording ? 95 : 10, text);
        }, _dispatcher);
    }

    private static void StopTicker(ReplayerState state)
    {
        state.Ticker?.Stop();
        state.Ticker = null;
    }

    private void OpenFolder()
    {
        var result = ProcessLauncher.Start(new ProcessStartInfo(_soundsDirectory) { UseShellExecute = true });
        result.Process?.Dispose();
    }

    /// <summary>Length of a 16-bit mono 22.05 kHz WAV, e.g. "3.2 s".</summary>
    private static string DescribeLength(string path)
    {
        try
        {
            var bytes = new FileInfo(path).Length - 44;
            return string.Format(Strings.Sound_Seconds, bytes <= 0 ? "0" : (bytes / 44100.0).ToString("0.0", CultureInfo.CurrentCulture));
        }
        catch (IOException)
        {
            return "?";
        }
    }
}
