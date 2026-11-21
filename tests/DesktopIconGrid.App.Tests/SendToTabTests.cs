using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Resources;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.14.0 WP5 and A3: Settings ▸ Send to PC, with this PC's name, receiving, the receive folder and sync.</summary>
public sealed class SendToTabTests
{
    private const string Downloads = @"C:\Users\Bo\Downloads";
    private const string Data = @"C:\Users\Bo\AppData\Local\DesktopIconGrid";
    private const string SyncFolder = @"D:\OneDrive\DIG";

    private static ThisPcSettings ThisPc(string name = "BAMBOO-PC", string? folder = null, SyncSetup setup = SyncSetup.Folder) =>
        new(name, ReceiveFiles: false, folder, Downloads, Data, setup == SyncSetup.Folder ? SyncFolder : null, setup);

    [Fact]
    public void The_tab_shows_this_PCs_name_and_Downloads_and_Save_keeps_no_folder_for_Downloads()
    {
        var viewModel = new SettingsViewModel(new LauncherSettings());
        viewModel.UseThisPc(ThisPc());

        Assert.Equal(("BAMBOO-PC", false, Downloads, true), (viewModel.PcName, viewModel.ReceiveFiles, viewModel.ReceiveFolderText, viewModel.ReceiveFolderIsDownloads));
        Assert.Equal((Strings.Settings_SyncStateFolder, false), (viewModel.SyncStateText, viewModel.NeedsSync));
        Assert.Equal(Strings.Settings_TabSendTo, viewModel.SendToTab);
        Assert.Equal(new ThisPcChoice("BAMBOO-PC", false, null), viewModel.ToThisPc());
        Assert.Equal(64, SettingsViewModel.MaxPcNameLength);
    }

    [Fact]
    public void A_name_a_folder_and_receiving_are_kept_at_Save_after_the_folder_is_checked()
    {
        var viewModel = new SettingsViewModel(new LauncherSettings());
        viewModel.UseThisPc(ThisPc());
        viewModel.PcName = "  Study  ";
        viewModel.ReceiveFiles = true;
        viewModel.ReceiveFolderText = @"E:\Inbox";
        string? checkedFolder = null;

        Assert.True(viewModel.TryCheckReceiveFolder((folder, data, sync) =>
        {
            checkedFolder = folder;
            Assert.Equal((Data, SyncFolder), (data, sync));
            return ReceiveFolderProblem.None;
        }));

        Assert.Equal(@"E:\Inbox", checkedFolder);
        Assert.Equal(new ThisPcChoice("Study", true, @"E:\Inbox"), viewModel.ToThisPc());
    }

    [Fact]
    public void A_folder_in_the_sync_folder_is_refused_at_once_and_one_that_fails_on_the_disk_at_Save()
    {
        var viewModel = new SettingsViewModel(new LauncherSettings());
        viewModel.UseThisPc(ThisPc());

        viewModel.ReceiveFolderText = @"D:\OneDrive\DIG\in";
        Assert.Equal(string.Format(Strings.Settings_ReceiveInSyncFolder, @"D:\OneDrive\DIG\in"), viewModel.ReceiveFolderError);
        Assert.False(viewModel.CanSave);
        Assert.Equal("\u26A0 " + Strings.Settings_TabSendTo, viewModel.SendToTab);
        Assert.Equal(string.Format(Strings.Settings_FixOnTab, Strings.Settings_TabSendTo), viewModel.ProblemText);

        viewModel.ReceiveFolderText = "inbox";
        Assert.Equal(string.Format(Strings.Settings_ReceiveNotAbsolute, "inbox"), viewModel.ReceiveFolderError);

        viewModel.ReceiveFolderText = @"E:\Stick";
        Assert.Null(viewModel.ReceiveFolderError);
        Assert.False(viewModel.TryCheckReceiveFolder((_, _, _) => ReceiveFolderProblem.CannotUse)); // the stick was pulled out
        Assert.Equal(string.Format(Strings.Settings_ReceiveCannotUse, @"E:\Stick"), viewModel.ReceiveFolderError);
        Assert.False(viewModel.CanSave);

        viewModel.ReceiveFolderText = @"E:\Stick\Inbox";
        Assert.Null(viewModel.ReceiveFolderError); // said for the folder that was there then
        viewModel.ResetReceiveFolder();
        Assert.True(viewModel.ReceiveFolderIsDownloads);
        Assert.True(viewModel.TryCheckReceiveFolder((_, _, _) => throw new InvalidOperationException("Downloads is not checked")));
        Assert.Null(viewModel.ToThisPc()!.ReceiveFolder);
    }

    [Fact]
    public void A_folder_kept_with_a_variable_shows_expanded_and_without_this_PCs_settings_nothing_is_kept()
    {
        var viewModel = new SettingsViewModel(new LauncherSettings());
        Assert.Null(viewModel.ToThisPc()); // tests and a dialog without them
        Assert.True(viewModel.TryCheckReceiveFolder());

        viewModel.UseThisPc(ThisPc(folder: @"""%SystemDrive%\Inbox"""));
        Assert.Equal(Environment.ExpandEnvironmentVariables(@"%SystemDrive%\Inbox"), viewModel.ReceiveFolderText);
        Assert.False(viewModel.ReceiveFolderIsDownloads);
    }

    [Fact]
    public void After_the_Sync_window_the_tab_says_how_sync_is_set_up_and_takes_a_name_changed_there_unless_one_was_typed_here()
    {
        var viewModel = new SettingsViewModel(new LauncherSettings());
        viewModel.UseThisPc(ThisPc(name: "A", setup: SyncSetup.None));
        Assert.True(viewModel.NeedsSync);
        Assert.Equal(Strings.Settings_SyncStateNone, viewModel.SyncStateText);

        viewModel.RefreshFromSync(ThisPc(name: "B", setup: SyncSetup.Drive));
        Assert.Equal(("B", Strings.Settings_SyncStateDrive, false), (viewModel.PcName, viewModel.SyncStateText, viewModel.NeedsSync));

        viewModel.PcName = "Mine";
        viewModel.RefreshFromSync(ThisPc(name: "C", setup: SyncSetup.Folder));
        Assert.Equal(("Mine", Strings.Settings_SyncStateFolder), (viewModel.PcName, viewModel.SyncStateText));
    }
}
