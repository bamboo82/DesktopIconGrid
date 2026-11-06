using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.ViewModels;

/// <summary>One installed app in the Add apps list.</summary>
public sealed class AppChoice(InstalledApp app, bool isOnBar) : ObservableObject
{
    private bool _isChosen;
    private ImageSource? _image;

    public InstalledApp App { get; } = app;

    public string Name => App.Name;

    /// <summary>"Microsoft Store app", or where the program is.</summary>
    public string Detail => IsOnBar ? $"{Strings.AddApp_OnBar} · {Where}" : Where;

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

    private string Where => App.IsStoreApp ? Strings.AddApp_StoreApp : App.Location;
}

/// <summary>Backs the Add apps dialog: every installed app, filtered as the user types, ticked to be added.</summary>
public sealed class AddAppViewModel : ObservableObject
{
    private readonly IReadOnlySet<string> _onBar;
    private string _filter = string.Empty;
    private bool _isLoading;
    private string _status = string.Empty;

    /// <param name="locationsOnBar">The locations already on the bar (any case), shown as such and not ticked.</param>
    public AddAppViewModel(IReadOnlySet<string> locationsOnBar)
    {
        _onBar = locationsOnBar;
        View = CollectionViewSource.GetDefaultView(Apps);
        View.Filter = item => item is AppChoice choice && Matches(choice, _filter);
    }

    public ObservableCollection<AppChoice> Apps { get; } = [];

    /// <summary>The apps the list shows: <see cref="Apps"/> through the filter.</summary>
    public ICollectionView View { get; }

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value ?? string.Empty))
            {
                View.Refresh();
            }
        }
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

    public IReadOnlyList<InstalledApp> Chosen => Apps.Where(a => a.IsChosen).Select(a => a.App).ToList();

    public int ChosenCount => Apps.Count(a => a.IsChosen);

    public string AddText => ChosenCount > 0 ? string.Format(Strings.AddApp_AddCount, ChosenCount) : Strings.AddApp_Add;

    public bool CanAdd => ChosenCount > 0;

    /// <summary>A name matches when it contains the filter, ignoring case and width (so Chinese and full-width letters work).</summary>
    public static bool Matches(AppChoice choice, string filter)
    {
        ArgumentNullException.ThrowIfNull(choice);
        var text = filter?.Trim() ?? string.Empty;
        return text.Length == 0
               || CultureInfo.CurrentCulture.CompareInfo.IndexOf(choice.Name, text, CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType) >= 0;
    }

    /// <summary>Lists the apps <paramref name="load"/> returns, then fetches their icons one by one with <paramref name="icon"/>.</summary>
    public async Task LoadAsync(Func<Task<IReadOnlyList<InstalledApp>>> load, Func<string, Task<ImageSource?>>? icon, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(load);
        IsLoading = true;
        Status = Strings.AddApp_Loading;
        IReadOnlyList<InstalledApp> apps;
        try
        {
            apps = await load();
        }
        finally
        {
            IsLoading = false;
        }

        foreach (var app in apps)
        {
            var choice = new AppChoice(app, _onBar.Contains(app.Location));
            choice.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppChoice.IsChosen))
                {
                    OnPropertyChanged(nameof(ChosenCount));
                    OnPropertyChanged(nameof(AddText));
                    OnPropertyChanged(nameof(CanAdd));
                }
            };
            Apps.Add(choice);
        }

        Status = Apps.Count == 0 ? Strings.AddApp_None : string.Empty;
        if (icon is null)
        {
            return;
        }

        foreach (var choice in Apps.ToList())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            choice.Image = await icon(choice.App.Location);
        }
    }
}
