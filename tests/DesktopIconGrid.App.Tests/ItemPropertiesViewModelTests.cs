using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

[Collection("Shared icon worker")]
public class ItemPropertiesViewModelTests
{
    private sealed class CountingIcons : IIconProvider
    {
        private int _lookups;

        public int Lookups => Volatile.Read(ref _lookups);

        public ImageSource? GetIcon(string source, int pixelSize)
        {
            Interlocked.Increment(ref _lookups);
            return null;
        }

        public ImageSource? GetItemIcon(LauncherItem item, int pixelSize) => null;

        public ImageSource? GetFolderIcon(int pixelSize) => null;

        public bool CanResolve(string? source) => !string.IsNullOrEmpty(source);

        public void ClearCache()
        {
        }
    }

    [Fact]
    public async Task Typing_a_location_resolves_the_preview_once_after_the_last_keystroke()
    {
        var icons = new CountingIcons();
        var viewModel = new ItemPropertiesViewModel(new ShortcutItem { Name = "Tool", Location = "C:\\tool.exe" }, isNew: false, icons);
        var pauses = new List<TaskCompletionSource>();
        viewModel.PausePreview = () =>
        {
            var pause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pauses.Add(pause);
            return pause.Task;
        };
        await viewModel.PreviewSettled;
        Assert.Equal(1, icons.Lookups); // the dialog opens with a preview right away, without a pause

        viewModel.Location = "C:\\t";
        viewModel.Location = "C:\\to";
        viewModel.Location = "C:\\too";
        Assert.Equal(3, pauses.Count); // each edit waits for a pause...
        Assert.Equal(1, icons.Lookups);

        foreach (var pause in pauses)
        {
            pause.SetResult(); // ...and however late the pauses end, only the last edit's survives
        }

        await viewModel.PreviewSettled;
        Assert.Equal(2, icons.Lookups); // one for the opening, one for the whole burst of edits
    }

    [Fact]
    public void A_path_pasted_with_quotes_is_stored_without_them()
    {
        // Review 16 R3′: Explorer's Copy as path gives "E:\Apps\x.exe"; with the quote, the drive was not recognised.
        var shortcut = new ShortcutItem { Name = "X", Location = @"C:\x.exe" };
        var viewModel = new ItemPropertiesViewModel(shortcut, isNew: false, new CountingIcons())
        {
            Location = "\"E:\\Apps\\x.exe\"",
            StartIn = "\"E:\\Apps\"",
        };

        Assert.True(viewModel.Apply());

        Assert.Equal(@"E:\Apps\x.exe", shortcut.Location);
        Assert.Equal(@"E:\Apps", shortcut.StartIn);
    }

    [Fact]
    public void When_running_follows_the_location_until_the_user_picks_it()
    {
        var shortcut = new ShortcutItem { Name = "Report", Location = @"C:\Docs\report.docx" };
        var viewModel = new ItemPropertiesViewModel(shortcut, isNew: false, new CountingIcons());

        Assert.False(viewModel.SelectedWhenRunning.Switches); // a document starts again
        viewModel.Location = @"C:\Tools\app.exe";
        Assert.True(viewModel.SelectedWhenRunning.Switches); // a program switches
        Assert.True(viewModel.Apply());
        Assert.Null(shortcut.WhenRunning); // nothing stored: the location keeps deciding
    }

    [Fact]
    public void Parameters_turn_the_default_to_starting_another_until_the_user_picks()
    {
        var shortcut = new ShortcutItem { Name = "Server", Location = "mstsc.exe" };
        var viewModel = new ItemPropertiesViewModel(shortcut, isNew: false, new CountingIcons());

        Assert.True(viewModel.SelectedWhenRunning.Switches);
        viewModel.Parameters = "/v:server2";
        Assert.False(viewModel.SelectedWhenRunning.Switches);
        Assert.True(viewModel.Apply());
        Assert.Null(shortcut.WhenRunning); // the default, so nothing stored

        var picked = new ItemPropertiesViewModel(shortcut, isNew: false, new CountingIcons());
        picked.SelectedWhenRunning = ItemPropertiesViewModel.WhenRunningChoices.Single(o => o.Switches);
        Assert.True(picked.Apply());
        Assert.Equal(ShortcutItem.WhenRunningSwitch, shortcut.WhenRunning);
    }

    [Fact]
    public void Keep_a_log_is_off_while_Run_as_administrator_is_ticked()
    {
        var viewModel = new ItemPropertiesViewModel(new ShortcutItem { Name = "Tool", Location = @"C:\Tools\tool.exe" }, isNew: false, new CountingIcons());
        Assert.True(viewModel.CanCaptureLog);

        viewModel.RunAsAdministrator = true;

        Assert.False(viewModel.CanCaptureLog);
        Assert.Equal(DesktopIconGrid.Resources.Strings.Props_LogNotElevated, viewModel.CaptureLogHint);
    }

    [Fact]
    public void A_picked_choice_is_stored_only_when_it_differs_from_the_location_s_default()
    {
        var program = new ShortcutItem { Name = "App", Location = @"C:\Tools\app.exe" };
        var viewModel = new ItemPropertiesViewModel(program, isNew: false, new CountingIcons());
        viewModel.SelectedWhenRunning = ItemPropertiesViewModel.WhenRunningChoices.Single(o => !o.Switches);
        Assert.True(viewModel.Apply());
        Assert.Equal(ShortcutItem.WhenRunningStart, program.WhenRunning);

        var back = new ItemPropertiesViewModel(program, isNew: false, new CountingIcons());
        Assert.False(back.SelectedWhenRunning.Switches);
        back.SelectedWhenRunning = ItemPropertiesViewModel.WhenRunningChoices.Single(o => o.Switches);
        Assert.True(back.Apply());
        Assert.Null(program.WhenRunning);
    }

    [Fact]
    public void The_advanced_launch_options_are_applied_and_a_browser_profile_from_another_PC_is_kept()
    {
        var shortcut = new ShortcutItem { Name = "Mail", Location = "https://mail.example.com", Browser = "edge", BrowserProfile = "Profile 9", AppWindow = true };
        var browsers = new[] { BrowserCatalog.DefaultBrowser, new BrowserChoice("edge", "Default", "Microsoft Edge – Personal") };
        var viewModel = new ItemPropertiesViewModel(shortcut, isNew: false, new CountingIcons(), browsers);

        Assert.True(viewModel.IsWebLink);
        Assert.Equal("Profile 9", viewModel.SelectedBrowser.Profile); // not installed here, kept as it is
        Assert.Contains(viewModel.SelectedBrowser, viewModel.BrowserOptions);
        viewModel.StartIn = @" C:\Work ";
        viewModel.SelectedWindowState = ItemPropertiesViewModel.WindowStateChoices.Single(c => c.Value == ShortcutItem.WindowMaximized);
        viewModel.RunAsAdministrator = true;
        Assert.True(viewModel.Apply());

        Assert.Equal(@"C:\Work", shortcut.StartIn);
        Assert.Equal(ShortcutItem.WindowMaximized, shortcut.WindowState);
        Assert.True(shortcut.RunAsAdministrator);
        Assert.Equal(("edge", "Profile 9", true), (shortcut.Browser, shortcut.BrowserProfile, shortcut.AppWindow));

        viewModel.SelectedBrowser = BrowserCatalog.DefaultBrowser;
        Assert.False(viewModel.CanChooseAppWindow);
        Assert.True(viewModel.Apply());
        Assert.Equal((null, null, false), (shortcut.Browser, shortcut.BrowserProfile, shortcut.AppWindow));
    }
}
