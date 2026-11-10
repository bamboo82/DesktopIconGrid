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
/// approved on this PC (<see cref="CommandTrust"/>); until then it shows a pause sign, and a click asks in a window of
/// its own (<see cref="CommandApprovalWindow"/>) that shows everything the approval covers.
/// </summary>
public sealed class CommandTileHandler : ISpecialIconHandler
{
    private readonly CommandTrust _trust;
    private readonly Func<Window?> _ownerProvider;
    private readonly Dispatcher _dispatcher;
    private readonly Func<string, CommandTileSettings, bool> _askApproval;
    private readonly ConditionalWeakTable<SpecialIconItem, CommandTileSession> _sessions = [];
    private string? _savedHere;

    /// <param name="askApproval">Asks whether the tile (by name) may run as shown; the approval window when not given (tests pass their own).</param>
    public CommandTileHandler(CommandTrust trust, Func<Window?> ownerProvider, Dispatcher dispatcher, Func<string, CommandTileSettings, bool>? askApproval = null)
    {
        _trust = trust;
        _ownerProvider = ownerProvider;
        _dispatcher = dispatcher;
        _askApproval = askApproval ?? AskInWindow;
    }

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
        if (_savedHere is { } saved && saved == settings.Hash() && settings.Problem() is null)
        {
            // Saved in Properties on this PC (ShowProperties): approved now, after the edit reached the bar and the tile's
            // old session was stopped, so the command does not start in the old session first.
            _savedHere = null;
            Approve(item.Name, settings);
        }

        var style = settings.Display switch
        {
            CommandTileDisplay.Ring => GaugeStyle.Ring,
            CommandTileDisplay.Bar => GaugeStyle.Bar,
            _ => GaugeStyle.Number,
        };
        var state = new GaugeState(item.Name, style, settings.WarningAt, settings.CriticalAt, Strings.Command_NotRunYet);
        var session = new CommandTileSession(item.Name, settings, state, _trust, _dispatcher);
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
            return LaunchResult.Failure(settings.Program.Length == 0 ? Strings.Command_NotSet : Strings.Command_Invalid);
        }

        if (!_trust.IsApproved(settings.Hash()))
        {
            // The snapshot shown is the one approved: a sync load while the window is open cannot change what Yes starts.
            if (!_askApproval(item.Name, settings))
            {
                return LaunchResult.Cancelled;
            }

            Approve(item.Name, settings); // the sessions of every tile with this command start (CommandTrust.Changed)
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
        var runnable = settings.Problem() is null && _trust.IsApproved(settings.Hash());
        return [new SpecialIconAction(Strings.Command_RunNow, () => RunNow(item), runnable)];
    }

    public bool ShowProperties(SpecialIconItem item, bool isNew)
    {
        ArgumentNullException.ThrowIfNull(item);
        var before = CommandTileSettings.From(item);
        var trustedBefore = isNew || (before.Problem() is null && _trust.IsApproved(before.Hash()));
        var viewModel = new CommandTileViewModel(item);
        var window = new CommandTileWindow { DataContext = viewModel, Owner = _ownerProvider() };
        if (window.ShowDialog() != true)
        {
            return false;
        }

        viewModel.Apply(item);
        NoteSaved(CommandTileSettings.From(item), trustedBefore);
        return true;
    }

    /// <summary>
    /// After a save in Properties: a new tile, or an edit of one already approved here, is approved when the edited tile
    /// reaches the bar (<see cref="CreateState"/>). A tile that came from elsewhere and is still paused stays paused: only
    /// the approval window, which shows all of it, lets it in.
    /// </summary>
    internal void NoteSaved(CommandTileSettings saved, bool trustedBefore) =>
        _savedHere = trustedBefore && saved.Problem() is null ? saved.Hash() : null;

    private void Approve(string name, CommandTileSettings settings)
    {
        AppLog.Default.Info("Commands", $"Command tile \"{name}\" approved on this PC");
        if (!_trust.Approve(settings.Hash()))
        {
            AppLog.Default.Warning("Commands", $"The approval of \"{name}\" holds until the app ends: it could not be saved");
        }
    }

    private bool AskInWindow(string name, CommandTileSettings settings) =>
        new CommandApprovalWindow(name, settings) { Owner = _ownerProvider() }.ShowDialog() == true;

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
            Show("?", CommandLevel.Unknown, _settings.Program.Length == 0 ? Strings.Command_NotSet : Strings.Command_Invalid);
            return;
        }

        if (!_trust.IsApproved(_settings.Hash()))
        {
            Show("⏸", CommandLevel.Unknown, Strings.Command_Paused);
            return;
        }

        _started = true;
        Show("…", CommandLevel.Unknown, Strings.Command_Running); // not the pause sign until the first run ends
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
        var commandLine = CommandTileOutput.Shorten(
            string.IsNullOrWhiteSpace(_settings.Arguments) ? _settings.Program : $"{_settings.Program} {_settings.Arguments}",
            CommandTileOutput.MaxShownLineLength);
        var at = DateTime.Now.ToString("T", CultureInfo.CurrentCulture);
        if (run.StartError is { } error)
        {
            Show("!", CommandLevel.Failed, $"{commandLine}\n{CommandTileOutput.Shorten(string.Format(Strings.Command_CouldNotStart, error), CommandTileOutput.MaxShownLineLength)}");
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
