using System.IO;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Services;

/// <summary>
/// The background picture file coming through sync on this PC (0.15.0 WP5b): which pictures are being fetched now and
/// which could not be, for Settings' thumbnail and for the start's pruning, and the fetch itself, one at a time per picture
/// and never in a loop (the next sync check tries again).
/// </summary>
public static class PictureTransfers
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> Fetching = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Failed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised with the picture's stored name when a fetch of it starts or ends, on the thread that did it.</summary>
    public static event EventHandler<string>? StateChanged;

    /// <summary>A fetch of the picture <paramref name="storedName"/> is under way.</summary>
    public static bool IsFetching(string storedName)
    {
        lock (Gate)
        {
            return Fetching.Contains(storedName);
        }
    }

    /// <summary>The last fetch of the picture <paramref name="storedName"/> did not bring it.</summary>
    public static bool HasFailed(string storedName)
    {
        lock (Gate)
        {
            return Failed.Contains(storedName);
        }
    }

    /// <summary>
    /// Fetches <paramref name="storedName"/> into <paramref name="folder"/> when this PC lacks it and no fetch of it is under
    /// way; true when the file is there afterwards (the caller then lets the bar draw it). Not there or refused: one line in
    /// the log, no message box.
    /// </summary>
    public static async Task<bool> EnsureAsync(SyncService service, string? storedName, string folder, Func<byte[], bool, bool> decodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (!BackgroundImage.IsStoredName(storedName))
        {
            return false;
        }

        if (File.Exists(Path.Combine(folder, storedName!)))
        {
            return true;
        }

        lock (Gate)
        {
            if (!Fetching.Add(storedName!))
            {
                return false;
            }

            Failed.Remove(storedName!);
        }

        StateChanged?.Invoke(null, storedName!);
        var fetched = false;
        try
        {
            var outcome = await service.FetchPictureAsync(storedName!, folder, decodes, cancellationToken);
            fetched = outcome == PictureFetch.Fetched;
            if (!fetched)
            {
                AppLog.Default.Info("Sync", $"Background picture {storedName} not taken from the sync place: {outcome}; the colour shows");
            }
        }
        catch (Exception ex) when (SyncCoordinator.IsSyncError(ex) || ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Info("Sync", $"Background picture {storedName} could not be fetched: {ex.Message}; the colour shows");
        }
        finally
        {
            lock (Gate)
            {
                Fetching.Remove(storedName!);
                if (!fetched)
                {
                    Failed.Add(storedName!);
                }
            }

            StateChanged?.Invoke(null, storedName!);
        }

        return fetched;
    }

    /// <summary>Forgets every state (tests).</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            Fetching.Clear();
            Failed.Clear();
        }
    }

    /// <summary>Marks a fetch as under way without one (tests of what waits for it).</summary>
    internal static void MarkFetching(string storedName, bool fetching)
    {
        lock (Gate)
        {
            if (fetching)
            {
                Fetching.Add(storedName);
            }
            else
            {
                Fetching.Remove(storedName);
            }
        }
    }
}
