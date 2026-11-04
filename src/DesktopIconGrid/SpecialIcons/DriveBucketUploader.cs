using System.IO;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.SpecialIcons;

/// <summary>
/// Uploads dropped files and folders into a bucket's Drive folder, one after the other, showing progress
/// on the bucket's gauge. Runs on the UI thread's synchronization context so the gauge can be updated directly.
/// </summary>
public sealed class DriveBucketUploader(SyncService sync, Action<string, string> showError)
{
    private readonly Queue<UploadJob> _pending = new();
    private readonly List<string> _failures = [];
    private readonly Dictionary<string, List<DriveEntry>> _listings = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cancellation;
    private GaugeState? _gauge;
    private int _total;
    private int _done;

    private sealed record UploadJob(string FolderId, string Policy, string Path);

    public bool IsBusy { get; private set; }

    /// <summary>Raised on the UI thread when the queue has been worked off; the argument is the Drive folder written to last.</summary>
    public event EventHandler<string>? Completed;

    public void AttachGauge(GaugeState gauge)
    {
        _gauge = gauge;
        gauge.IsVisible = IsBusy;
    }

    public void Enqueue(string folderId, string policy, IEnumerable<string> paths)
    {
        var queued = 0;
        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            _pending.Enqueue(new UploadJob(folderId, policy, path));
            _total++;
            queued++;
        }

        AppLog.Default.Info("Bucket", $"Queued {queued} item(s) for upload to Drive folder {folderId} (policy: {policy})");

        if (!IsBusy && _pending.Count > 0)
        {
            _ = RunAsync();
        }
    }

    public void Cancel() => _cancellation?.Cancel();

    private async Task RunAsync()
    {
        IsBusy = true;
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        _done = 0;
        _failures.Clear();
        _listings.Clear();
        var lastFolder = string.Empty;
        ShowGauge(0, "Preparing upload…");

        try
        {
            while (_pending.TryDequeue(out var job))
            {
                token.ThrowIfCancellationRequested();
                lastFolder = job.FolderId;
                try
                {
                    await UploadPathAsync(job, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (SyncCoordinator.IsSyncError(ex))
                {
                    _failures.Add($"{Path.GetFileName(job.Path.TrimEnd(Path.DirectorySeparatorChar))}: {ex.Message}");
                    AppLog.Default.Error("Bucket", $"Upload of {job.Path} failed", ex);
                }

                _done++;
            }
        }
        catch (OperationCanceledException)
        {
            _pending.Clear();
            if (token.IsCancellationRequested)
            {
                AppLog.Default.Warning("Bucket", "Uploads cancelled");
            }
            else
            {
                _failures.Add("The upload timed out.");
                AppLog.Default.Error("Bucket", "Uploads stopped: a request timed out");
            }
        }
        finally
        {
            IsBusy = false;
            _cancellation.Dispose();
            _cancellation = null;
            _total = 0;
            HideGauge();

            var failures = _failures.ToList();
            AppLog.Default.Info("Bucket", $"Upload run finished: {_done} item(s) processed, {failures.Count} failed");
            if (failures.Count > 0)
            {
                showError("Some files were not uploaded", string.Join("\n", failures));
            }

            Completed?.Invoke(this, lastFolder);
        }
    }

    private async Task UploadPathAsync(UploadJob job, CancellationToken cancellationToken)
    {
        var files = sync.Files ?? throw new InvalidOperationException("Google Drive access is not available.");
        if (Directory.Exists(job.Path))
        {
            await UploadDirectoryAsync(files, job.FolderId, job.Path, job.Policy, cancellationToken);
            return;
        }

        if (!File.Exists(job.Path))
        {
            throw new FileNotFoundException("The file no longer exists.", job.Path);
        }

        await UploadFileAsync(files, job.FolderId, job.Path, job.Policy, cancellationToken);
    }

    private async Task UploadDirectoryAsync(IDriveFiles files, string parentId, string directory, string policy, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name))
        {
            name = directory;
        }

        var siblings = await ListingAsync(files, parentId, cancellationToken);
        var target = siblings.FirstOrDefault(e => e.IsFolder && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            target = await files.CreateFolderAsync(parentId, name, cancellationToken);
            siblings.Add(target);
            _listings[target.Id] = []; // created just now, so it is empty: no listing needed
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await UploadFileAsync(files, target.Id, file, policy, cancellationToken);
            }
            catch (Exception ex) when (SyncCoordinator.IsSyncError(ex) && ex is not OperationCanceledException)
            {
                _failures.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        foreach (var subdirectory in Directory.EnumerateDirectories(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await UploadDirectoryAsync(files, target.Id, subdirectory, policy, cancellationToken);
        }
    }

    private async Task UploadFileAsync(IDriveFiles files, string parentId, string path, string policy, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(path);
        string? existingId = null;
        List<DriveEntry>? siblings = null;
        if (!string.Equals(policy, DriveBucketHandler.KeepBoth, StringComparison.OrdinalIgnoreCase))
        {
            siblings = await ListingAsync(files, parentId, cancellationToken);
            var match = siblings.FirstOrDefault(e => !e.IsFolder && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                if (string.Equals(policy, DriveBucketHandler.Skip, StringComparison.OrdinalIgnoreCase))
                {
                    ShowGauge(100, $"Skipped {name} (already on Drive)");
                    AppLog.Default.Info("Bucket", $"Skipped {name}: a file with that name is already in the folder");
                    return;
                }

                existingId = match.Id;
            }
        }

        var length = new FileInfo(path).Length;
        var label = $"Uploading {name} ({_done + 1} of {Math.Max(_total, _done + 1)})";
        ShowGauge(0, label);
        var progress = new Progress<long>(sent => ShowGauge(length == 0 ? 100 : sent * 100.0 / length, label));

        await using var stream = File.OpenRead(path);
        var uploaded = await files.UploadAsync(parentId, name, MimeTypes.FromFileName(name), stream, length, existingId, progress, cancellationToken);
        if (siblings is not null && existingId is null)
        {
            siblings.Add(uploaded); // keep the cached listing current for the rest of this run
        }

        AppLog.Default.Info("Bucket", $"{(existingId is null ? "Uploaded" : "Replaced")} {name} ({length:N0} bytes)");
    }

    /// <summary>The folder's entries, listed once per upload run (not once per file) and kept current as files are added.</summary>
    private async Task<List<DriveEntry>> ListingAsync(IDriveFiles files, string folderId, CancellationToken cancellationToken)
    {
        if (!_listings.TryGetValue(folderId, out var listing))
        {
            listing = (await files.ListChildrenAsync(folderId, cancellationToken)).ToList();
            _listings[folderId] = listing;
        }

        return listing;
    }

    private void ShowGauge(double percent, string detail)
    {
        if (_gauge is null)
        {
            return;
        }

        _gauge.IsVisible = true;
        _gauge.Update(percent, detail);
    }

    private void HideGauge()
    {
        if (_gauge is null)
        {
            return;
        }

        _gauge.IsVisible = false;
        _gauge.Update(null, string.Empty);
    }
}
