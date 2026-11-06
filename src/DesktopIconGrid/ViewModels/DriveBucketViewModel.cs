using System.Windows.Input;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;

namespace DesktopIconGrid.ViewModels;

public sealed record ConflictPolicyChoice(string Id, string Label);

/// <summary>Backs the "Google Drive Bucket" properties dialog.</summary>
public sealed class DriveBucketViewModel : ObservableObject
{
    public static readonly IReadOnlyList<ConflictPolicyChoice> Policies =
    [
        new(DriveBucketHandler.KeepBoth, Strings.Bucket_KeepBoth),
        new(DriveBucketHandler.Replace, Strings.Bucket_Replace),
        new(DriveBucketHandler.Skip, Strings.Bucket_Skip),
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
        _nameIsAutomatic = _name.Length == 0 || _name == Strings.Bucket_ItemName || _name == "Drive Bucket"; // the English default, from layouts saved before 0.5.0

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
        ? (_folderInput.Trim().Length == 0 ? Strings.Bucket_PasteLink : Strings.Bucket_NotALink)
        : _folderName is null ? string.Format(Strings.Bucket_FolderIdUnchecked, _folderId) : string.Format(Strings.Bucket_FolderNamed, _folderName);

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
        ? Strings.Bucket_NotAvailable
        : !_sync.HasCredentials
            ? Strings.Bucket_SetUpFirst
            : !_sync.IsGoogleConnected
                ? Strings.Bucket_NotConnected
                : _sync.HasFullDriveAccess
                    ? string.Format(Strings.Bucket_ConnectedFull, _sync.Settings.AccountEmail ?? Strings.Common_AGoogleAccount)
                    : string.Format(Strings.Bucket_ConnectedLimited, _sync.Settings.AccountEmail ?? Strings.Common_AGoogleAccount);

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
            StatusText = Strings.Bucket_GrantFirst;
            return;
        }

        IsBusy = true;
        StatusText = Strings.Bucket_LookingUp;
        try
        {
            var entry = await _sync.Files.GetEntryAsync(_folderId, CancellationToken.None);
            if (entry is null)
            {
                StatusText = Strings.Bucket_NoFolderVisible;
            }
            else if (!entry.IsFolder)
            {
                StatusText = string.Format(Strings.Bucket_IsAFile, entry.Name);
            }
            else
            {
                _folderName = entry.Name;
                if (_nameIsAutomatic)
                {
                    _name = entry.Name;
                    OnPropertyChanged(nameof(Name));
                }

                StatusText = string.Format(Strings.Bucket_FoundFolder, entry.Name);
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
        StatusText = Strings.Common_FinishSignIn;
        try
        {
            await _sync.EnsureFullDriveAccessAsync(CancellationToken.None);
            StatusText = Strings.Bucket_AccessGranted;
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
