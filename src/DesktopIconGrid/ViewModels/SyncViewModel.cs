using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>Backs the "Sync across PCs" dialog: where the copies are kept, the Google account, and the copies.</summary>
public sealed class SyncViewModel : ObservableObject
{
    public const string CloudConsoleUrl = "https://console.cloud.google.com/apis/credentials";

    private readonly SyncCoordinator _sync;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _operation;
    private bool _isBusy;
    private string _statusText = string.Empty;
    private RemoteProfile? _selectedProfile;
    private string _newProfileName;

    public SyncViewModel(SyncCoordinator sync, IDialogService dialogs)
    {
        _sync = sync;
        _dialogs = dialogs;
        _newProfileName = Settings.LinkedProfile ?? string.Empty;
        _sync.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null or nameof(SyncCoordinator.Status))
            {
                OnPropertyChanged(nameof(BackgroundStatus));
            }
        };

        ConnectCommand = new RelayCommand(() => _ = ConnectAsync(), () => HasCredentials && !IsBusy && !IsGoogleConnected);
        CancelCommand = new RelayCommand(() => _operation?.Cancel(), () => IsBusy);
        DisconnectCommand = new RelayCommand(() => _ = DisconnectAsync(), () => IsGoogleConnected && !IsBusy);
        ChooseFolderCommand = new RelayCommand(() => _ = ChooseFolderAsync(), () => !IsBusy);
        UseGoogleCommand = new RelayCommand(() => _ = UseGoogleDriveAsync(), () => !IsBusy);
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => IsConnected && !IsBusy);
        SaveAsCommand = new RelayCommand(() => _ = SaveAsAsync(), () => IsConnected && !IsBusy && !string.IsNullOrWhiteSpace(NewProfileName));
        OverwriteCommand = new RelayCommand(() => _ = OverwriteAsync(), () => IsConnected && !IsBusy && SelectedProfile is not null);
        LoadCommand = new RelayCommand(() => _ = LoadAsync(), () => IsConnected && !IsBusy && SelectedProfile is not null);
        DeleteCommand = new RelayCommand(() => _ = DeleteAsync(), () => IsConnected && !IsBusy && SelectedProfile is not null);
        UnlinkCommand = new RelayCommand(() => _ = UnlinkAsync(), () => !IsBusy && Settings.LinkedProfile is not null);
        OpenConsoleCommand = new RelayCommand(OpenCloudConsole);
    }

    private SyncService Service => _sync.Service;

    private SyncSettings Settings => Service.Settings;

    /// <summary>sync.json could not be read at start: these settings only stand in for it and are not saved (<see cref="SyncSettings.IsStandIn"/>).</summary>
    public bool SettingsNotRead => Settings.IsStandIn;

    // ----- credentials and device ---------------------------------------------------------------

    public string ClientId
    {
        get => Settings.ClientId ?? string.Empty;
        set
        {
            Settings.ClientId = value;
            SaveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCredentials));
            OnPropertyChanged(nameof(ShowCredentials));
        }
    }

    public string ClientSecret
    {
        get => Settings.ClientSecret ?? string.Empty;
        set
        {
            Settings.ClientSecret = value;
            SaveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCredentials));
            OnPropertyChanged(nameof(ShowCredentials));
        }
    }

    public string DeviceName
    {
        get => Settings.DeviceName;
        set
        {
            Settings.DeviceName = value;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool AutoUpload
    {
        get => Settings.AutoUpload;
        set
        {
            Settings.AutoUpload = value;
            SaveSettings();
            OnPropertyChanged();
            if (value)
            {
                _sync.ScheduleUpload();
            }
        }
    }

    public bool CheckAtStartup
    {
        get => Settings.CheckAtStartup;
        set
        {
            Settings.CheckAtStartup = value;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool HasCredentials => Service.HasCredentials;

    /// <summary>Expand the credentials section until credentials are entered.</summary>
    public bool ShowCredentials => !HasCredentials;

    public string CredentialsHelp => Strings.Sync_ConsoleHelp;

    // ----- where the copies are kept ---------------------------------------------------------------

    public bool UsesFolder => Service.UsesFolder;

    public bool UsesGoogle => !UsesFolder;

    public string SyncFolder => Settings.SyncFolder ?? string.Empty;

    /// <summary>Picks the folder for the copies (a OneDrive, Dropbox or network folder) and keeps them there from now on.</summary>
    public ICommand ChooseFolderCommand { get; }

    public ICommand UseGoogleCommand { get; }

    /// <summary>Where the folder picker opens the first time: the OneDrive folder when there is one.</summary>
    public static string? SuggestedFolder()
    {
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        return !string.IsNullOrWhiteSpace(oneDrive) ? oneDrive : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    /// <summary>The folder choice, and then the folder's copies listed (tests await it; the command does not).</summary>
    internal async Task ChooseFolderAsync()
    {
        var folder = _dialogs.PickFolder(Strings.Sync_PickFolderTitle, Settings.SyncFolder ?? SuggestedFolder());
        if (folder is null || !ConfirmUnlink(folder))
        {
            NotifyStateChanged(); // the radio button goes back to what is in use
            return;
        }

        await RunAsync(WaitingStatus(), async ct =>
        {
            try
            {
                await Service.UseFolderAsync(folder, ct); // after an upload that is running; the folder is made on a worker
                StatusText = string.Format(Strings.Sync_UsingFolder, Settings.SyncFolder);
            }
            catch (ArgumentException ex)
            {
                StatusText = ex.Message;
            }
        });

        Profiles.Clear();
        NotifyStateChanged();
        if (IsConnected)
        {
            await RefreshAsync();
        }
    }

    internal async Task UseGoogleDriveAsync()
    {
        if (!UsesFolder || !ConfirmUnlink(newFolder: null))
        {
            NotifyStateChanged();
            return;
        }

        await RunAsync(WaitingStatus(), async ct =>
        {
            await Service.UseGoogleDriveAsync(ct);
            StatusText = Strings.Sync_UsingGoogle;
        });

        Profiles.Clear();
        NotifyStateChanged();
        if (IsConnected)
        {
            await RefreshAsync();
        }
    }

    /// <summary>A linked device is unlinked when the copies move: asks first. True to go ahead.</summary>
    private bool ConfirmUnlink(string? newFolder)
    {
        var sameFolder = newFolder is not null && UsesFolder
                         && string.Equals(System.IO.Path.GetFullPath(newFolder), Settings.SyncFolder, StringComparison.OrdinalIgnoreCase);
        return sameFolder
               || Settings.LinkedProfile is not { } linked
               || _dialogs.Confirm(Strings.Sync_SwitchStorageTitle, string.Format(Strings.Sync_SwitchUnlinks, linked));
    }

    // ----- connection -----------------------------------------------------------------------------

    /// <summary>The copies can be reached: a folder is chosen, or the Google account is connected.</summary>
    public bool IsConnected => Service.IsConnected;

    public bool IsGoogleConnected => Service.IsGoogleConnected;

    public bool IsGoogleNotConnected => !IsGoogleConnected;

    /// <summary>The Google account's state (for copies on Google Drive, and for buckets).</summary>
    public string ConnectionText => IsGoogleConnected
        ? string.Format(Strings.Sync_ConnectedAs, Settings.AccountEmail ?? Strings.Common_AGoogleAccount)
        : Strings.Sync_NotConnected;

    public string LinkedText
    {
        get
        {
            if (Settings.LinkedProfile is null)
            {
                return Strings.Sync_NotLinked;
            }

            var synced = Settings.LastSyncedAt is { } t ? string.Format(Strings.Sync_LastSynced, SyncCoordinator.Describe(t)) : string.Empty;
            return string.Format(Strings.Sync_LinkedTo, Settings.LinkedProfile, synced);
        }
    }

    // ----- copies ---------------------------------------------------------------------------------

    public ObservableCollection<RemoteProfile> Profiles { get; } = [];

    public RemoteProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                if (value is not null)
                {
                    NewProfileName = value.Name;
                }

                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string NewProfileName
    {
        get => _newProfileName;
        set
        {
            if (SetProperty(ref _newProfileName, value ?? string.Empty))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    // ----- state ----------------------------------------------------------------------------------

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsIdle => !IsBusy;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string BackgroundStatus => _sync.Status;

    public ICommand ConnectCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand DisconnectCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand SaveAsCommand { get; }

    public ICommand OverwriteCommand { get; }

    public ICommand LoadCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand UnlinkCommand { get; }

    public ICommand OpenConsoleCommand { get; }

    public Task InitializeAsync() => IsConnected ? RefreshAsync() : Task.CompletedTask;

    /// <summary>Accepts the content of a client_secret JSON file downloaded from Google Cloud Console.</summary>
    public void ImportCredentials(string json)
    {
        if (OAuthClientCredentials.TryParseClientSecretJson(json, out var credentials))
        {
            Settings.ClientId = credentials.ClientId;
            Settings.ClientSecret = credentials.ClientSecret;
            SaveSettings();
            OnPropertyChanged(nameof(ClientId));
            OnPropertyChanged(nameof(ClientSecret));
            OnPropertyChanged(nameof(HasCredentials));
            OnPropertyChanged(nameof(ShowCredentials));
            StatusText = Strings.Sync_Imported;
            CommandManager.InvalidateRequerySuggested();
        }
        else
        {
            _dialogs.ShowError(Strings.Sync_ImportTitle, Strings.Sync_ImportInvalid);
        }
    }

    public void CancelPendingOperation() => _operation?.Cancel();

    private Task ConnectAsync() => RunAsync(Strings.Common_FinishSignIn, async ct =>
    {
        var user = await Service.ConnectAsync(ct);
        StatusText = string.Format(Strings.Sync_ConnectedAsDot, user.EmailAddress ?? Strings.Common_AGoogleAccount);
        await RefreshProfilesAsync(ct);
    });

    private Task DisconnectAsync()
    {
        if (!_dialogs.Confirm(Strings.Sync_Disconnect, Strings.Sync_DisconnectQuestion))
        {
            return Task.CompletedTask;
        }

        return RunAsync(Strings.Sync_Disconnecting, async ct =>
        {
            await Service.DisconnectAsync(ct);
            if (Service.IsConnected)
            {
                await RefreshProfilesAsync(ct); // the copies are in a folder: still there
            }
            else
            {
                Profiles.Clear();
                SelectedProfile = null;
            }

            StatusText = Strings.Sync_Disconnected;
        });
    }

    private Task RefreshAsync() => RunAsync(Strings.Sync_LoadingCopies, async ct =>
    {
        await RefreshProfilesAsync(ct);
        StatusText = Profiles.Count switch
        {
            0 => Strings.Sync_NoCopies,
            1 => Strings.Sync_OneCopy,
            _ => string.Format(Strings.Sync_CopyCount, Profiles.Count),
        };
    });

    private Task SaveAsAsync()
    {
        string name;
        try
        {
            name = SyncService.NormalizeProfileName(NewProfileName);
        }
        catch (ArgumentException ex)
        {
            StatusText = ex.Message;
            return Task.CompletedTask;
        }

        var existing = Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null
            && !_dialogs.Confirm(Strings.Sync_ReplaceCopyTitle, string.Format(Strings.Sync_ReplaceExisting, existing.Name, existing.SavedBy ?? Strings.Common_AnotherDevice, SyncCoordinator.Describe(existing.SavedAt))))
        {
            return Task.CompletedTask;
        }

        return SaveToAsync(name, existing?.FileId);
    }

    private Task OverwriteAsync()
    {
        if (SelectedProfile is not { } selected)
        {
            return Task.CompletedTask;
        }

        if (!_dialogs.Confirm(Strings.Sync_ReplaceCopyTitle, string.Format(Strings.Sync_ReplaceSelected, selected.Name)))
        {
            return Task.CompletedTask;
        }

        return SaveToAsync(selected.Name, selected.FileId);
    }

    private Task SaveToAsync(string name, string? fileId) => RunAsync(string.Format(Strings.Sync_Saving, name), async ct =>
    {
        var saved = await Service.SaveProfileAsync(name, _sync.ExportLayout(), fileId, ct);
        await RefreshProfilesAsync(ct);
        SelectedProfile = Profiles.FirstOrDefault(p => p.FileId == saved.FileId);
        StatusText = string.Format(Strings.Sync_SavedLinked, saved.Name);
    });

    private Task LoadAsync()
    {
        if (SelectedProfile is not { } selected)
        {
            return Task.CompletedTask;
        }

        if (!_dialogs.Confirm(
                Strings.Sync_LoadCopyTitle,
                string.Format(Strings.Sync_LoadQuestion, selected.Name, selected.SavedBy ?? Strings.Common_AnotherDevice, SyncCoordinator.Describe(selected.SavedAt))))
        {
            return Task.CompletedTask;
        }

        return RunAsync(string.Format(Strings.Viewer_Loading, selected.Name), async ct =>
        {
            await _sync.LoadAndApplyAsync(selected, ct);
            StatusText = selected.IsConflictCopy
                ? string.Format(Strings.Sync_LoadedConflictCopy, selected.Name, Settings.LinkedProfile)
                : string.Format(Strings.Sync_LoadedLinked, selected.Name);
        });
    }

    private Task DeleteAsync()
    {
        if (SelectedProfile is not { } selected)
        {
            return Task.CompletedTask;
        }

        if (!_dialogs.Confirm(Strings.Sync_DeleteCopyTitle, string.Format(Strings.Sync_DeleteQuestion, selected.Name)))
        {
            return Task.CompletedTask;
        }

        return RunAsync(string.Format(Strings.Sync_Deleting, selected.Name), async ct =>
        {
            await Service.DeleteProfileAsync(selected, ct);
            await RefreshProfilesAsync(ct);
            StatusText = string.Format(Strings.Sync_Deleted, selected.Name);
        });
    }

    private Task UnlinkAsync() => RunAsync(WaitingStatus(), async ct =>
    {
        await Service.UnlinkAsync(ct); // after an upload that is running, which would link this device again
        StatusText = Strings.Sync_Unlinked;
    });

    /// <summary>What the dialog says while it waits for the sync service: an automatic upload may be running.</summary>
    private string WaitingStatus() => _sync.IsUploading ? Strings.Sync_WaitingForUpload : string.Empty;

    private static void OpenCloudConsole()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(CloudConsoleUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser available; the URL is shown in the dialog text anyway.
        }
    }

    private async Task RefreshProfilesAsync(CancellationToken cancellationToken)
    {
        var profiles = await Service.ListProfilesAsync(cancellationToken);
        var selectedId = SelectedProfile?.FileId;
        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.FileId == selectedId)
                          ?? Profiles.FirstOrDefault(p => string.Equals(p.Name, Settings.LinkedProfile, StringComparison.OrdinalIgnoreCase));
    }

    private async Task RunAsync(string status, Func<CancellationToken, Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        _operation = new CancellationTokenSource();
        IsBusy = true;
        StatusText = status;
        try
        {
            await action(_operation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = Strings.Common_Cancelled;
        }
        catch (Exception ex) when (SyncCoordinator.IsSyncError(ex))
        {
            StatusText = ex.Message;
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(UsesFolder));
        OnPropertyChanged(nameof(UsesGoogle));
        OnPropertyChanged(nameof(SyncFolder));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsGoogleConnected));
        OnPropertyChanged(nameof(IsGoogleNotConnected));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(LinkedText));
        OnPropertyChanged(nameof(HasCredentials));
        OnPropertyChanged(nameof(ShowCredentials));
        CommandManager.InvalidateRequerySuggested();
    }

    private void SaveSettings()
    {
        try
        {
            Service.SaveSettings();
        }
        catch (Exception ex) when (SyncCoordinator.IsSyncError(ex))
        {
            StatusText = string.Format(Strings.Sync_SaveFailed, ex.Message);
        }
    }
}
