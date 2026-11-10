using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;
using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.ViewModels;

/// <summary>One shortcut or session in the Import list.</summary>
public sealed class ImportChoice(ImportCandidate candidate, bool isOnBar) : ObservableObject
{
    private bool _isChosen;
    private ImageSource? _image;

    public ImportCandidate Candidate { get; } = candidate;

    public string Name => Candidate.Name;

    /// <summary>The source's name, by which the list is grouped.</summary>
    public string SourceName => ImportViewModel.SourceName(Candidate.Source);

    /// <summary>What it starts, marked when it is on the bar already.</summary>
    public string Detail => IsOnBar ? $"{Strings.AddApp_OnBar} · {Command}" : Command;

    public bool IsOnBar { get; } = isOnBar;

    public bool IsChosen
    {
        get => _isChosen;
        set => SetProperty(ref _isChosen, value);
    }

    public ImageSource? Image
    {
        get => _image;
        set => SetProperty(ref _image, value);
    }

    private string Command => string.IsNullOrWhiteSpace(Candidate.Parameters) ? Candidate.Location : $"{Candidate.Location} {Candidate.Parameters}";
}

/// <summary>Backs the Import dialog: what other programs have on this PC, grouped by source, ticked to be added.</summary>
public sealed class ImportViewModel : ObservableObject
{
    private readonly IReadOnlySet<string> _onBar;
    private bool _isLoading;
    private string _status = string.Empty;
    private bool _groupPerSource;

    /// <param name="keysOnBar">The shortcuts already on the bar (<see cref="ImportPlan.KeyOf"/>, any case): shown as such and not ticked.</param>
    /// <param name="canGroup">Whether the items go to the bar itself, where groups may go; not into a group.</param>
    public ImportViewModel(IReadOnlySet<string> keysOnBar, bool canGroup)
    {
        _onBar = keysOnBar;
        CanGroup = canGroup;
        View = CollectionViewSource.GetDefaultView(Choices);
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ImportChoice.SourceName)));
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
    }

    public ObservableCollection<ImportChoice> Choices { get; } = [];

    /// <summary>The choices, grouped by source.</summary>
    public ICollectionView View { get; }

    public bool CanGroup { get; }

    /// <summary>Each source in a group of its own; only where groups may go (<see cref="CanGroup"/>).</summary>
    public bool GroupPerSource
    {
        get => _groupPerSource;
        set => SetProperty(ref _groupPerSource, value && CanGroup);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public System.Windows.Input.ICommand SelectAllCommand { get; }

    public System.Windows.Input.ICommand SelectNoneCommand { get; }

    public IReadOnlyList<ImportCandidate> Chosen => Choices.Where(c => c.IsChosen).Select(c => c.Candidate).ToList();

    public int ChosenCount => Choices.Count(c => c.IsChosen);

    public string ImportText => ChosenCount > 0 ? string.Format(Strings.Import_ImportCount, ChosenCount) : Strings.Import_Import;

    public bool CanImport => ChosenCount > 0;

    public static string SourceName(ImportSource source) => source switch
    {
        ImportSource.TaskbarPins => Strings.Import_SourceTaskbar,
        ImportSource.Desktop => Strings.Import_SourceDesktop,
        ImportSource.WindowsTerminal => Strings.Import_SourceTerminal,
        ImportSource.WinScp => Strings.Import_SourceWinScp,
        ImportSource.Putty => Strings.Import_SourcePutty,
        _ => Strings.Import_SourceSsh,
    };

    /// <summary>Lists what <paramref name="load"/> finds, then fetches the icons one by one with <paramref name="icon"/>.</summary>
    public async Task LoadAsync(Func<Task<IReadOnlyList<ImportCandidate>>> load, Func<string, Task<ImageSource?>>? icon, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(load);
        IsLoading = true;
        Status = Strings.Import_Loading;
        IReadOnlyList<ImportCandidate> found;
        try
        {
            found = await load();
        }
        finally
        {
            IsLoading = false;
        }

        foreach (var candidate in found.DistinctBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
        {
            var choice = new ImportChoice(candidate, _onBar.Contains(candidate.Key));
            choice.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ImportChoice.IsChosen))
                {
                    OnPropertyChanged(nameof(ChosenCount));
                    OnPropertyChanged(nameof(ImportText));
                    OnPropertyChanged(nameof(CanImport));
                }
            };
            Choices.Add(choice);
        }

        Status = Choices.Count == 0 ? Strings.Import_None : string.Empty;
        if (icon is null)
        {
            return;
        }

        foreach (var choice in Choices.ToList())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            choice.Image = await icon(choice.Candidate.Location);
        }
    }

    /// <summary>Ticks or clears every choice that is not on the bar already.</summary>
    private void SetAll(bool chosen)
    {
        foreach (var choice in Choices.Where(c => !c.IsOnBar || !chosen))
        {
            choice.IsChosen = chosen;
        }
    }
}

/// <summary>What the Import dialog returns: the chosen candidates, and whether each source gets a group.</summary>
public sealed record ImportRequest(IReadOnlyList<ImportCandidate> Chosen, bool GroupPerSource);
