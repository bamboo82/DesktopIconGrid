using System.Windows.Input;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;

namespace DesktopIconGrid.ViewModels;

public sealed record ConflictPolicyChoice(string Id, string Label);

/// <summary>Backs the "Google Drive Bucket" properties dialog.</summary>
public sealed class DriveBucketViewModel : ObservableObject
{
    public static readonly IReadOnlyList<ConflictPolicyChoice> Policies =
    [
        new(DriveBucketHandler.KeepBoth, "Keep both (Drive allows duplicate names)"),
        new(DriveBucketHandler.Replace, "Replace the existing file"),
        new(DriveBucketHandler.Skip, "Skip the file"),
    ];

    private readonly SyncService _sync;
    private string _name;
    private string _folderInput;
    private string? _folderId;
    private string? _folderName;
    private ConflictPolicyChoice _policy;
    private string _statusText = string.Empty;
    private bool _isBusy;
    private bool _nameIsAutomatic;

    public DriveBucketViewModel(SpecialIconItem item, SyncService sync)
    {
        ArgumentNullException.ThrowIfNull(item);
        _sync = sync;
        _name = item.Name;
        _folderId = item.GetSetting(DriveBucketHandler.FolderIdKey);
        _folderName = item.GetSetting(DriveBucketHandler.FolderNameKey);
        _folderInput = _folderId ?? string.Empty;
        _policy = Policies.FirstOrDefault(p => string.Equals(p.Id, item.GetSetting(DriveBucketHandler.ConflictKey), StringComparison.OrdinalIgnoreCase)) ?? Policies[0];
        _nameIsAutomatic = _name is "Drive Bucket" or "";

        CheckFolderCommand = new RelayCommand(() => _ = CheckFolderAsync(), () => !IsBusy && HasParsableFolder);
        GrantAccessCommand = new RelayCommand(() => _ = GrantAccessAsync(), () => !IsBusy && NeedsAccess && _sync.HasCredentials);
    }

    public IReadOnlyList<ConflictPolicyChoice> PolicyChoices => Policies;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                _nameIsAutomatic = _name.Length == 0;
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    /// <summary>A Drive folder link or id as pasted by the user.</summary>
    public string FolderInput
    {
        get => _folderInput;
        set
        {
            if (SetProperty(ref _folderInput, value ?? string.Empty))
            {
                _folderId = DriveFolderLink.TryParseId(_folderInput, out var id) ? id : null;
                _folderName = null;
                OnPropertyChanged(nameof(FolderStatus));
                OnPropertyChanged(nameof(HasParsableFolder));
                OnPropertyChanged(nameof(CanSave));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool HasParsableFolder => _folderId is not null;

    public string FolderStatus => _folderId is null
        ? (_folderInput.Trim().Length == 0 ? "Paste the folder's link from Google Drive, or its id." : "That does not look like a Google Drive folder link or id.")
        : _folderName is null ? $"Folder id {_folderId} (click Check to confirm it)" : $"Folder \"{_folderName}\"";

    public ConflictPolicyChoice Policy
    {
        get => _policy;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _policy, value);
            }
        }
    }

    public bool NeedsAccess => !_sync.HasFullDriveAccess;

    public string AccessText => _sync.Files is null
        ? "Google Drive access is not available in this build."
        : !_sync.HasCredentials
            ? "Set up the Google API credentials in \"Sync with Google Drive…\" first."
            : !_sync.IsConnected
                ? "Not connected to Google Drive yet."
                : _sync.HasFullDriveAccess
                    ? $"Connected as {_sync.Settings.AccountEmail ?? "a Google account"} with access to your Drive."
                    : $"Connected as {_sync.Settings.AccountEmail ?? "a Google account"}, but without access to your whole Drive.";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanSave));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanSave => !IsBusy && _name.Trim().Length > 0 && _folderId is not null;

    public ICommand CheckFolderCommand { get; }

    public ICommand GrantAccessCommand { get; }

    public void Apply(SpecialIconItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Name = _name.Trim();
        item.SetSetting(DriveBucketHandler.FolderIdKey, _folderId);
        item.SetSetting(DriveBucketHandler.FolderNameKey, _folderName);
        item.SetSetting(DriveBucketHandler.ConflictKey, _policy.Id);
    }

    private async Task CheckFolderAsync()
    {
        if (_folderId is null || _sync.Files is null)
        {
            return;
        }

        if (NeedsAccess)
        {
            StatusText = "Grant access to Google Drive first, then check the folder.";
            return;
        }

        IsBusy = true;
        StatusText = "Looking up the folder…";
        try
        {
            var entry = await _sync.Files.GetEntryAsync(_folderId, CancellationToken.None);
            if (entry is null)
            {
                StatusText = "No folder with that id is visible to this account.";
            }
            else if (!entry.IsFolder)
            {
                StatusText = $"\"{entry.Name}\" is a file, not a folder.";
            }
            else
            {
                _folderName = entry.Name;
                if (_nameIsAutomatic)
                {
                    _name = entry.Name;
                    OnPropertyChanged(nameof(Name));
                }

                StatusText = $"Found folder \"{entry.Name}\".";
                OnPropertyChanged(nameof(FolderStatus));
            }
        }
        catch (Exception ex) when (SyncCoordinator.IsSyncError(ex))
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task GrantAccessAsync()
    {
        IsBusy = true;
        StatusText = "Finish signing in to Google in your browser…";
        try
        {
            await _sync.EnsureFullDriveAccessAsync(CancellationToken.None);
            StatusText = "Access granted.";
        }
        catch (Exception ex) when (SyncCoordinator.IsSyncError(ex))
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(NeedsAccess));
            OnPropertyChanged(nameof(AccessText));
        }
    }
}
