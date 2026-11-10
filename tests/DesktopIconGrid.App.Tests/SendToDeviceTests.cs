using System.IO;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>The "Send to PC" icon and the receiving side's mark on a file.</summary>
public sealed class SendToDeviceTests : IDisposable
{
    private const string Laptop = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Desktop = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private readonly string _folder = Directory.CreateTempSubdirectory("dig-send-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Files_are_sent_and_a_folder_is_reported_not_sent()
    {
        var sync = Path.Combine(_folder, "sync");
        var laptop = new FolderInbox(sync, Laptop, "Laptop");
        var desktop = new FolderInbox(sync, Desktop, "Desktop");
        laptop.Announce();
        desktop.Announce();
        var file = Path.Combine(_folder, "a.txt");
        File.WriteAllText(file, "x");

        var (sent, problems) = SendToDeviceHandler.Send(laptop, Desktop, [file, _folder]);

        Assert.Equal(1, sent);
        Assert.Single(problems);
        Assert.Equal("a.txt", Assert.Single(desktop.Pending()).Name);
    }

    [Fact]
    public void The_dialog_needs_a_PC_and_names_the_icon_after_it()
    {
        var item = new SpecialIconItem { Name = DesktopIconGrid.Resources.Strings.SendTo_Name, Kind = SpecialIconKinds.SendToDevice };

        var none = new SendToDeviceViewModel(item, []);
        Assert.False(none.CanSave);
        Assert.True(none.HasNoDevices);

        var viewModel = new SendToDeviceViewModel(item, [new InboxDevice(Desktop, "Desktop"), new InboxDevice(Laptop, "Laptop")]);
        Assert.Equal("Desktop", viewModel.SelectedDevice!.Name);
        viewModel.SelectedDevice = viewModel.Devices[1];
        Assert.Contains("Laptop", viewModel.Name);
        viewModel.Apply(item);

        Assert.Equal(Laptop, item.GetSetting(SendToDeviceHandler.DeviceIdKey));
        Assert.Equal("Laptop", item.GetSetting(SendToDeviceHandler.DeviceNameKey));
    }

    [Fact]
    public void A_received_file_is_marked_as_downloaded_on_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var file = Path.Combine(_folder, "got.exe");
        File.WriteAllText(file, "x");

        InboxService.MarkAsDownloaded(file);

        Assert.Contains("ZoneId=3", File.ReadAllText(file + ":Zone.Identifier"));
        Assert.True(Directory.Exists(Path.GetDirectoryName(InboxService.DownloadsPath())));
    }
}
