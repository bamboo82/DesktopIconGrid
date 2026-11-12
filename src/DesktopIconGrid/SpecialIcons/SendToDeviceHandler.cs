using System.IO;
using System.Windows;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.SpecialIcons;

/// <summary>
/// "Send to PC": drop files on it (or paste them) and they go to another PC that shares the sync folder, which puts
/// them in its Downloads folder (<see cref="InboxService"/>). Only with a sync folder.
/// </summary>
public sealed class SendToDeviceHandler(Func<IInbox?> inbox, IDialogService dialogs, Func<Window?> ownerProvider, string pasteDirectory, CommandTrust trust) : ISpecialIconHandler
{
    public const string DeviceIdKey = "deviceId";
    public const string DeviceNameKey = "deviceName";

    public string Kind => SpecialIconKinds.SendToDevice;

    public string DisplayName => Strings.SendTo_Name;

    public string Description => Strings.SendTo_Desc;

    public SpecialIconItem CreateItem() => new() { Name = Strings.SendTo_Name, Kind = Kind };

    public GaugeState? CreateState(SpecialIconItem item) => null;

    public string? GetToolTip(SpecialIconItem item) =>
        item.GetSetting(DeviceIdKey) is { } id ? string.Format(Strings.SendTo_ToolTip, StoredName(item, id)) : Strings.SendTo_NoDevice;

    /// <summary>A click sends nothing: it says what the icon is for (a hint, not a failure).</summary>
    public LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers)
    {
        dialogs.Notify(item.Name, item.GetSetting(DeviceIdKey) is { } id ? string.Format(Strings.SendTo_ToolTip, StoredName(item, id)) : Strings.SendTo_NoDevice);
        return LaunchResult.Started;
    }

    /// <summary>The name kept with the icon, made fit to show: it may have come with a synced bar (review 24 I4).</summary>
    private static string StoredName(SpecialIconItem item, string id) =>
        FolderInbox.ShownName(item.GetSetting(DeviceNameKey), id.Length >= 8 ? id : "????????");

    public bool AcceptsFileDrops(SpecialIconItem item) => item.GetSetting(DeviceIdKey) is not null;

    public bool AcceptsPaste(SpecialIconItem item) => AcceptsFileDrops(item);

    public Task<LaunchResult> PasteAsync(SpecialIconItem item)
    {
        var files = ClipboardFiles.Take(
            pasteDirectory,
            DateTime.Now,
            text => dialogs.ConfirmDefaultNo(Strings.Paste_TextTitle, ClipboardFiles.TextQuestion(Strings.Paste_TextQuestion, item.Name, text)));
        if (files is null)
        {
            return Task.FromResult(LaunchResult.Cancelled);
        }

        return files.Count == 0 ? Task.FromResult(LaunchResult.Failure(Strings.Bucket_NothingToPaste)) : SendPastedAsync(item, files);
    }

    /// <summary>Sends what was pasted, then removes the temporary picture or text file it was saved as (review 24 P2).</summary>
    private async Task<LaunchResult> SendPastedAsync(SpecialIconItem item, IReadOnlyList<string> files)
    {
        try
        {
            return await DropFilesAsync(item, files);
        }
        finally
        {
            foreach (var file in files.Where(file => file.StartsWith(pasteDirectory, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The start-up purge takes it.
                }
            }
        }
    }

    public async Task<LaunchResult> DropFilesAsync(SpecialIconItem item, IReadOnlyList<string> paths)
    {
        if (item.GetSetting(DeviceIdKey) is not { } target)
        {
            return LaunchResult.Failure(Strings.SendTo_NoDevice);
        }

        if (inbox() is not { } mine)
        {
            return LaunchResult.Failure(Strings.SendTo_NeedsFolder);
        }

        var stored = item.GetSetting(DeviceNameKey);
        var (sent, problems, name) = await Task.Run(async () =>
        {
            var result = await SendAsync(mine, target, paths).ConfigureAwait(false);
            string shown;
            try
            {
                shown = await mine.NameOfDeviceAsync(target, stored, CancellationToken.None).ConfigureAwait(false); // the PC's own name as it is now
            }
            catch (Exception ex) when (ex is IOException or DriveException or System.Net.Http.HttpRequestException)
            {
                shown = FolderInbox.ShownName(stored, target);
            }

            return (result.Sent, result.Problems, shown);
        });
        if (sent > 0)
        {
            dialogs.Notify(item.Name, string.Format(Strings.SendTo_Sent, sent, name));
        }

        return problems.Count == 0 ? LaunchResult.Started : LaunchResult.Failure(string.Join("\n", problems));
    }

    internal static async Task<(int Sent, List<string> Problems)> SendAsync(IInbox mine, string target, IReadOnlyList<string> paths)
    {
        var sent = 0;
        var problems = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                await mine.SendAsync(target, path, CancellationToken.None).ConfigureAwait(false);
                sent++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DriveException or System.Net.Http.HttpRequestException)
            {
                // The reasons this app knows, in the user's language; anything else as Windows says it.
                var reason = ex is InboxSendException known
                    ? known.Problem switch
                    {
                        SendProblem.NoInbox => Strings.SendTo_ProblemNoInbox,
                        SendProblem.NotAFile => Strings.SendTo_ProblemNotAFile,
                        SendProblem.TooLarge => Strings.SendTo_ProblemTooLarge,
                        _ => Strings.SendTo_ProblemNotAnotherPc,
                    }
                    : ex.Message;
                problems.Add($"{Path.GetFileName(path.TrimEnd('\\'))}: {reason}");
                AppLog.Default.Warning("Inbox", $"\"{path}\" was not sent: {ex.Message}");
            }
        }

        return (sent, problems);
    }

    public bool ShowProperties(SpecialIconItem item, bool isNew)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (inbox() is not { } mine)
        {
            dialogs.Notify(DisplayName, Strings.SendTo_NeedsFolder);
            return false;
        }

        // This PC takes part from the moment it gets the icon, so the other PCs can pick it too; then the list of the
        // others. Through Drive these are network calls: off the UI thread, and given up after a while.
        IReadOnlyList<InboxDevice> devices = [];
        var listing = Task.Run(async () =>
        {
            await mine.AnnounceAsync(CancellationToken.None).ConfigureAwait(false);
            return await mine.DevicesAsync(CancellationToken.None).ConfigureAwait(false);
        });
        try
        {
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            if (listing.Wait(TimeSpan.FromSeconds(20)))
            {
                devices = listing.Result;
            }
            else
            {
                AppLog.Default.Warning("Inbox", "The other PCs could not be listed in time");
            }
        }
        catch (AggregateException ex) when (ex.InnerException is IOException or UnauthorizedAccessException or DriveException or System.Net.Http.HttpRequestException or InvalidOperationException)
        {
            AppLog.Default.Warning("Inbox", $"The other PCs could not be listed: {ex.InnerException.Message}");
        }
        finally
        {
            System.Windows.Input.Mouse.OverrideCursor = null;
        }

        var viewModel = new SendToDeviceViewModel(item, devices);
        var window = new SendToDeviceWindow { DataContext = viewModel, Owner = ownerProvider() };
        if (window.ShowDialog() != true)
        {
            return false;
        }

        viewModel.Apply(item);
        trust.Approve(FolderInbox.IconMark(item.Id)); // saved on this PC: this icon turns receiving on here, a synced copy elsewhere does not
        return true;
    }
}
