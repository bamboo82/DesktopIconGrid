using System.Collections.ObjectModel;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.ViewModels;

/// <summary>One place the Repair dialog offers for a missing program; <paramref name="Likely"/> as in <see cref="RepairCandidate.Likely"/>.</summary>
public sealed record RepairChoice(string Path, string Reason, bool Likely = true);

/// <summary>What the Repair dialog decided: the new path, and whether it is for this PC only.</summary>
public sealed record RepairResult(string Path, bool OnlyThisPc);

/// <summary>Backs the Repair dialog: where a missing shortcut's program may be on this PC, and where the fix applies.</summary>
public sealed class RepairViewModel : ObservableObject
{
    private readonly Func<CancellationToken, RepairSearch> _search;
    private RepairChoice? _selectedChoice;
    private bool _onlyThisPc;
    private bool _isSearching;
    private string _status = string.Empty;

    /// <param name="search">Looks for candidates (<see cref="LocationRepair.Search"/>); runs on a worker thread.</param>
    /// <param name="canBeOnlyThisPc">This PC's id is known, so the fix can apply to it alone.</param>
    public RepairViewModel(ShortcutItem shortcut, Func<CancellationToken, RepairSearch> search, bool canBeOnlyThisPc)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        Name = shortcut.Name;
        Location = shortcut.EffectiveLocation;
        _search = search;
        CanBeOnlyThisPc = canBeOnlyThisPc;
        _onlyThisPc = canBeOnlyThisPc; // the safe default: other PCs that share the bar keep theirs
    }

    public string Name { get; }

    public string Location { get; }

    public string Intro => string.Format(Strings.Repair_Intro, Name, Location);

    public ObservableCollection<RepairChoice> Choices { get; } = [];

    public RepairChoice? SelectedChoice
    {
        get => _selectedChoice;
        set
        {
            if (SetProperty(ref _selectedChoice, value))
            {
                OnPropertyChanged(nameof(CanRepair));
            }
        }
    }

    public bool CanBeOnlyThisPc { get; }

    public bool OnlyThisPc
    {
        get => _onlyThisPc;
        set => SetProperty(ref _onlyThisPc, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set => SetProperty(ref _isSearching, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool CanRepair => SelectedChoice is not null;

    public RepairResult? Result => SelectedChoice is { } choice ? new RepairResult(choice.Path, CanBeOnlyThisPc && OnlyThisPc) : null;

    /// <summary>
    /// Looks for the program off the UI thread and lists what was found, the most likely first. Only a likely one is
    /// selected: a match on the file name alone may be another program, and Repair is the default button.
    /// </summary>
    public async Task SearchAsync(CancellationToken cancellationToken)
    {
        IsSearching = true;
        Status = string.Format(Strings.Repair_Searching, System.IO.Path.GetFileName(Location.TrimEnd('\\', '/')));
        try
        {
            var search = await Task.Run(() => _search(cancellationToken), cancellationToken);
            foreach (var candidate in search.Candidates)
            {
                Add(candidate.Path, ReasonText(candidate.Reason), candidate.Likely);
            }

            SelectedChoice ??= Choices.FirstOrDefault(c => c.Likely);
            var found = Choices.Count switch
            {
                0 => Strings.Repair_NothingFound,
                1 => Strings.Repair_FoundOne,
                _ => string.Format(Strings.Repair_FoundMany, Choices.Count),
            };
            var unsure = Choices.Count > 0 && !Choices.Any(c => c.Likely) ? " " + Strings.Repair_NoneSure : string.Empty;
            var stopped = search.StoppedEarly ? " " + Strings.Repair_StoppedEarly : string.Empty;
            Status = found + unsure + stopped;
        }
        catch (OperationCanceledException)
        {
            Status = string.Empty;
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>A path the user picked with Browse…: listed and selected.</summary>
    public void AddChosen(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SelectedChoice = Add(path, Strings.Repair_ReasonChosen);
    }

    private RepairChoice Add(string path, string reason, bool likely = true)
    {
        var existing = Choices.FirstOrDefault(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var choice = new RepairChoice(path, reason, likely);
        Choices.Add(choice);
        return choice;
    }

    private static string ReasonText(RepairReason reason) => reason switch
    {
        RepairReason.OtherVersion => Strings.Repair_ReasonOtherVersion,
        RepairReason.Registered => Strings.Repair_ReasonRegistered,
        _ => Strings.Repair_ReasonInstalled,
    };
}
