using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.ViewModels;

public sealed record LogLevelChoice(string Label, LogLevel Minimum);

public sealed record LogTimeRange(string Label, TimeSpan? Window, bool TodayOnly = false);

/// <summary>One line of the log viewer.</summary>
public sealed class LogEntryViewModel(LogEntry entry)
{
    public LogEntry Entry { get; } = entry;

    public string TimeText => Entry.Time.LocalDateTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    public string DateText => Entry.Time.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public string LevelText => Entry.Level.ToString();

    public string Category => Entry.Category;

    public string Message => Entry.Message;

    public bool IsWarning => Entry.Level == LogLevel.Warning;

    public bool IsError => Entry.Level == LogLevel.Error;

    public bool IsDebug => Entry.Level == LogLevel.Debug;

    public string FullText => Entry.Format();
}

/// <summary>Backs the application log viewer: live entries with level, category, time and text filters.</summary>
public sealed class AppLogViewModel : ObservableObject, IDisposable
{
    public static readonly IReadOnlyList<LogLevelChoice> LevelChoices =
    [
        new(Strings.Log_Everything, LogLevel.Debug),
        new(Strings.Log_InfoAndAbove, LogLevel.Info),
        new(Strings.Log_WarningsAndErrors, LogLevel.Warning),
        new(Strings.Log_ErrorsOnly, LogLevel.Error),
    ];

    public static readonly IReadOnlyList<LogTimeRange> TimeRanges =
    [
        new(Strings.Log_AnyTime, null),
        new(Strings.Log_Last15, TimeSpan.FromMinutes(15)),
        new(Strings.Log_LastHour, TimeSpan.FromHours(1)),
        new(Strings.Log_Today, null, TodayOnly: true),
    ];

    public static string AllCategories => Strings.Log_AllCategories;

    private readonly AppLog _log;
    private readonly Dispatcher _dispatcher;
    private readonly List<LogEntryViewModel> _all = [];
    private LogLevelChoice _level = LevelChoices[1];
    private LogTimeRange _timeRange = TimeRanges[0];
    private string _category = AllCategories;
    private string _filterText = string.Empty;
    private bool _followTail = true;
    private LogEntryViewModel? _selected;

    public AppLogViewModel(AppLog log, Dispatcher dispatcher)
    {
        _log = log;
        _dispatcher = dispatcher;
        foreach (var entry in log.Snapshot())
        {
            _all.Add(new LogEntryViewModel(entry));
        }

        RebuildCategories();
        Refilter();
        _log.EntryAdded += OnEntryAdded;

        ClearCommand = new RelayCommand(() =>
        {
            _log.Clear();
            _all.Clear();
            RebuildCategories();
            Refilter();
        });
        CopyCommand = new RelayCommand(() => CopyRequested?.Invoke(this, ToText()), () => Visible.Count > 0);
        CopyLineCommand = new RelayCommand<LogEntryViewModel>(row => CopyRequested?.Invoke(this, row.FullText), row => row is not null);
        CopyMessageCommand = new RelayCommand<LogEntryViewModel>(row => CopyRequested?.Invoke(this, row.Message), row => row is not null);
        OnlyThisCategoryCommand = new RelayCommand<LogEntryViewModel>(row => Category = row.Category, row => row is not null);
        ResetFiltersCommand = new RelayCommand(ResetFilters);
    }

    public ObservableCollection<LogEntryViewModel> Visible { get; } = [];

    public ObservableCollection<string> Categories { get; } = [];

    public IReadOnlyList<LogLevelChoice> Levels => LevelChoices;

    public IReadOnlyList<LogTimeRange> Ranges => TimeRanges;

    public string? FilePath => _log.FilePath;

    public LogLevelChoice Level
    {
        get => _level;
        set
        {
            if (value is not null && SetProperty(ref _level, value))
            {
                Refilter();
            }
        }
    }

    public LogTimeRange TimeRange
    {
        get => _timeRange;
        set
        {
            if (value is not null && SetProperty(ref _timeRange, value))
            {
                Refilter();
            }
        }
    }

    public string Category
    {
        get => _category;
        set
        {
            if (SetProperty(ref _category, string.IsNullOrEmpty(value) ? AllCategories : value))
            {
                Refilter();
            }
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value ?? string.Empty))
            {
                Refilter();
            }
        }
    }

    /// <summary>Keep the newest entry in view as entries arrive.</summary>
    public bool FollowTail
    {
        get => _followTail;
        set
        {
            if (SetProperty(ref _followTail, value) && value)
            {
                Appended?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public LogEntryViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(DetailText));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>The selected entry in full (multi-line messages wrap here).</summary>
    public string DetailText => _selected?.FullText ?? Strings.Log_SelectEntry;

    public string StatusText
    {
        get
        {
            var warnings = _all.Count(r => r.IsWarning);
            var errors = _all.Count(r => r.IsError);
            return string.Format(Strings.Log_Summary, Visible.Count, _all.Count, warnings, errors);
        }
    }

    public ICommand ClearCommand { get; }

    public ICommand CopyCommand { get; }

    public ICommand CopyLineCommand { get; }

    public ICommand CopyMessageCommand { get; }

    public ICommand OnlyThisCategoryCommand { get; }

    public ICommand ResetFiltersCommand { get; }

    /// <summary>Raised with text to put on the clipboard.</summary>
    public event EventHandler<string>? CopyRequested;

    /// <summary>Raised after entries were appended or refiltered so the view can scroll to the end.</summary>
    public event EventHandler? Appended;

    public string ToText()
    {
        var builder = new StringBuilder();
        foreach (var row in Visible)
        {
            builder.AppendLine(row.FullText);
        }

        return builder.ToString();
    }

    /// <summary>Writes the visible (or all) entries to a text file.</summary>
    public void SaveTo(string path, bool visibleOnly)
    {
        var rows = visibleOnly ? Visible.AsEnumerable() : _all;
        File.WriteAllLines(path, rows.Select(r => r.FullText), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public string SuggestedFileName() =>
        $"DesktopIconGrid-log-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.txt";

    public void ResetFilters()
    {
        _level = LevelChoices[1];
        _timeRange = TimeRanges[0];
        _category = AllCategories;
        _filterText = string.Empty;
        OnPropertyChanged(nameof(Level));
        OnPropertyChanged(nameof(TimeRange));
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(FilterText));
        Refilter();
    }

    public void Dispose() => _log.EntryAdded -= OnEntryAdded;

    private void OnEntryAdded(object? sender, LogEntry entry)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => OnEntryAdded(sender, entry));
            return;
        }

        var row = new LogEntryViewModel(entry);
        _all.Add(row);
        while (_all.Count > _log.Capacity)
        {
            var dropped = _all[0];
            _all.RemoveAt(0);
            Visible.Remove(dropped);
        }

        if (!Categories.Contains(entry.Category))
        {
            Categories.Add(entry.Category);
        }

        if (Matches(row, DateTimeOffset.Now))
        {
            Visible.Add(row);
            if (_followTail)
            {
                Appended?.Invoke(this, EventArgs.Empty);
            }
        }

        OnPropertyChanged(nameof(StatusText));
    }

    private void RebuildCategories()
    {
        Categories.Clear();
        Categories.Add(AllCategories);
        foreach (var category in _all.Select(r => r.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase))
        {
            Categories.Add(category);
        }
    }

    private void Refilter()
    {
        var now = DateTimeOffset.Now;
        Visible.Clear();
        foreach (var row in _all.Where(r => Matches(r, now)))
        {
            Visible.Add(row);
        }

        OnPropertyChanged(nameof(StatusText));
        CommandManager.InvalidateRequerySuggested();
        if (_followTail)
        {
            Appended?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool Matches(LogEntryViewModel row, DateTimeOffset now)
    {
        if (row.Entry.Level < _level.Minimum)
        {
            return false;
        }

        if (_category != AllCategories && !string.Equals(row.Category, _category, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_timeRange.Window is { } window && now - row.Entry.Time > window)
        {
            return false;
        }

        if (_timeRange.TodayOnly && row.Entry.Time.LocalDateTime.Date != now.LocalDateTime.Date)
        {
            return false;
        }

        return _filterText.Length == 0
            || row.Message.Contains(_filterText, StringComparison.CurrentCultureIgnoreCase)
            || row.Category.Contains(_filterText, StringComparison.CurrentCultureIgnoreCase);
    }
}
