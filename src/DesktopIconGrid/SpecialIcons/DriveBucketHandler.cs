using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.SpecialIcons;

/// <summary>
/// A Google Drive folder on the bar: drop files to upload them, click to open the folder in the browser,
/// Ctrl+click (or <em>Browse…</em>) to open the Bucket Viewer and copy files to this PC.
/// </summary>
public sealed class DriveBucketHandler : ISpecialIconHandler
{
    public const string FolderIdKey = "folderId";
    public const string FolderNameKey = "folderName";
    public const string ConflictKey = "onNameConflict";

    public const string KeepBoth = "keep-both";
    public const string Replace = "replace";
    public const string Skip = "skip";

    private readonly SyncService _sync;
    private readonly IDialogService _dialogs;
    private readonly Func<Window?> _owner;
    private readonly IIconProvider _icons;
    private readonly string _cacheDirectory;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<Guid, DriveBucketUploader> _uploaders = [];

    public DriveBucketHandler(SyncService sync, IDialogService dialogs, Func<Window?> owner, IIconProvider icons, string cacheDirectory, Dispatcher dispatcher)
    {
        _sync = sync;
        _dialogs = dialogs;
        _owner = owner;
        _icons = icons;
        _cacheDirectory = cacheDirectory;
        _dispatcher = dispatcher;
    }

    public string Kind => SpecialIconKinds.GoogleDriveBucket;

    public string DisplayName => "Google Drive Bucket";

    public string Description =>
        "A folder on Google Drive. Drop files on it to upload them; click to open the folder in your browser; " +
        "Ctrl+click to browse it and copy files to this PC.";

    public SpecialIconItem CreateItem()
    {
        var item = new SpecialIconItem { Name = "Drive Bucket", Kind = Kind };
        item.SetSetting(ConflictKey, KeepBoth);
        return item;
    }

    public GaugeState CreateState(SpecialIconItem item)
    {
        var state = new GaugeState("↑", GaugeStyle.Ring, warningAt: 101, criticalAt: 101, string.Empty) { IsVisible = false };
        if (_uploaders.TryGetValue(item.Id, out var uploader))
        {
            uploader.AttachGauge(state);
        }

        return state;
    }

    public ImageSource? GetIcon(SpecialIconItem item) =>
        Application.Current?.TryFindResource("DriveBucketIconImage") as ImageSource;

    public string? GetToolTip(SpecialIconItem item)
    {
        var folder = item.GetSetting(FolderNameKey) ?? item.GetSetting(FolderIdKey);
        return folder is null
            ? "No folder chosen yet — open Properties."
            : $"Google Drive folder: {folder}\nDrop files to upload • Click to open in browser • Ctrl+click to browse";
    }

    public LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers)
    {
        var folderId = item.GetSetting(FolderIdKey);
        if (folderId is null)
        {
            return LaunchResult.Failure("This bucket has no Google Drive folder yet. Open Properties to choose one.");
        }

        if (modifiers.HasFlag(ActivationModifiers.Control))
        {
            _ = OpenViewerAsync(item);
            return LaunchResult.Started;
        }

        return OpenUrl($"https://drive.google.com/drive/folders/{Uri.EscapeDataString(folderId)}");
    }

    public bool ShowProperties(SpecialIconItem item, bool isNew)
    {
        var viewModel = new DriveBucketViewModel(item, _sync);
        var window = new DriveBucketWindow { DataContext = viewModel, Owner = _owner() };
        if (window.ShowDialog() != true)
        {
            return false;
        }

        viewModel.Apply(item);
        return true;
    }

    public bool AcceptsFileDrops(SpecialIconItem item) => item.GetSetting(FolderIdKey) is not null;

    public async Task<LaunchResult> DropFilesAsync(SpecialIconItem item, IReadOnlyList<string> paths)
    {
        var folderId = item.GetSetting(FolderIdKey);
        if (folderId is null)
        {
            return LaunchResult.Failure("This bucket has no Google Drive folder yet. Open Properties to choose one.");
        }

        if (!await EnsureAccessAsync())
        {
            return LaunchResult.Cancelled;
        }

        GetUploader(item).Enqueue(folderId, item.GetSetting(ConflictKey) ?? KeepBoth, paths);
        return LaunchResult.Started;
    }

    public IReadOnlyList<SpecialIconAction> GetActions(SpecialIconItem item)
    {
        var hasFolder = item.GetSetting(FolderIdKey) is not null;
        var uploader = _uploaders.GetValueOrDefault(item.Id);
        return
        [
            new SpecialIconAction("Open in browser", () => Activate(item, ActivationModifiers.None), hasFolder),
            new SpecialIconAction("Browse…", () => _ = OpenViewerAsync(item), hasFolder),
            new SpecialIconAction("Cancel uploads", () => uploader?.Cancel(), uploader?.IsBusy == true),
        ];
    }

    private DriveBucketUploader GetUploader(SpecialIconItem item)
    {
        if (!_uploaders.TryGetValue(item.Id, out var uploader))
        {
            uploader = new DriveBucketUploader(_sync, (title, message) => _dialogs.Notify(title, message));
            _uploaders[item.Id] = uploader;
        }

        return uploader;
    }

    private async Task OpenViewerAsync(SpecialIconItem item)
    {
        var folderId = item.GetSetting(FolderIdKey);
        if (folderId is null || !await EnsureAccessAsync())
        {
            return;
        }

        var viewModel = new DriveViewerViewModel(
            _sync,
            _icons,
            new DriveCrumb(folderId, item.GetSetting(FolderNameKey) ?? item.Name),
            GetUploader(item),
            item.GetSetting(ConflictKey) ?? KeepBoth,
            _cacheDirectory);
        var window = new DriveViewerWindow { DataContext = viewModel, Owner = _owner() };
        window.Show();
    }

    /// <summary>Makes sure the account is connected with full Drive access, asking first. False when the user declined or it failed.</summary>
    private async Task<bool> EnsureAccessAsync()
    {
        if (_sync.Files is null)
        {
            _dialogs.Notify("Google Drive", "Google Drive access is not available in this build.");
            return false;
        }

        if (_sync.HasFullDriveAccess)
        {
            return true;
        }

        if (!_sync.HasCredentials)
        {
            _dialogs.ShowInfo(
                "Google Drive Bucket",
                "Set up the Google API credentials first: open the options menu (▸), choose \"Sync with Google Drive…\" and enter or import the client ID and secret.");
            return false;
        }

        var message = _sync.IsConnected
            ? "Google Drive Buckets need access to your whole Google Drive, which the current sign-in does not include.\n\nSign in again in your browser and grant it now?"
            : "Google Drive Buckets need access to your Google Drive.\n\nSign in in your browser now?";
        if (!_dialogs.Confirm("Google Drive access", message))
        {
            return false;
        }

        try
        {
            await _sync.EnsureFullDriveAccessAsync(CancellationToken.None);
            return true;
        }
        catch (Exception ex) when (SyncCoordinator.IsSyncError(ex))
        {
            _dialogs.Notify("Google Drive access", ex.Message);
            return false;
        }
    }

    private static LaunchResult OpenUrl(string url)
    {
        var result = ProcessLauncher.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        result.Process?.Dispose();
        return result;
    }
}
