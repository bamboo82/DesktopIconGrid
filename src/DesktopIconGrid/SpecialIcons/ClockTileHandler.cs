using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Resources;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.SpecialIcons;

/// <summary>
/// The clock tile: the time here or in another zone; a click starts a countdown or a Pomodoro (or stops it), and the
/// end of a phase is a notification. All of it stays on this PC and contacts nothing.
/// </summary>
public sealed class ClockTileHandler(Action<string, string> notify, Func<Window?> ownerProvider, Dispatcher dispatcher) : ISpecialIconHandler
{
    private readonly ConditionalWeakTable<SpecialIconItem, ClockSession> _sessions = [];

    public string Kind => SpecialIconKinds.Clock;

    public string DisplayName => Strings.Clock_Name;

    public string Description => Strings.Clock_Desc;

    public SpecialIconItem CreateItem()
    {
        var item = new SpecialIconItem { Name = Strings.Clock_Name, Kind = Kind };
        ClockTileSettings.Default.ApplyTo(item);
        return item;
    }

    public GaugeState? CreateState(SpecialIconItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var state = new GaugeState(item.Name, GaugeStyle.Number, warningAt: 101, criticalAt: 101, string.Empty);
        _sessions.AddOrUpdate(item, new ClockSession(item.Name, ClockTileSettings.From(item), state, notify, dispatcher));
        return state;
    }

    public LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers)
    {
        if (_sessions.TryGetValue(item, out var session))
        {
            session.Click();
        }

        return LaunchResult.Started;
    }

    public bool ShowProperties(SpecialIconItem item, bool isNew)
    {
        ArgumentNullException.ThrowIfNull(item);
        var viewModel = new ClockTileViewModel(item);
        var window = new ClockTileWindow { DataContext = viewModel, Owner = ownerProvider() };
        if (window.ShowDialog() != true)
        {
            return false;
        }

        viewModel.Apply(item);
        return true;
    }
}

/// <summary>The live part of one clock tile: a one-second timer that draws the time or what is left.</summary>
internal sealed class ClockSession
{
    private readonly string _name;
    private readonly ClockTileSettings _settings;
    private readonly ClockTimer _timer;
    private readonly GaugeState _state;
    private readonly Action<string, string> _notify;
    private readonly DispatcherTimer _tick;

    public ClockSession(string name, ClockTileSettings settings, GaugeState state, Action<string, string> notify, Dispatcher dispatcher)
    {
        _name = name;
        _settings = settings;
        _timer = new ClockTimer(settings);
        _state = state;
        _notify = notify;
        _tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), dispatcher);
        state.Detached += (_, _) => _tick.Stop();
        Draw(DateTimeOffset.Now);
    }

    public void Click()
    {
        _timer.Click(DateTimeOffset.Now);
        Draw(DateTimeOffset.Now);
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        switch (_timer.Tick(now))
        {
            case ClockEvent.CountdownEnded:
                _notify(_name, string.Format(Strings.Clock_CountdownEnded, _settings.Minutes));
                break;
            case ClockEvent.BreakStarted:
                _notify(_name, string.Format(Strings.Clock_BreakStarted, _settings.BreakMinutes));
                break;
            case ClockEvent.WorkStarted:
                _notify(_name, string.Format(Strings.Clock_WorkStarted, _settings.Minutes));
                break;
        }

        Draw(now);
    }

    private void Draw(DateTimeOffset now)
    {
        var label = _timer.Label(now);
        var level = _timer.Phase switch
        {
            ClockPhase.Running => CommandLevel.Ok,
            ClockPhase.Break => CommandLevel.Warning,
            ClockPhase.Done => CommandLevel.Failed,
            _ => CommandLevel.Unknown,
        };
        if (_state.LabelText == label && _state.Level == level)
        {
            return; // the minute has not turned: nothing to redraw
        }

        var detail = _timer.Phase switch
        {
            ClockPhase.Running => Strings.Clock_Running,
            ClockPhase.Break => Strings.Clock_OnBreak,
            ClockPhase.Done => Strings.Clock_Done,
            _ => _settings.Timer == ClockTimerKind.None
                ? _settings.Zone().DisplayName
                : $"{_settings.Zone().DisplayName}\n{Strings.Clock_ClickToStart}",
        };
        _state.LabelText = label;
        _state.Level = level;
        _state.Update(null, detail);
    }
}
