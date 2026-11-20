using System.IO;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Resources;
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
    public async Task Files_are_sent_and_a_folder_is_reported_not_sent()
    {
        var sync = Path.Combine(_folder, "sync");
        var laptop = new FolderInbox(sync, Laptop, "Laptop");
        var desktop = new FolderInbox(sync, Desktop, "Desktop");
        laptop.Announce();
        desktop.Announce();
        var file = Path.Combine(_folder, "a.txt");
        File.WriteAllText(file, "x");

        var (sent, problems) = await SendToDeviceHandler.SendAsync(laptop, Desktop, [file, _folder]);

        Assert.Equal(1, sent);
        Assert.Single(problems);
        Assert.Equal("a.txt", Assert.Single(desktop.Pending()).Name);
    }

    private static Func<CancellationToken, Task<IReadOnlyList<InboxDevice>>> Listing(params InboxDevice[] devices) =>
        _ => Task.FromResult<IReadOnlyList<InboxDevice>>(devices);

    private static SpecialIconItem NewIcon() => new() { Name = Strings.SendTo_Name, Kind = SpecialIconKinds.SendToDevice };

    [Fact]
    public async Task The_list_comes_after_the_dialog_opens_and_the_icon_is_named_after_the_PC_picked()
    {
        // Review 27 S2: nothing is listed before the window shows; then the look runs off the UI thread.
        var item = NewIcon();
        var viewModel = new SendToDeviceViewModel(item, receives: true, receivesByMenu: false, Listing(new(Desktop, "Desktop"), new(Laptop, "Laptop")));
        Assert.True(viewModel.IsLooking);
        Assert.False(viewModel.CanChooseDevice);
        Assert.False(viewModel.CanLook);
        Assert.Equal(Strings.SendTo_Looking, viewModel.Status);

        await viewModel.LookAsync();

        Assert.False(viewModel.IsLooking);
        Assert.True(viewModel.CanChooseDevice && viewModel.CanLook);
        Assert.Equal((Strings.SendTo_Hint, false), (viewModel.Status, viewModel.StatusIsWarning));
        Assert.Equal("Desktop", viewModel.SelectedDevice!.Name);
        Assert.Equal(Strings.SendTo_Name, viewModel.Name); // the list's answer does not rename the icon
        viewModel.SelectedDevice = viewModel.Devices[1];
        Assert.Contains("Laptop", viewModel.Name);
        viewModel.Apply(item);

        Assert.Equal(Laptop, item.GetSetting(SendToDeviceHandler.DeviceIdKey));
        Assert.Equal("Laptop", item.GetSetting(SendToDeviceHandler.DeviceNameKey));
    }

    [Fact]
    public async Task With_no_other_PC_yet_the_box_alone_lets_the_icon_be_saved_to_receive()
    {
        var viewModel = new SendToDeviceViewModel(NewIcon(), receives: true, receivesByMenu: false, Listing());
        await viewModel.LookAsync();

        Assert.Equal((Strings.SendTo_NoOtherPcs, true), (viewModel.Status, viewModel.StatusIsWarning));
        Assert.Null(viewModel.SelectedDevice);
        Assert.False(viewModel.CanChooseDevice);
        Assert.True(viewModel.CanSave); // receive only, for now; the PC is chosen later
        viewModel.ReceivesShown = false;
        Assert.False(viewModel.CanSave); // neither a PC nor receiving
        viewModel.ReceivesShown = true;
        viewModel.Name = " ";
        Assert.False(viewModel.CanSave);
    }

    [Fact]
    public async Task A_failed_or_silent_look_says_why_and_the_PC_chosen_before_is_kept()
    {
        var item = NewIcon();
        item.SetSetting(SendToDeviceHandler.DeviceIdKey, Desktop);
        item.SetSetting(SendToDeviceHandler.DeviceNameKey, "Den");

        var failing = new SendToDeviceViewModel(item, receives: false, receivesByMenu: false, _ => throw new IOException("The network path was not found."));
        Assert.Equal("Den", failing.SelectedDevice!.Name); // before any answer, so OK cannot drop it
        await failing.LookAsync();
        Assert.Equal((string.Format(Strings.SendTo_LookFailed, "The network path was not found."), true), (failing.Status, failing.StatusIsWarning));
        Assert.True(failing.CanSave && failing.CanLook);
        failing.Apply(item);
        Assert.Equal(Desktop, item.GetSetting(SendToDeviceHandler.DeviceIdKey));

        var before = SendToDeviceViewModel.LookTimeout;
        SendToDeviceViewModel.LookTimeout = TimeSpan.FromMilliseconds(100);
        try
        {
            var never = new TaskCompletionSource<IReadOnlyList<InboxDevice>>();
            var silent = new SendToDeviceViewModel(item, receives: false, receivesByMenu: false, _ => never.Task);
            await silent.LookAsync();
            Assert.Equal(string.Format(Strings.SendTo_LookFailed, Strings.SendTo_NoAnswer), silent.Status);
            Assert.True(silent.CanLook); // ↻ can try again
        }
        finally
        {
            SendToDeviceViewModel.LookTimeout = before;
        }

        // A list without that PC (it does not receive now) keeps it as a choice, after the PCs listed.
        var without = new SendToDeviceViewModel(item, receives: false, receivesByMenu: false, Listing(new InboxDevice(Laptop, "Laptop")));
        await without.LookAsync();
        Assert.Equal(["Laptop", "Den"], without.Devices.Select(device => device.Name));
        Assert.Equal(Desktop, without.SelectedDevice!.Id);

        // A list with it gives the name it has now.
        var renamed = new SendToDeviceViewModel(item, receives: false, receivesByMenu: false, Listing(new InboxDevice(Desktop, "Study")));
        await renamed.LookAsync();
        Assert.Equal("Study", Assert.Single(renamed.Devices).Name);
        Assert.Equal("Study", renamed.SelectedDevice!.Name);
    }

    [Fact]
    public void While_the_menu_turns_receiving_on_the_box_shows_it_and_the_icons_mark_is_left_alone()
    {
        var viewModel = new SendToDeviceViewModel(NewIcon(), receives: false, receivesByMenu: true, Listing());

        Assert.True(viewModel.ReceivesShown);
        Assert.False(viewModel.CanChangeReceives);
        viewModel.ReceivesShown = false;
        Assert.True(viewModel.ReceivesShown);
        Assert.False(viewModel.Receives); // OK does not mark the icon for it
        Assert.True(viewModel.CanSave);
    }

    /// <summary>The receiving side, as the properties see it, without a sync folder.</summary>
    private sealed class NoReceiving : IReceiving
    {
        public IInbox? Current() => null;

        public bool ReceiveSetting => false;

        public void Opened()
        {
        }

        public void Changed()
        {
        }
    }

    [Fact]
    public async Task Without_sync_a_new_icon_is_added_without_a_PC_and_later_starts_with_the_box_ticked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 0.13.0 WP5: adding the icon before Sync across PCs is set up used to add nothing.
        var trust = new CommandTrust(null);
        var dialogs = new RecordingDialogs();
        var handler = new SendToDeviceHandler(new NoReceiving(), dialogs, () => null, _folder, trust);
        using var bar = new TestBar(_folder, new LauncherModel(), handlers: [handler], trust: trust);

        bar.ViewModel.AddSpecialIcon(handler, bar.ViewModel.RootItems, 0);

        var item = Assert.IsType<SpecialIconItem>(Assert.Single(bar.ViewModel.RootItems.Items).Model);
        Assert.Null(item.GetSetting(SendToDeviceHandler.DeviceIdKey));
        Assert.False(trust.IsApproved(FolderInbox.IconMark(item.Id))); // nothing receives, nothing is announced
        var notice = Assert.Single(dialogs.Shown);
        Assert.Equal((NoticeKind.Information, NoticeTarget.Sync, Strings.SendTo_AddedWithoutSync), (notice.Kind, notice.Target, notice.Message));
        Assert.Equal(Strings.SendTo_NoDevice, handler.GetToolTip(item));
        Assert.False(handler.AcceptsFileDrops(item) || handler.AcceptsPaste(item));

        // With sync set up, its properties start with "This PC also receives files" ticked, as for a new icon; OK with a
        // PC sets the mark, and the note that it was added here goes.
        Assert.True(handler.StartsTicked(item, isNew: false));
        var viewModel = new SendToDeviceViewModel(item, receives: handler.StartsTicked(item, isNew: false), receivesByMenu: false, Listing(new InboxDevice(Desktop, "Desktop")));
        await viewModel.LookAsync();
        handler.Saved(item, viewModel);

        Assert.Equal(Desktop, item.GetSetting(SendToDeviceHandler.DeviceIdKey));
        Assert.True(trust.IsApproved(FolderInbox.IconMark(item.Id)));
        Assert.False(trust.IsApproved(FolderInbox.AddedHereMark(item.Id)));
    }

    [Fact]
    public void An_icon_without_a_PC_that_arrived_from_another_PC_starts_with_the_box_empty()
    {
        // Review 27 S3 stays: only an icon added or saved on this PC starts ticked.
        var trust = new CommandTrust(null);
        var handler = new SendToDeviceHandler(new NoReceiving(), new RecordingDialogs(), () => null, _folder, trust);
        var arrived = NewIcon();

        Assert.False(handler.StartsTicked(arrived, isNew: false));
        Assert.True(handler.StartsTicked(arrived, isNew: true));

        // Added here without a PC, then given one on another PC: the box follows this PC's mark again.
        trust.Approve(FolderInbox.AddedHereMark(arrived.Id));
        Assert.True(handler.StartsTicked(arrived, isNew: false));
        arrived.SetSetting(SendToDeviceHandler.DeviceIdKey, Desktop);
        Assert.False(handler.StartsTicked(arrived, isNew: false));
        trust.Approve(FolderInbox.IconMark(arrived.Id));
        Assert.True(handler.StartsTicked(arrived, isNew: false));
    }

    [Fact]
    public void A_new_Send_to_PC_icon_has_the_send_symbol_and_older_ones_are_drawn_with_it_without_a_change_to_the_layout()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Review 27 S5, and the review session's design for 0.12.0 (A3): the paper plane symbol, one colour like the bar.
        var handler = new SendToDeviceHandler(new NoReceiving(), new RecordingDialogs(), () => null, _folder, new CommandTrust(null));
        Assert.Equal(BuiltInTextIcons.SendToDevice, handler.CreateItem().TextIcon);
        Assert.Equal("\uE724", BuiltInTextIcons.SendToDevice.Text);

        var outbox = new TextIcon("\U0001F4E4", "Segoe UI Emoji", 22);
        var chosen = new TextIcon("送", "Segoe UI", 20);
        var model = new LauncherModel();
        var withoutPicture = new SpecialIconItem { Name = "To the den", Kind = SpecialIconKinds.SendToDevice }; // made by 0.10.0
        var withEmoji = new SpecialIconItem { Name = "To the study", Kind = SpecialIconKinds.SendToDevice, TextIcon = outbox }; // made by 0.11.0
        var withOwn = new SpecialIconItem { Name = "To the attic", Kind = SpecialIconKinds.SendToDevice, TextIcon = chosen };
        model.Add(withoutPicture);
        model.Add(withEmoji);
        model.Add(withOwn);
        using var bar = new TestBar(_folder, model, handlers: [handler]);
        var drawn = bar.ViewModel.RootItems.Items.ToList();

        Assert.All(drawn, item => Assert.True(item.HasTextIcon));
        Assert.Equal([BuiltInTextIcons.SendToDevice, BuiltInTextIcons.SendToDevice, chosen], drawn.Select(item => item.DrawnTextIcon));
        Assert.Null(withoutPicture.TextIcon); // the layout is not changed for them
        Assert.Equal(outbox, withEmoji.TextIcon);
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
