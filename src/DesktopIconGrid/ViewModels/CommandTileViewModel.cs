using System.Globalization;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.ViewModels;

public sealed record CommandDisplayChoice(CommandTileDisplay Display, string Label);

/// <summary>Backs the properties dialog of a command tile.</summary>
public sealed class CommandTileViewModel : ObservableObject
{
    public static readonly IReadOnlyList<CommandDisplayChoice> DisplayChoices =
    [
        new(CommandTileDisplay.Text, Strings.Command_ShowText),
        new(CommandTileDisplay.Ring, Strings.Command_ShowRing),
        new(CommandTileDisplay.Bar, Strings.Command_ShowBar),
        new(CommandTileDisplay.Number, Strings.Command_ShowNumber),
    ];

    private string _name;
    private string _program;
    private string _arguments;
    private string _startIn;
    private string _interval;
    private string _timeout;
    private CommandDisplayChoice _display;
    private string _warningAt;
    private string _criticalAt;
    private bool _opensTarget;
    private string _openTarget;

    public CommandTileViewModel(SpecialIconItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var settings = CommandTileSettings.From(item);
        _name = item.Name;
        _program = settings.Program;
        _arguments = settings.Arguments ?? string.Empty;
        _startIn = settings.StartIn ?? string.Empty;
        _interval = settings.IntervalSeconds.ToString(CultureInfo.CurrentCulture);
        _timeout = settings.TimeoutSeconds.ToString(CultureInfo.CurrentCulture);
        _display = DisplayChoices.First(c => c.Display == settings.Display);
        _warningAt = settings.WarningAt.ToString(CultureInfo.CurrentCulture);
        _criticalAt = settings.CriticalAt.ToString(CultureInfo.CurrentCulture);
        _opensTarget = settings.Click == CommandTileClick.Open;
        _openTarget = settings.OpenTarget ?? string.Empty;
    }

    public IReadOnlyList<CommandDisplayChoice> Displays => DisplayChoices;

    public string Name { get => _name; set => Set(ref _name, value); }

    public string Program { get => _program; set => Set(ref _program, value); }

    public string Arguments { get => _arguments; set => Set(ref _arguments, value); }

    public string StartIn { get => _startIn; set => Set(ref _startIn, value); }

    public string Interval { get => _interval; set => Set(ref _interval, value); }

    public string Timeout { get => _timeout; set => Set(ref _timeout, value); }

    public string WarningAt { get => _warningAt; set => Set(ref _warningAt, value); }

    public string CriticalAt { get => _criticalAt; set => Set(ref _criticalAt, value); }

    public string OpenTarget { get => _openTarget; set => Set(ref _openTarget, value); }

    public CommandDisplayChoice SelectedDisplay
    {
        get => _display;
        set
        {
            if (value is not null && SetProperty(ref _display, value))
            {
                OnPropertyChanged(nameof(ShowsGauge));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool ShowsGauge => _display.Display != CommandTileDisplay.Text;

    public bool RunsOnClick
    {
        get => !_opensTarget;
        set => OpensTarget = !value;
    }

    public bool OpensTarget
    {
        get => _opensTarget;
        set
        {
            if (SetProperty(ref _opensTarget, value))
            {
                OnPropertyChanged(nameof(RunsOnClick));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool CanSave => _name.Trim().Length > 0 && Settings() is { } settings && settings.Problem() is null;

    /// <summary>The settings as entered, or <see langword="null"/> when a number does not read.</summary>
    public CommandTileSettings? Settings()
    {
        if (!int.TryParse(_interval, NumberStyles.Integer, CultureInfo.CurrentCulture, out var interval)
            || !int.TryParse(_timeout, NumberStyles.Integer, CultureInfo.CurrentCulture, out var timeout))
        {
            return null;
        }

        double warning = 70, critical = 90;
        if (ShowsGauge
            && (!double.TryParse(_warningAt, NumberStyles.Float, CultureInfo.CurrentCulture, out warning)
                || !double.TryParse(_criticalAt, NumberStyles.Float, CultureInfo.CurrentCulture, out critical)))
        {
            return null;
        }

        return new CommandTileSettings(
            _program.Trim(),
            Blank(_arguments),
            Blank(_startIn),
            CommandTileSettings.NormalizeInterval(interval),
            CommandTileSettings.NormalizeTimeout(timeout),
            _display.Display,
            warning,
            critical,
            _opensTarget ? CommandTileClick.Open : CommandTileClick.RunNow,
            Blank(_openTarget));
    }

    public void Apply(SpecialIconItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Name = _name.Trim();
        (Settings() ?? throw new InvalidOperationException("The dialog was saved with numbers that do not read.")).ApplyTo(item);
    }

    private static string? Blank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private void Set(ref string field, string? value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetProperty(ref field, value ?? string.Empty, name))
        {
            OnPropertyChanged(nameof(CanSave));
        }
    }
}
