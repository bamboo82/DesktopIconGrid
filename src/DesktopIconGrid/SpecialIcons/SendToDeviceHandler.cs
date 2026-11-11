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
public sealed class SendToDeviceHandler(Func<FolderInbox?> inbox, IDialogService dialogs, Func<Window?> ownerProvider, string pasteDirectory, CommandTrust trust) : ISpecialIconHandler
{
    public const string DeviceIdKey = "deviceId";
    public const string DeviceNameKey = "deviceName";

    public string Kind => SpecialIconKinds.SendToDevice;

    public string DisplayName => Strings.SendTo_Name;

    public string Description => Strings.SendTo_Desc;

    public SpecialIconItem CreateItem() => new() { Name = Strings.SendTo_Name, Kind = Kind };

    public GaugeState? CreateState(SpecialIconItem item) => null;

    public string? GetToolTip(SpecialIconItem item) =>
        item.GetSetting(DeviceNameKey) is { } name ? string.Format(Strings.SendTo_ToolTip, name) : Strings.SendTo_NoDevice;

    public LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers) =>
        LaunchResult.Failure(item.GetSetting(DeviceIdKey) is null ? Strings.SendTo_NoDevice : string.Format(Strings.SendTo_ToolTip, item.GetSetting(DeviceNameKey)));

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

        var (sent, problems) = await Task.Run(() => Send(mine, target, paths));
        if (sent > 0)
        {
            dialogs.Notify(item.Name, string.Format(Strings.SendTo_Sent, sent, item.GetSetting(DeviceNameKey) ?? target[..8]));
        }

        return problems.Count == 0 ? LaunchResult.Started : LaunchResult.Failure(string.Join("\n", problems));
    }

    internal static (int Sent, List<string> Problems) Send(FolderInbox mine, string target, IReadOnlyList<string> paths)
    {
        var sent = 0;
        var problems = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                mine.Send(target, path);
                sent++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{Path.GetFileName(path.TrimEnd('\\'))}: {ex.Message}");
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

        try
        {
            mine.Announce(); // this PC takes part from the moment it gets the icon: the other PCs can then pick it too
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Warning("Inbox", $"This PC could not be announced in the sync folder: {ex.Message}");
        }

        var viewModel = new SendToDeviceViewModel(item, mine.Devices());
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
