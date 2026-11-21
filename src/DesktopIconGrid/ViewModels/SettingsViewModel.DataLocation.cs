using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>
/// Settings ▸ Portable ▸ <em>Data location</em> (0.13.0 WP1; on General until 0.14.0 WP4): this PC's folder or a
/// portable folder named in <c>portable.txt</c>, where the data is now, and, in a portable copy, bringing this PC's bar
/// in. Nothing is written here: Save checks the new place (<see cref="TryPlanDataMove"/>) and the bar carries it out
/// once the dialog closes.
/// </summary>
public sealed partial class SettingsViewModel
{
    private DataFolders _folders = new(string.Empty, string.Empty, string.Empty, false, string.Empty, false);
    private bool _dataPortable;
    private bool _dataPortableBefore;
    private string _dataFolder = ".";
    private string _dataFolderBefore = ".";
    private string? _dataProblem;

    /// <summary>Shows <paramref name="folders"/>: where the data is now, and the choice <c>portable.txt</c> holds now.</summary>
    public void UseDataFolders(DataFolders folders)
    {
        _folders = folders ?? throw new ArgumentNullException(nameof(folders));
        _dataPortable = _dataPortableBefore = folders.HasMarker;
        _dataFolder = _dataFolderBefore = folders.MarkerLine.Length == 0 ? "." : folders.MarkerLine;
        _dataProblem = null;
        PlannedMove = null;
        ImportThisPcRequested = false;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>The data in <c>%LocalAppData%\DesktopIconGrid</c>, without <c>portable.txt</c>.</summary>
    public bool DataIsThisPc
    {
        get => !_dataPortable;
        set => DataIsPortable = !value;
    }

    /// <summary>The data in the folder <c>portable.txt</c> names.</summary>
    public bool DataIsPortable
    {
        get => _dataPortable;
        set
        {
            if (SetProperty(ref _dataPortable, value))
            {
                OnDataChanged();
            }
        }
    }

    /// <summary>The folder as <c>portable.txt</c> is to say it: <c>.</c>, a name under the program's folder, or a full one.</summary>
    public string DataFolder
    {
        get => _dataFolder;
        set
        {
            if (SetProperty(ref _dataFolder, value ?? string.Empty))
            {
                OnDataChanged();
            }
        }
    }

    public string DataThisPcText => string.Format(Strings.Settings_DataThisPc, @"%LocalAppData%\" + LayoutStorage.ApplicationFolderName);

    /// <summary>Where the running bar's data is: a new place takes effect at the next start.</summary>
    public string DataNow => _folders.Directory;

    public string ProgramDirectory => _folders.ProgramDirectory;

    public string ThisPcDirectory => _folders.ThisPcDirectory;

    /// <summary>The folder <see cref="DataFolder"/> names, in full, for Browse to start in; <see langword="null"/> when it names none.</summary>
    public string? DataFolderFull => DataLocation.FolderOf(_dataFolder, _folders.ProgramDirectory);

    /// <summary><em>Import this PC's data…</em>: in a portable copy whose data is not in <c>%LocalAppData%</c>, when this PC has a bar there.</summary>
    public bool ShowsDataImport =>
        _folders.IsPortable && !DataLocation.SameFolder(_folders.Directory, _folders.ThisPcDirectory) && DataImport.HasBar(_folders.ThisPcDirectory);

    /// <summary>The choice or the folder differs from what <c>portable.txt</c> says now.</summary>
    public bool DataLocationChanged =>
        _dataPortable != _dataPortableBefore
        || (_dataPortable && !string.Equals(Full(_dataFolder), Full(_dataFolderBefore), StringComparison.OrdinalIgnoreCase));

    /// <summary>A changed portable folder whose name is no folder's: Save waits for it, on the Portable tab.</summary>
    private bool HasPortableProblem => _dataProblem is not null || (DataLocationChanged && _dataPortable && DataFolderFull is null);

    /// <summary>What is wrong with the data location, said under it; empty when nothing is.</summary>
    public string DataProblem =>
        _dataProblem
        ?? (DataLocationChanged && _dataPortable && DataFolderFull is null ? string.Format(Strings.Settings_DataNotAFolder, _dataFolder.Trim()) : string.Empty);

    public bool HasDataProblem => DataProblem.Length > 0;

    /// <summary>The new place, checked by <see cref="TryPlanDataMove"/> and told whether to copy; the bar carries it out.</summary>
    public DataMove? PlannedMove { get; set; }

    /// <summary><em>Import this PC's data…</em> was confirmed: the dialog closes and the bar brings it in.</summary>
    public bool ImportThisPcRequested { get; set; }

    /// <summary>
    /// At Save: nothing to do when the place did not change; else the new folder must name a folder that can be made and
    /// written (<paramref name="canUse"/>), and the move is planned without a copy yet. False, with the reason under the
    /// choice, when it cannot be used: nothing is written.
    /// </summary>
    public bool TryPlanDataMove(Func<string, bool> canUse, out DataMove? move)
    {
        ArgumentNullException.ThrowIfNull(canUse);
        move = null;
        _dataProblem = null;
        if (!DataLocationChanged)
        {
            OnDataProblemChanged();
            return true;
        }

        var text = _dataFolder.Trim().Length == 0 ? "." : _dataFolder.Trim();
        var folder = _dataPortable ? DataFolderFull : _folders.ThisPcDirectory;
        if (folder is null)
        {
            OnDataProblemChanged();
            return false; // the line under the choice says it already
        }

        if (!canUse(folder))
        {
            _dataProblem = string.Format(Strings.Settings_DataCannotUse, folder);
            OnDataProblemChanged();
            return false;
        }

        OnDataProblemChanged();
        move = new DataMove(_dataPortable, _dataPortable ? text : string.Empty, folder, Copy: false);
        return true;
    }

    /// <summary>Whether a copy is worth asking about: the new folder is not the one in use, and there is a bar to copy.</summary>
    public bool OffersCopy(DataMove move) =>
        move is not null && !DataLocation.SameFolder(move.Folder, _folders.Directory) && DataImport.HasBar(_folders.Directory);

    private string? Full(string folder) => DataLocation.FolderOf(folder.Trim().Length == 0 ? "." : folder, _folders.ProgramDirectory);

    private void OnDataChanged()
    {
        _dataProblem = null;
        OnPropertyChanged(nameof(DataIsThisPc));
        OnPropertyChanged(nameof(DataIsPortable));
        OnPropertyChanged(nameof(DataFolderFull));
        OnPropertyChanged(nameof(DataLocationChanged));
        OnDataProblemChanged();
    }

    private void OnDataProblemChanged()
    {
        OnPropertyChanged(nameof(DataProblem));
        OnPropertyChanged(nameof(HasDataProblem));
        OnPropertyChanged(nameof(PortableTab));
        OnProblemsChanged();
    }
}
