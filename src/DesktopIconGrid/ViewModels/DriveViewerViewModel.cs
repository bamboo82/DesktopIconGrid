using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;

namespace DesktopIconGrid.ViewModels;

public sealed record DriveCrumb(string Id, string Name);

/// <summary>One row of the Bucket Viewer.</summary>
public sealed class DriveEntryViewModel(DriveEntry entry, ImageSource? icon)
{
    public DriveEntry Entry { get; } = entry;

    public string Name => Entry.Name;

    public bool IsFolder => Entry.IsFolder;

    public string KindText => Entry.KindText;

    public string SizeText => Entry.Size is { } size && !Entry.IsFolder ? SystemMonitor.FormatBytes(size) : string.Empty;

    public string ModifiedText => Entry.ModifiedTime is { } time ? time.LocalDateTime.ToString("g", CultureInfo.CurrentCulture) : string.Empty;

    public ImageSource? Icon { get; } = icon;
}

/// <summary>Backs the Bucket Viewer: browses a Drive folder, downloads files and feeds the drag-out.</summary>
public sealed class DriveViewerViewModel : ObservableObject
{
    private readonly SyncService _sync;
    private readonly IIconProvider _icons;
    private readonly DriveBucketUploader? _uploader;
    private readonly string _conflictPolicy;
    private readonly string _cacheDirectory;
    private readonly DriveDownloader _downloader;
    private readonly Stack<DriveCrumb> _history = new();
    private CancellationTokenSource? _transfer;
    private string _statusText = string.Empty;
    private bool _isBusy;
    private double? _transferPercent;

    public DriveViewerViewModel(SyncService sync, IIconProvider icons, DriveCrumb root, DriveBucketUploader? uploader, string conflictPolicy, string cacheDirectory)
    {
        _sync = sync;
        _icons = icons;
        _uploader = uploader;
        _conflictPolicy = conflictPolicy;
        _cacheDirectory = cacheDirectory;
        _downloader = new DriveDownloader(sync.Files ?? throw new DriveException(Strings.Upload_NotAvailable));
        Root = root;
        Crumbs.Add(root);

        BackCommand = new RelayCommand(() => _ = GoBackAsync(), () => !IsBusy && _history.Count > 0);
        UpCommand = new RelayCommand(() => _ = GoUpAsync(), () => !IsBusy && Crumbs.Count > 1);
        RefreshCommand = new RelayCommand(() => _ = NavigateAsync(Current, pushHistory: false), () => !IsBusy);
        OpenInBrowserCommand = new RelayCommand(() => OpenUrl($"https://drive.google.com/drive/folders/{Uri.EscapeDataString(Current.Id)}"));
        CancelCommand = new RelayCommand(() => _transfer?.Cancel(), () => IsBusy);

        if (_uploader is not null)
        {
            _uploader.Completed += (_, folderId) =>
            {
                if (string.Equals(folderId, Current.Id, StringComparison.Ordinal) && !IsBusy)
                {
                    _ = NavigateAsync(Current, pushHistory: false);
                }
            };
        }
    }

    public DriveCrumb Root { get; }

    public string Title => string.Format(Strings.Viewer_Title, Root.Name);

    public ObservableCollection<DriveCrumb> Crumbs { get; } = [];

    public ObservableCollection<DriveEntryViewModel> Entries { get; } = [];

    public DriveCrumb Current => Crumbs[^1];

    public bool CanUpload => _uploader is not null;

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
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>0–100 while a transfer with a known size runs; null otherwise.</summary>
    public double? TransferPercent
    {
        get => _transferPercent;
        private set
        {
            if (SetProperty(ref _transferPercent, value))
            {
                OnPropertyChanged(nameof(HasTransferPercent));
            }
        }
    }

    public bool HasTransferPercent => _transferPercent is not null;

    public ICommand BackCommand { get; }

    public ICommand UpCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand OpenInBrowserCommand { get; }

    public ICommand CancelCommand { get; }

    public Task InitializeAsync() => NavigateAsync(Root, pushHistory: false);

    public async Task NavigateToCrumbAsync(DriveCrumb crumb)
    {
        var index = Crumbs.IndexOf(crumb);
        if (index < 0 || index == Crumbs.Count - 1)
        {
            return;
        }

        _history.Push(Current);
        while (Crumbs.Count > index + 1)
        {
            Crumbs.RemoveAt(Crumbs.Count - 1);
        }

        await NavigateAsync(crumb, pushHistory: false);
    }

    public async Task OpenEntryAsync(DriveEntryViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var entry = row.Entry;
        if (entry.IsFolder)
        {
            _history.Push(Current);
            Crumbs.Add(new DriveCrumb(entry.Id, entry.Name));
            await NavigateAsync(Current, pushHistory: false);
            return;
        }

        if (!entry.CanDownload)
        {
            OpenUrl(entry.WebUrl);
            return;
        }

        var paths = await PrepareLocalCopiesAsync([row]);
        if (paths.Count == 1)
        {
            if (DriveDownloader.IsExecutable(paths[0]))
            {
                // Never run programs pulled from a shared folder on a double-click; show them in Explorer instead.
                StatusText = string.Format(Strings.Viewer_IsProgram, row.Name);
                var reveal = ShellHelper.RevealInExplorer(paths[0]);
                if (reveal.Outcome == LaunchOutcome.Failed)
                {
                    StatusText = reveal.ErrorMessage ?? StatusText;
                }

                return;
            }

            var result = ProcessLauncher.Start(new ProcessStartInfo(paths[0]) { UseShellExecute = true });
            result.Process?.Dispose();
            if (result.Outcome == LaunchOutcome.Failed)
            {
                StatusText = result.ErrorMessage ?? Strings.Viewer_CannotOpen;
            }
        }
    }

    /// <summary>Downloads the rows into the cache and returns their local paths (for drag-out and opening).</summary>
    public async Task<IReadOnlyList<string>> PrepareLocalCopiesAsync(IReadOnlyList<DriveEntryViewModel> rows)
    {
        var paths = new List<string>();
        await RunTransferAsync(Strings.Viewer_Preparing, async (progress, token) =>
        {
            foreach (var row in rows)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var path = await _downloader.DownloadToCacheAsync(row.Entry, _cacheDirectory, progress, token);
                    if (path is not null)
                    {
                        paths.Add(path);
                        AppLog.Default.Info("Bucket", $"Downloaded \"{row.Name}\" to the cache for drag-out/open");
                    }
                }
                catch (Exception ex) when (SyncCoordinator.IsSyncError(ex) && ex is not OperationCanceledException)
                {
                    StatusText = $"{row.Name}: {ex.Message}";
                }
            }
        }, paths.Count == 0 ? Strings.Viewer_NothingToCopy : string.Format(Strings.Viewer_ItemsReady, paths.Count));
        return paths;
    }

    public async Task DownloadToAsync(string destination, IReadOnlyList<DriveEntryViewModel> rows)
    {
        var done = 0;
        await RunTransferAsync(Strings.Viewer_Downloading, async (progress, token) =>
        {
            foreach (var row in rows)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (await _downloader.DownloadEntryAsync(row.Entry, destination, overwrite: false, progress, token) is { } saved)
                    {
                        done++;
                        AppLog.Default.Info("Bucket", $"Downloaded \"{row.Name}\" to {saved}");
                    }
                }
                catch (Exception ex) when (SyncCoordinator.IsSyncError(ex) && ex is not OperationCanceledException)
                {
                    StatusText = $"{row.Name}: {ex.Message}";
                    AppLog.Default.Error("Bucket", $"Download of \"{row.Name}\" failed", ex);
                }
            }
        }, string.Format(Strings.Viewer_Downloaded, done, destination));
    }

    /// <summary>Uploads dropped files into the folder currently shown (through the bucket's uploader).</summary>
    public void Upload(IReadOnlyList<string> paths)
    {
        if (_uploader is null)
        {
            StatusText = Strings.Viewer_UploadsUnavailable;
            return;
        }

        _uploader.Enqueue(Current.Id, _conflictPolicy, paths);
        StatusText = string.Format(Strings.Viewer_Uploading, paths.Count, Current.Name);
    }

    private async Task GoBackAsync()
    {
        if (_history.Count == 0)
        {
            return;
        }

        var previous = _history.Pop();
        var index = Crumbs.IndexOf(previous);
        if (index >= 0)
        {
            while (Crumbs.Count > index + 1)
            {
                Crumbs.RemoveAt(Crumbs.Count - 1);
            }
        }
        else
        {
            Crumbs.Add(previous);
        }

        await NavigateAsync(previous, pushHistory: false);
    }

    private Task GoUpAsync() => Crumbs.Count > 1 ? NavigateToCrumbAsync(Crumbs[^2]) : Task.CompletedTask;

    private async Task NavigateAsync(DriveCrumb crumb, bool pushHistory)
    {
        if (pushHistory)
        {
            _history.Push(Current);
        }

        if (_sync.Files is null)
        {
            StatusText = Strings.Upload_NotAvailable;
            return;
        }

        IsBusy = true;
        StatusText = string.Format(Strings.Viewer_Loading, crumb.Name);
        try
        {
            var children = await _sync.Files.ListChildrenAsync(crumb.Id, CancellationToken.None);
            Entries.Clear();
            foreach (var child in children)
            {
                Entries.Add(new DriveEntryViewModel(child, IconFor(child)));
            }

            var folders = children.Count(c => c.IsFolder);
            StatusText = string.Format(Strings.Viewer_Counts, folders, children.Count - folders);
        }
        catch (Exception ex) when (SyncCoordinator.IsSyncError(ex))
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(Current));
            OnPropertyChanged(nameof(Crumbs));
        }
    }

    private async Task RunTransferAsync(string status, Func<IProgress<DriveTransferProgress>, CancellationToken, Task> work, string doneStatus)
    {
        if (IsBusy)
        {
            StatusText = Strings.Viewer_TransferRunning;
            return;
        }

        _transfer = new CancellationTokenSource();
        IsBusy = true;
        StatusText = status;
        var progress = new Progress<DriveTransferProgress>(p =>
        {
            TransferPercent = p.Total is > 0 ? Math.Min(100, p.Bytes * 100.0 / p.Total.Value) : null;
            StatusText = p.Total is > 0
                ? string.Format(Strings.Viewer_ProgressOf, p.FileName, SystemMonitor.FormatBytes(p.Bytes), SystemMonitor.FormatBytes(p.Total.Value))
                : string.Format(Strings.Viewer_Progress, p.FileName, SystemMonitor.FormatBytes(p.Bytes));
        });

        try
        {
            await work(progress, _transfer.Token);
            StatusText = doneStatus;
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
            TransferPercent = null;
            IsBusy = false;
            _transfer.Dispose();
            _transfer = null;
        }
    }

    private ImageSource? IconFor(DriveEntry entry)
    {
        if (entry.IsFolder)
        {
            return _icons.GetFolderIcon(16);
        }

        var name = entry.IsGoogleFormat ? "document" + (entry.Export?.Extension ?? ".gdoc") : entry.Name;
        var extension = System.IO.Path.GetExtension(name);
        return extension.Length > 1 ? _icons.GetIcon("file" + extension, 16) : null;
    }

    private static void OpenUrl(string url)
    {
        var result = ProcessLauncher.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        result.Process?.Dispose();
    }
}
