using System.Collections.ObjectModel;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.ViewModels;

/// <summary>One place the Repair dialog offers for a missing program.</summary>
public sealed record RepairChoice(string Path, string Reason);

/// <summary>What the Repair dialog decided: the new path, and whether it is for this PC only.</summary>
public sealed record RepairResult(string Path, bool OnlyThisPc);

/// <summary>Backs the Repair dialog: where a missing shortcut's program may be on this PC, and where the fix applies.</summary>
public sealed class RepairViewModel : ObservableObject
{
    private readonly Func<CancellationToken, IReadOnlyList<RepairCandidate>> _search;
    private RepairChoice? _selectedChoice;
    private bool _onlyThisPc;
    private bool _isSearching;
    private string _status = string.Empty;

    /// <param name="search">Looks for candidates (<see cref="LocationRepair.Candidates"/>); runs on a worker thread.</param>
    /// <param name="canBeOnlyThisPc">This PC's id is known, so the fix can apply to it alone.</param>
    public RepairViewModel(ShortcutItem shortcut, Func<CancellationToken, IReadOnlyList<RepairCandidate>> search, bool canBeOnlyThisPc)
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

    /// <summary>Looks for the program off the UI thread and lists what was found, the most likely first (and selected).</summary>
    public async Task SearchAsync(CancellationToken cancellationToken)
    {
        IsSearching = true;
        Status = string.Format(Strings.Repair_Searching, System.IO.Path.GetFileName(Location.TrimEnd('\\', '/')));
        try
        {
            var found = await Task.Run(() => _search(cancellationToken), cancellationToken);
            foreach (var candidate in found)
            {
                Add(candidate.Path, ReasonText(candidate.Reason));
            }

            SelectedChoice ??= Choices.FirstOrDefault();
            Status = Choices.Count switch
            {
                0 => Strings.Repair_NothingFound,
                1 => Strings.Repair_FoundOne,
                _ => string.Format(Strings.Repair_FoundMany, Choices.Count),
            };
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

    private RepairChoice Add(string path, string reason)
    {
        var existing = Choices.FirstOrDefault(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var choice = new RepairChoice(path, reason);
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
