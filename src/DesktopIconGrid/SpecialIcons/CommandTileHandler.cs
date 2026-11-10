using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.SpecialIcons;

/// <summary>
/// The command tile: runs a program on a timer or a click and shows its output. It runs only once its command is
/// approved on this PC (<see cref="CommandTrust"/>); until then it shows a pause sign, and a click asks.
/// </summary>
public sealed class CommandTileHandler(CommandTrust trust, IDialogService dialogs, Func<Window?> ownerProvider, Dispatcher dispatcher) : ISpecialIconHandler
{
    private readonly ConditionalWeakTable<SpecialIconItem, CommandTileSession> _sessions = [];

    public string Kind => SpecialIconKinds.CommandTile;

    public string DisplayName => Strings.Command_Name;

    public string Description => Strings.Command_Desc;

    public SpecialIconItem CreateItem()
    {
        var item = new SpecialIconItem { Name = Strings.Command_DefaultName, Kind = Kind };
        CommandTileSettings.Default.ApplyTo(item);
        return item;
    }

    public GaugeState? CreateState(SpecialIconItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var settings = CommandTileSettings.From(item);
        var style = settings.Display switch
        {
            CommandTileDisplay.Ring => GaugeStyle.Ring,
            CommandTileDisplay.Bar => GaugeStyle.Bar,
            _ => GaugeStyle.Number,
        };
        var state = new GaugeState(item.Name, style, settings.WarningAt, settings.CriticalAt, Strings.Command_NotRunYet);
        var session = new CommandTileSession(item.Name, settings, state, trust, dispatcher);
        _sessions.AddOrUpdate(item, session);
        session.Start();
        return state;
    }

    public LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(item);
        var settings = CommandTileSettings.From(item);
        if (settings.Problem() is not null)
        {
            return LaunchResult.Failure(Strings.Command_NotSet);
        }

        if (!trust.IsApproved(settings.Hash()))
        {
            var question = string.Format(
                Strings.Command_ApproveQuestion,
                settings.Program,
                string.IsNullOrWhiteSpace(settings.Arguments) ? Strings.Command_None : settings.Arguments,
                string.IsNullOrWhiteSpace(settings.StartIn) ? Strings.Command_None : settings.StartIn);
            if (!dialogs.Confirm(Strings.Command_ApproveTitle, question))
            {
                return LaunchResult.Cancelled;
            }

            AppLog.Default.Info("Commands", $"Command tile \"{item.Name}\" approved on this PC");
            trust.Approve(settings.Hash()); // the sessions of every tile with this command start (CommandTrust.Changed)
            return LaunchResult.Started;
        }

        if (settings.Click == CommandTileClick.Open)
        {
            var result = ProcessLauncher.Start(new System.Diagnostics.ProcessStartInfo(Environment.ExpandEnvironmentVariables(settings.OpenTarget!.Trim())) { UseShellExecute = true });
            result.Process?.Dispose();
            return result;
        }

        return RunNow(item);
    }

    public IReadOnlyList<SpecialIconAction> GetActions(SpecialIconItem item)
    {
        var settings = CommandTileSettings.From(item);
        var runnable = settings.Problem() is null && trust.IsApproved(settings.Hash());
        return [new SpecialIconAction(Strings.Command_RunNow, () => RunNow(item), runnable)];
    }

    public bool ShowProperties(SpecialIconItem item, bool isNew)
    {
        ArgumentNullException.ThrowIfNull(item);
        var viewModel = new CommandTileViewModel(item);
        var window = new CommandTileWindow { DataContext = viewModel, Owner = ownerProvider() };
        if (window.ShowDialog() != true)
        {
            return false;
        }

        viewModel.Apply(item);
        var settings = CommandTileSettings.From(item);
        if (settings.Problem() is null)
        {
            trust.Approve(settings.Hash()); // typed (or at least seen and saved) on this PC
        }

        return true;
    }

    private LaunchResult RunNow(SpecialIconItem item)
    {
        if (!_sessions.TryGetValue(item, out var session))
        {
            return LaunchResult.Failure(Strings.Command_NotRunYet);
        }

        session.RunNow();
        return LaunchResult.Started;
    }
}

/// <summary>The live part of one command tile on the bar: its timer, the run under way, and what the tile shows.</summary>
internal sealed class CommandTileSession
{
    private readonly string _name;
    private readonly CommandTileSettings _settings;
    private readonly GaugeState _state;
    private readonly CommandTrust _trust;
    private readonly DispatcherTimer? _timer;
    private readonly CancellationTokenSource _stop = new();
    private bool _running;
    private bool _started;
    private CommandLevel? _lastLevel;

    public CommandTileSession(string name, CommandTileSettings settings, GaugeState state, CommandTrust trust, Dispatcher dispatcher)
    {
        _name = name;
        _settings = settings;
        _state = state;
        _trust = trust;
        if (!settings.RunsOnClickOnly)
        {
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(settings.IntervalSeconds), DispatcherPriority.Background, (_, _) => RunNow(), dispatcher);
            _timer.Stop();
        }

        state.Detached += (_, _) => Stop();
        trust.Changed += OnTrustChanged;
    }

    /// <summary>Runs now and starts the timer when the command is approved here; shows why not otherwise.</summary>
    public void Start()
    {
        if (_settings.Problem() is not null)
        {
            Show("?", CommandLevel.Unknown, Strings.Command_NotSet);
            return;
        }

        if (!_trust.IsApproved(_settings.Hash()))
        {
            Show("⏸", CommandLevel.Unknown, Strings.Command_Paused);
            return;
        }

        _started = true;
        RunNow();
        _timer?.Start();
    }

    public async void RunNow()
    {
        if (_running || _state.IsDetached || !_started)
        {
            return;
        }

        _running = true;
        try
        {
            if (_state.Detail == Strings.Command_NotRunYet)
            {
                Show("…", CommandLevel.Unknown, Strings.Command_Running);
            }

            var run = await CommandTileRunner.RunAsync(_settings, _stop.Token);
            if (_state.IsDetached)
            {
                return;
            }

            Apply(run);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // async void: nothing may escape to the dispatcher.
            AppLog.Default.Error("Commands", $"Command tile \"{_name}\" failed: {ex.Message}");
            Show("!", CommandLevel.Failed, ex.Message);
        }
        finally
        {
            _running = false;
        }
    }

    private void Apply(CommandRun run)
    {
        var commandLine = string.IsNullOrWhiteSpace(_settings.Arguments) ? _settings.Program : $"{_settings.Program} {_settings.Arguments}";
        var at = DateTime.Now.ToString("T", CultureInfo.CurrentCulture);
        if (run.StartError is { } error)
        {
            Show("!", CommandLevel.Failed, $"{commandLine}\n{string.Format(Strings.Command_CouldNotStart, error)}");
            Log(CommandLevel.Failed, $"could not start: {error}");
            return;
        }

        var reading = CommandTileOutput.Read(_settings, run.Output, run.Errors, run.ExitCode);
        var status = run.ExitCode is { } code
            ? string.Format(Strings.Command_Ran, code, at, run.Duration.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture))
            : string.Format(Strings.Command_TimedOut, _settings.TimeoutSeconds, at);
        var detail = reading.Output.Length > 0 ? $"{commandLine}\n{status}\n\n{reading.Output}" : $"{commandLine}\n{status}";
        _state.LabelText = reading.Label;
        _state.Level = reading.Level;
        _state.Update(reading.Percent, detail);
        Log(reading.Level, run.ExitCode is { } exit ? $"exit code {exit}" : $"stopped after {_settings.TimeoutSeconds} s");
    }

    private void Show(string label, CommandLevel level, string detail)
    {
        _state.LabelText = label;
        _state.Level = level;
        _state.Update(null, detail);
    }

    /// <summary>Logs when the colour changes, not every run.</summary>
    private void Log(CommandLevel level, string what)
    {
        if (_lastLevel == level)
        {
            return;
        }

        var first = _lastLevel is null;
        _lastLevel = level;
        if (first && level == CommandLevel.Ok)
        {
            return;
        }

        var message = $"Command tile \"{_name}\": {what}";
        if (level == CommandLevel.Ok)
        {
            AppLog.Default.Info("Commands", message);
        }
        else
        {
            AppLog.Default.Warning("Commands", message);
        }
    }

    private void OnTrustChanged(object? sender, EventArgs e)
    {
        if (!_started && !_state.IsDetached)
        {
            Start();
        }
    }

    private void Stop()
    {
        _timer?.Stop();
        _trust.Changed -= OnTrustChanged;
        _stop.Cancel();
    }
}
