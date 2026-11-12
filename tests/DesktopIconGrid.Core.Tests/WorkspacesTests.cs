using DesktopIconGrid.Core.Commands;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Workspaces: membership, names, and the window positions kept on this PC.</summary>
public sealed class WorkspacesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-ws-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void The_Multi_Launch_flag_is_the_built_in_workspace_and_others_are_names()
    {
        var a = new ShortcutItem { Name = "A", MultiLaunch = true };
        var b = new ShortcutItem { Name = "B" };

        Workspaces.SetMember(b, " Work ", true);
        Workspaces.SetMember(b, "work", true); // the same one, any case: once
        Workspaces.SetMember(a, "Work", true);
        Workspaces.SetMember(b, "Home", true);
        Workspaces.SetMember(b, "bad\nname", true); // refused

        Assert.Equal(["work", "Home"], b.Workspaces);
        Assert.Equal([Workspaces.MultiLaunch, "Home", "Work"], Workspaces.Names([a, b]));
        Assert.Equal([a, b], Workspaces.Members([a, b], "WORK"));
        Assert.Equal([a], Workspaces.Members([a, b], Workspaces.MultiLaunch));

        Workspaces.SetMember(a, Workspaces.MultiLaunch, false);
        Workspaces.SetMember(b, "Work", false);
        Workspaces.SetMember(b, "Home", false);
        Assert.False(a.MultiLaunch);
        Assert.Null(b.Workspaces);
        Assert.Null(Workspaces.Normalize("multi launch")); // the built-in name is not a second workspace
        Assert.Null(Workspaces.Normalize(new string('x', Workspaces.MaxNameLength + 1)));
    }

    [Fact]
    public void Membership_survives_a_copy_without_being_shared()
    {
        var original = new ShortcutItem { Name = "A", Workspaces = ["Work"] };

        var copy = (ShortcutItem)original.Clone();
        copy.Workspaces!.Add("Home");

        Assert.Equal(["Work"], original.Workspaces);
    }

    [Fact]
    public void Positions_are_kept_per_workspace_in_the_local_file()
    {
        var path = Path.Combine(_directory, WorkspacePositions.FileName);
        var id = Guid.NewGuid();
        var positions = new WorkspacePositions(path);

        Assert.Null(positions.Get("Work", id));
        Assert.True(positions.Set("Work", new Dictionary<Guid, SavedWindow> { [id] = new(10, 20, 800, 600, true) }));

        var read = new WorkspacePositions(path);
        Assert.Equal(new SavedWindow(10, 20, 800, 600, true), read.Get("work", id));
        Assert.Null(read.Get("Home", id));

        // A member that is closed keeps its place; one that left the workspace loses it.
        var other = Guid.NewGuid();
        read.Set("Work", new Dictionary<Guid, SavedWindow> { [other] = new(1, 2, 300, 200, false) }, keep: [id, other]);
        Assert.NotNull(read.Get("Work", id));
        read.Set("Work", new Dictionary<Guid, SavedWindow> { [other] = new(1, 2, 300, 200, false) }, keep: [other]);
        Assert.Null(read.Get("Work", id));

        read.Set("Work", new Dictionary<Guid, SavedWindow>());
        Assert.Null(new WorkspacePositions(path).Get("Work", other));

        File.WriteAllText(path, "{ broken");
        Assert.Null(new WorkspacePositions(path).Get("Work", id)); // unreadable: nothing is moved
    }

    [Fact]
    public void The_command_line_starts_a_workspace_by_name()
    {
        var (commands, error) = BarCommands.Parse(["--workspace", "Work"]);

        Assert.Null(error);
        Assert.Equal([new BarCommand(BarCommandKind.Workspace, "Work")], commands);
        Assert.Equal(commands, BarCommands.Deserialize(BarCommands.Serialize(commands)));
        Assert.NotNull(BarCommands.Parse(["--workspace"]).Error);
        Assert.NotNull(BarCommands.Parse(["--workspace", "--show"]).Error);
    }

    [Fact]
    public void A_name_with_spaces_around_it_or_an_invisible_character_is_no_second_workspace()
    {
        var a = new ShortcutItem { Name = "A", Workspaces = [" Work"] }; // as a hand-edited or synced layout may have it

        Assert.Equal(["Work"], Workspaces.Names([a]));
        Assert.True(Workspaces.IsMember(a, "Work")); // listed, and found under the listed name
        Assert.Null(Workspaces.Normalize("Wo\u200Brk"));
        Assert.Null(Workspaces.Normalize("Work\u202E"));
    }

    [Fact]
    public void A_window_remembered_on_a_monitor_that_is_gone_is_put_on_one_that_is_there()
    {
        var laptop = new PixelRect(0, 0, 1920, 1080);
        var external = new PixelRect(1920, 0, 2560, 1440);
        var onExternal = new SavedWindow(2400, 200, 2000, 1300, true);

        Assert.Equal(onExternal, onExternal.OnScreen([laptop, external])); // still there: untouched
        Assert.Equal(onExternal, onExternal.OnScreen([])); // nothing known: untouched

        var moved = onExternal.OnScreen([laptop]); // undocked
        Assert.True(moved.Left >= 0 && moved.Left + moved.Width <= 1920);
        Assert.True(moved.Top >= 0 && moved.Top + moved.Height <= 1080);
        Assert.True(moved.Maximized);

        // Only a strip of its bottom on screen is not "visible": the title bar could not be grabbed.
        var above = new SavedWindow(100, -900, 800, 950, false);
        Assert.NotEqual(above, above.OnScreen([laptop]));
        // Partly off the right edge with its title bar on screen is fine.
        var partly = new SavedWindow(1700, 100, 800, 600, false);
        Assert.Equal(partly, partly.OnScreen([laptop]));
    }
}
