using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Switching to a running program the way the taskbar does, against fake window lists.</summary>
public class WindowSwitchingTests
{
    private static WindowInfo Window(
        int handle,
        int processId,
        string className = "Notepad",
        bool visible = true,
        bool cloaked = false,
        bool owned = false,
        bool tool = false,
        bool app = false,
        bool minimized = false,
        int? hosted = null) =>
        new(new IntPtr(handle), processId, className, visible, cloaked, owned, tool, app, minimized, hosted);

    [Fact]
    public void Only_windows_with_a_taskbar_button_count()
    {
        Assert.True(WindowSwitching.HasTaskbarButton(Window(1, 10)));
        Assert.False(WindowSwitching.HasTaskbarButton(Window(1, 10, visible: false)));
        Assert.False(WindowSwitching.HasTaskbarButton(Window(1, 10, cloaked: true))); // another desktop, a suspended app
        Assert.False(WindowSwitching.HasTaskbarButton(Window(1, 10, owned: true))); // a dialog of another window
        Assert.True(WindowSwitching.HasTaskbarButton(Window(1, 10, owned: true, app: true)));
        Assert.False(WindowSwitching.HasTaskbarButton(Window(1, 10, tool: true)));
        Assert.False(WindowSwitching.HasTaskbarButton(Window(1, 10, tool: true, app: true)));
    }

    [Fact]
    public void A_program_s_windows_are_its_own_and_the_Store_frames_hosting_it_in_z_order()
    {
        var windows = new[]
        {
            Window(30, 7, className: WindowSwitching.FrameWindowClass, hosted: 42), // ApplicationFrameHost (7) hosting the app (42)
            Window(10, 42),
            Window(20, 42, owned: true), // its dialog: no button
            Window(40, 99),
            Window(50, 7, className: WindowSwitching.FrameWindowClass, hosted: 99),
        };

        var found = WindowSwitching.WindowsOf(windows, new HashSet<int> { 42 });

        Assert.Equal([30, 10], found.Select(w => (int)w.Handle));
        Assert.Empty(WindowSwitching.WindowsOf(windows, new HashSet<int>()));
        Assert.Empty(WindowSwitching.WindowsOf(windows, new HashSet<int> { 1234 }));
    }

    [Fact]
    public void A_program_without_windows_is_started()
    {
        Assert.Equal(SwitchAction.Launch, WindowSwitching.Decide([], new IntPtr(5)));
    }

    [Fact]
    public void A_program_behind_others_comes_forward_with_the_window_used_last()
    {
        var windows = new[] { Window(20, 1), Window(10, 1) }; // 20 is topmost

        var action = WindowSwitching.Decide(windows, lastForeground: new IntPtr(999));

        Assert.Equal(new SwitchAction(SwitchKind.Activate, new IntPtr(20)), action);
        Assert.Equal(new SwitchAction(SwitchKind.Activate, new IntPtr(20)), WindowSwitching.Decide(windows, IntPtr.Zero));
    }

    [Fact]
    public void A_single_window_in_front_is_minimised()
    {
        var action = WindowSwitching.Decide([Window(10, 1, minimized: false)], lastForeground: new IntPtr(10));

        Assert.Equal(new SwitchAction(SwitchKind.Minimize, new IntPtr(10)), action);
    }

    [Fact]
    public void With_several_windows_each_click_goes_to_the_next_one_in_a_stable_order()
    {
        // z-order changes with every activation; the cycle follows the handles, so it visits every window in turn.
        Assert.Equal(new IntPtr(20), WindowSwitching.Decide([Window(10, 1), Window(30, 1), Window(20, 1)], new IntPtr(10)).Window);
        Assert.Equal(new IntPtr(30), WindowSwitching.Decide([Window(20, 1), Window(10, 1), Window(30, 1)], new IntPtr(20)).Window);
        Assert.Equal(new IntPtr(10), WindowSwitching.Decide([Window(30, 1), Window(20, 1), Window(10, 1)], new IntPtr(30)).Window);
        Assert.All(
            new[] { new IntPtr(10), new IntPtr(20), new IntPtr(30) },
            front => Assert.Equal(SwitchKind.Activate, WindowSwitching.Decide([Window(10, 1), Window(20, 1), Window(30, 1)], front).Kind));
    }

    [Theory]
    [InlineData(@"C:\Tools\app.exe", true)]
    [InlineData(@"C:\Tools\tool.com", true)]
    [InlineData("notepad", true)]
    [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", true)]
    [InlineData(@"C:\Docs\report.docx", false)]
    [InlineData(@"C:\Docs\run.bat", false)]
    [InlineData("https://example.com", false)]
    [InlineData("shell:Desktop", false)]
    [InlineData("special:show-desktop", false)]
    [InlineData("", false)]
    public void Programs_switch_by_default_and_documents_folders_and_links_start_again(string location, bool switches)
    {
        Assert.Equal(switches, LaunchPlanner.SwitchesWhenRunning(new ShortcutItem { Location = location }));
    }

    [Fact]
    public void An_explicit_choice_wins_over_the_default()
    {
        Assert.False(LaunchPlanner.SwitchesWhenRunning(new ShortcutItem { Location = @"C:\Tools\app.exe", WhenRunning = ShortcutItem.WhenRunningStart }));
        Assert.True(LaunchPlanner.SwitchesWhenRunning(new ShortcutItem { Location = @"C:\Docs\report.docx", WhenRunning = " Switch " }));
        Assert.True(LaunchPlanner.SwitchesWhenRunning(new ShortcutItem { Location = @"C:\Tools\app.exe", WhenRunning = "something newer" }));
    }

    [Fact]
    public void Running_as_administrator_asks_for_elevation_for_programs_only()
    {
        var program = new ShortcutItem { Location = @"C:\Tools\app.exe" };
        Assert.Equal(LaunchPlanner.RunAsVerb, LaunchPlanner.Plan(program, expandParameters: true, asAdministrator: true).StartInfo!.Verb);
        Assert.Equal(string.Empty, LaunchPlanner.Plan(program, expandParameters: true, asAdministrator: false).StartInfo!.Verb);
        Assert.Equal(string.Empty, LaunchPlanner.Plan(new ShortcutItem { Location = "https://example.com" }, true, true).StartInfo!.Verb);
        Assert.Equal(string.Empty, LaunchPlanner.Plan(new ShortcutItem { Location = "shell:Desktop" }, true, true).StartInfo!.Verb);
    }

    [Fact]
    public void The_choice_is_kept_in_the_layout_and_copied_by_edits()
    {
        var shortcut = new ShortcutItem { Location = @"C:\Tools\app.exe", WhenRunning = ShortcutItem.WhenRunningStart };
        var json = System.Text.Json.JsonSerializer.Serialize<LauncherItem>(shortcut, Storage.LayoutStorage.JsonOptions);
        var read = Assert.IsType<ShortcutItem>(System.Text.Json.JsonSerializer.Deserialize<LauncherItem>(json, Storage.LayoutStorage.JsonOptions));
        Assert.Equal(ShortcutItem.WhenRunningStart, read.WhenRunning);

        var target = new ShortcutItem();
        target.CopyContentFrom(read);
        Assert.Equal(ShortcutItem.WhenRunningStart, target.WhenRunning);
        Assert.DoesNotContain("whenRunning", System.Text.Json.JsonSerializer.Serialize<LauncherItem>(new ShortcutItem(), Storage.LayoutStorage.JsonOptions));
    }
}
