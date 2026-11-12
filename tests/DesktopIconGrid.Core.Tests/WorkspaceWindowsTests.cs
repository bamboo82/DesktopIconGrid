using DesktopIconGrid.Core.Launching;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Which window is which workspace member's (review 24 W3), against made-up window lists.</summary>
public class WorkspaceWindowsTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    private static WindowInfo Window(int handle, int processId, bool bare = false, bool owned = false, string className = "Main", string? appId = null) =>
        new(new IntPtr(handle), processId, className, IsVisible: true, IsCloaked: false, HasOwner: owned, IsToolWindow: false, IsAppWindow: false, IsMinimized: false, AppUserModelId: appId, IsBare: bare);

    private static WorkspaceMember Member(Guid id, int[] own, int[] program, bool parameters = false, string? family = null) =>
        new(id, own.ToHashSet(), program.ToHashSet(), family, parameters);

    [Fact]
    public void Two_members_on_one_program_get_two_windows_and_each_its_own_first()
    {
        // Notepad twice: A was started by the bar (process 11), B was opened some other way (process 10). A's window is on top.
        WindowInfo[] screen = [Window(1, 11), Window(2, 10)];
        var a = Member(A, own: [11], program: [10, 11], parameters: true);
        var b = Member(B, own: [], program: [10, 11], parameters: true);

        var capture = new WorkspaceWindows(screen);
        Assert.Equal(new HashSet<Guid> { A, B }, capture.TakeRunning([b, a], anyOfTheProgram: true));
        Assert.Equal(new IntPtr(1), capture.WindowOf(A)); // its own, although B comes first and would have taken the topmost
        Assert.Equal(new IntPtr(2), capture.WindowOf(B));

        // Starting the workspace: a member with parameters counts only what the bar started for it, so B is started.
        var start = new WorkspaceWindows(screen);
        Assert.Equal(new HashSet<Guid> { A }, start.TakeRunning([b, a], anyOfTheProgram: false));
        Assert.Null(start.WindowOf(B));

        // A member without parameters takes any window of its program that no other member has.
        var c = Member(C, own: [], program: [10, 11]);
        var plain = new WorkspaceWindows(screen);
        plain.TakeRunning([a, c], anyOfTheProgram: false);
        Assert.Equal(new IntPtr(2), plain.WindowOf(C));
    }

    [Fact]
    public void A_member_started_now_takes_only_a_window_that_was_not_there_before_the_oldest_first()
    {
        // A program that opens every file in its one process (10), already showing the user's own document (window 1).
        var windows = new WorkspaceWindows([Window(1, 10)]);
        var a = Member(A, own: [], program: [10], parameters: true);
        var b = Member(B, own: [], program: [10], parameters: true);
        Assert.Empty(windows.TakeRunning([a, b], anyOfTheProgram: false));

        windows.Look([Window(1, 10)]);
        windows.TakeNew([a, b], waitIsOver: false);
        Assert.Null(windows.WindowOf(A)); // the user's window is not A's

        // Both new windows show between two looks: the lower one opened first, so it is A's (started first).
        windows.Look([Window(3, 10), Window(2, 10), Window(1, 10)]);
        windows.TakeNew([a, b], waitIsOver: false);
        Assert.Equal(new IntPtr(2), windows.WindowOf(A));
        Assert.Equal(new IntPtr(3), windows.WindowOf(B));
    }

    [Fact]
    public void A_window_seen_in_an_earlier_look_is_older_than_one_on_top_of_it()
    {
        var windows = new WorkspaceWindows([]);
        var a = Member(A, own: [], program: [10]);
        var b = Member(B, own: [], program: [10]);

        windows.Look([Window(5, 10)]);
        windows.Look([Window(4, 10), Window(5, 10)]); // 4 opened later and is on top
        windows.TakeNew([a, b], waitIsOver: false);

        Assert.Equal(new IntPtr(5), windows.WindowOf(A));
        Assert.Equal(new IntPtr(4), windows.WindowOf(B));
    }

    [Fact]
    public void A_splash_screen_waits_for_the_main_window_and_a_closed_window_frees_its_member()
    {
        var windows = new WorkspaceWindows([]);
        var a = Member(A, own: [20], program: [20]);

        windows.Look([Window(7, 20, bare: true)]); // the splash screen
        windows.TakeNew([a], waitIsOver: false);
        Assert.Null(windows.WindowOf(A));

        windows.Look([Window(8, 20), Window(7, 20, bare: true)]); // the main window, while the splash is still up
        windows.TakeNew([a], waitIsOver: false);
        Assert.Equal(new IntPtr(8), windows.WindowOf(A));

        windows.Look([Window(9, 20)]); // the window A had closed: A takes the next one
        Assert.Null(windows.WindowOf(A));
        windows.TakeNew([a], waitIsOver: false);
        Assert.Equal(new IntPtr(9), windows.WindowOf(A));

        // A program whose only window has no title bar and no border: taken when the wait is over.
        var bare = new WorkspaceWindows([]);
        bare.Look([Window(11, 30, bare: true)]);
        var b = Member(B, own: [], program: [30]);
        bare.TakeNew([b], waitIsOver: false);
        Assert.Null(bare.WindowOf(B));
        bare.TakeNew([b], waitIsOver: true);
        Assert.Equal(new IntPtr(11), bare.WindowOf(B));

        // A program that runs already may have a bare window as its only one: taken, after any with a frame.
        var running = new WorkspaceWindows([Window(12, 40, bare: true), Window(13, 40)]);
        var c = Member(C, own: [], program: [40]);
        running.TakeRunning([c], anyOfTheProgram: false);
        Assert.Equal(new IntPtr(13), running.WindowOf(C));
    }

    [Fact]
    public void Only_windows_with_a_taskbar_button_count_and_a_Store_app_is_found_by_its_frame()
    {
        const string Family = "Microsoft.WindowsCalculator_8wekyb3d8bbwe";
        var frame = Window(21, 999, className: WindowSwitching.FrameWindowClass, appId: Family + "!App"); // ApplicationFrameHost's
        var windows = new WorkspaceWindows([Window(20, 10, owned: true), frame]);
        var dialogOnly = Member(A, own: [10], program: [10]);
        var calculator = Member(B, own: [], program: [], family: Family);

        windows.TakeRunning([dialogOnly, calculator], anyOfTheProgram: false);

        Assert.Null(windows.WindowOf(A)); // an owned dialog is not the program's window
        Assert.Equal(new IntPtr(21), windows.WindowOf(B));
    }
}
