using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

public class LaunchPlannerTests
{
    private static readonly string RootedExe = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopIconGridTests", "Tools", "app.exe"));

    [Fact]
    public void Rooted_file_gets_shell_execute_and_folder_as_working_directory()
    {
        var plan = LaunchPlanner.Plan(new ShortcutItem { Location = RootedExe, Parameters = " --flag  " });

        Assert.Equal(LaunchAction.StartProcess, plan.Action);
        var info = plan.StartInfo!;
        Assert.True(info.UseShellExecute);
        Assert.Equal(RootedExe, info.FileName);
        Assert.Equal("--flag", info.Arguments);
        Assert.Equal(Path.GetDirectoryName(RootedExe), info.WorkingDirectory);
    }

    [Fact]
    public void Bare_command_and_url_have_no_working_directory()
    {
        var bare = LaunchPlanner.Plan(new ShortcutItem { Location = "notepad.exe" });
        var url = LaunchPlanner.Plan(new ShortcutItem { Location = "https://example.com/path" });

        Assert.Equal(string.Empty, bare.StartInfo!.WorkingDirectory);
        Assert.Equal(string.Empty, url.StartInfo!.WorkingDirectory);
        Assert.Equal("https://example.com/path", url.StartInfo.FileName);
    }

    [Fact]
    public void Existing_folder_is_opened_without_working_directory()
    {
        var folder = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        var plan = LaunchPlanner.Plan(new ShortcutItem { Location = folder });

        Assert.Equal(folder, plan.StartInfo!.FileName);
        Assert.Equal(string.Empty, plan.StartInfo.WorkingDirectory);
    }

    [Fact]
    public void Environment_variables_are_expanded()
    {
        Environment.SetEnvironmentVariable("DIG_TEST_DIR", "expanded-dir");
        try
        {
            var plan = LaunchPlanner.Plan(new ShortcutItem { Location = "%DIG_TEST_DIR%/tool", Parameters = "%DIG_TEST_DIR%" });

            Assert.Equal("expanded-dir/tool", plan.StartInfo!.FileName);
            Assert.Equal("expanded-dir", plan.StartInfo.Arguments);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DIG_TEST_DIR", null);
        }
    }

    [Fact]
    public void Parameter_expansion_can_be_skipped_for_already_built_arguments()
    {
        var shortcut = new ShortcutItem { Location = "notepad.exe", Parameters = "\"C:\\reports\\100%done%final.txt\"" };

        Assert.Equal("\"C:\\reports\\100%done%final.txt\"", LaunchPlanner.Plan(shortcut, expandParameters: false).StartInfo!.Arguments);
    }

    [Fact]
    public void Shell_namespace_locations_go_through_explorer()
    {
        var plan = LaunchPlanner.Plan(new ShortcutItem { Location = "shell:MyComputerFolder" });

        Assert.Equal("explorer.exe", plan.StartInfo!.FileName);
        Assert.Equal("shell:MyComputerFolder", plan.StartInfo.Arguments);
    }

    [Fact]
    public void Special_locations_map_to_actions()
    {
        Assert.Equal(LaunchAction.ShowDesktop, LaunchPlanner.Plan(new ShortcutItem { Location = "SPECIAL:Show-Desktop" }).Action);
        Assert.Equal(LaunchAction.RunDialog, LaunchPlanner.Plan(new ShortcutItem { Location = LaunchPlanner.RunDialogLocation }).Action);
        Assert.Throws<ArgumentException>(() => LaunchPlanner.Plan(new ShortcutItem { Location = "special:nope" }));
    }

    [Fact]
    public void Empty_location_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => LaunchPlanner.Plan(new ShortcutItem { Location = "  " }));
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("mailto:someone@example.com", true)]
    [InlineData("ms-settings:display", true)]
    [InlineData("steam://run/123", true)]
    [InlineData("notepad.exe", false)]
    [InlineData("C:\\Windows\\notepad.exe", false)]
    [InlineData("\\\\server\\share\\tool.exe", false)]
    public void IsUri_distinguishes_links_from_paths(string value, bool expected)
    {
        Assert.Equal(expected, LaunchPlanner.IsUri(value));
    }
}
