using System.Diagnostics;
using System.Text.Json;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>The options every <c>.lnk</c> has: start folder, elevation, window state, browser profile, and two placeholders.</summary>
public class LaunchOptionsTests
{
    private static ProcessStartInfo Plan(ShortcutItem shortcut) => LaunchPlanner.Plan(shortcut).StartInfo!;

    [Fact]
    public void Start_in_replaces_the_program_s_folder_and_expands_variables()
    {
        Environment.SetEnvironmentVariable("DIG_TEST_WORK", "/work");
        Assert.Equal("/work/project", Plan(new ShortcutItem { Location = @"C:\Tools\app.exe", StartIn = " %DIG_TEST_WORK%/project " }).WorkingDirectory);
        Assert.Equal(LaunchPlanner.DefaultWorkingDirectory(@"C:\Tools\app.exe") ?? string.Empty, Plan(new ShortcutItem { Location = @"C:\Tools\app.exe", StartIn = "  " }).WorkingDirectory);
    }

    [Fact]
    public void Run_as_administrator_asks_for_elevation_and_the_window_state_is_passed_on()
    {
        var start = Plan(new ShortcutItem { Location = @"C:\Tools\app.exe", RunAsAdministrator = true, WindowState = ShortcutItem.WindowMaximized });

        Assert.Equal(LaunchPlanner.RunAsVerb, start.Verb);
        Assert.Equal(ProcessWindowStyle.Maximized, start.WindowStyle);
        Assert.Equal(ProcessWindowStyle.Minimized, Plan(new ShortcutItem { Location = @"C:\Tools\app.exe", WindowState = "Minimized" }).WindowStyle);
        Assert.Equal(ProcessWindowStyle.Normal, Plan(new ShortcutItem { Location = @"C:\Tools\app.exe" }).WindowStyle);
        Assert.Equal(string.Empty, Plan(new ShortcutItem { Location = "https://example.com", RunAsAdministrator = true }).Verb);
    }

    [Fact]
    public void A_web_link_opens_in_the_chosen_browser_profile_or_as_an_app_window()
    {
        var edge = Plan(new ShortcutItem { Location = "https://mail.example.com/inbox", Browser = "edge", BrowserProfile = "Profile 1", AppWindow = true });
        Assert.Equal("msedge.exe", edge.FileName);
        Assert.Equal("--profile-directory=\"Profile 1\" --app=\"https://mail.example.com/inbox\"", edge.Arguments);

        var chrome = Plan(new ShortcutItem { Location = "https://example.com", Browser = "Chrome", Parameters = "--incognito" });
        Assert.Equal("chrome.exe", chrome.FileName);
        Assert.Equal("\"https://example.com\" --incognito", chrome.Arguments);

        Assert.Equal("https://example.com", Plan(new ShortcutItem { Location = "https://example.com", Browser = "netscape" }).FileName);
        Assert.Equal(@"C:\Docs\a.pdf", Plan(new ShortcutItem { Location = @"C:\Docs\a.pdf", Browser = "edge" }).FileName); // not a web link
        Assert.Equal("mailto:me@example.com", Plan(new ShortcutItem { Location = "mailto:me@example.com", Browser = "edge" }).FileName);
    }

    [Fact]
    public void Every_option_round_trips_through_the_layout_file_and_is_copied_by_edits()
    {
        var shortcut = new ShortcutItem
        {
            Location = "https://example.com",
            StartIn = @"C:\Work",
            RunAsAdministrator = true,
            WindowState = ShortcutItem.WindowMinimized,
            Browser = "edge",
            BrowserProfile = "Profile 2",
            AppWindow = true,
            WhenRunning = ShortcutItem.WhenRunningSwitch,
        };

        var json = JsonSerializer.Serialize<LauncherItem>(shortcut, LayoutStorage.JsonOptions);
        var read = Assert.IsType<ShortcutItem>(JsonSerializer.Deserialize<LauncherItem>(json, LayoutStorage.JsonOptions));
        var copy = new ShortcutItem();
        copy.CopyContentFrom(read);

        foreach (var item in new[] { read, copy })
        {
            Assert.Equal(@"C:\Work", item.StartIn);
            Assert.True(item.RunAsAdministrator);
            Assert.Equal(ShortcutItem.WindowMinimized, item.WindowState);
            Assert.Equal("edge", item.Browser);
            Assert.Equal("Profile 2", item.BrowserProfile);
            Assert.True(item.AppWindow);
            Assert.Equal(ShortcutItem.WhenRunningSwitch, item.WhenRunning);
        }

        var plain = JsonSerializer.Serialize<LauncherItem>(new ShortcutItem { Location = "x" }, LayoutStorage.JsonOptions);
        Assert.DoesNotContain("startIn", plain); // unset options cost nothing in the file
        Assert.DoesNotContain("browser", plain);
    }

    [Fact]
    public void Placeholders_are_found_in_order()
    {
        Assert.True(LaunchPlaceholders.HasAny("--text {Clipboard}"));
        Assert.True(LaunchPlaceholders.HasAny("--host {ask:Host name}"));
        Assert.False(LaunchPlaceholders.HasAny("{file} --x"));
        Assert.False(LaunchPlaceholders.HasAny(null));
        Assert.Equal(["Host name", "Port", string.Empty], LaunchPlaceholders.Prompts("-h {ask: Host name } -p \"{ask:Port}\" {ask:}"));
        Assert.True(LaunchPlaceholders.UsesClipboard("x \"{clipboard}\""));
    }

    [Fact]
    public void Values_are_quoted_like_dropped_files_and_never_expanded()
    {
        var filled = LaunchPlaceholders.Fill(
            "--title \"{ask:Title}\" --body {clipboard} --to {ask:To} {clipboard}",
            clipboard: "say \"hi\" to %USERNAME%",
            answers: ["My report", @"C:\Out\"]);

        Assert.Equal(
            "--title \"My report\" --body \"say \\\"hi\\\" to %USERNAME%\" --to \"C:\\Out\\\\\" \"say \\\"hi\\\" to %USERNAME%\"",
            filled);
        Assert.Throws<ArgumentException>(() => LaunchPlaceholders.Fill("{ask:A} {ask:B}", null, ["only one"]));
    }

    [Fact]
    public void Every_placeholder_goes_in_with_one_pass_so_a_value_keeps_a_placeholder_as_text()
    {
        // Review 15 P1: an answer that held {clipboard} got the clipboard put inside it, and a value that held {file}
        // got a dropped path inside it, outside its quotes and after the check.
        Assert.Equal("\"{clipboard}\" \"text\"", LaunchPlaceholders.Fill("{ask:A} {clipboard}", "text", ["{clipboard}"]));

        var sets = DropArguments.BuildArgumentSets("{clipboard}", [@"C:\t\Invoice&calc&.pdf"], clipboard: "x{file}");
        Assert.Equal("\"x{file}\" \"C:\\t\\Invoice&calc&.pdf\"", Assert.Single(sets)); // the file goes at the end, as without {file}

        var each = DropArguments.BuildArgumentSets("--from {clipboard} \"{file}\"", [@"C:\a.txt", @"C:\b.txt"], clipboard: "{files}");
        Assert.Equal(["--from \"{files}\" \"C:\\a.txt\"", "--from \"{files}\" \"C:\\b.txt\""], each);
    }

    [Theory]
    [InlineData("\"--in={file}\"")] // review 16 P3: a batch file's &calc& stood outside every quote
    [InlineData("git commit -m \"WIP {clipboard}\"")] // bash got "WIP "x;calc.exe #"
    [InlineData("\"--message=WIP {clipboard}\"")] // a value with a space split into two arguments
    [InlineData("\"-oProxyCommand {clipboard} %h %p\" server2")] // review 16 P2′ a: a quote opened before the option
    [InlineData("\"-o ProxyCommand {clipboard}\" server2")]
    [InlineData("-o\"ProxyCommand {clipboard}\" server2")]
    [InlineData("\"{clipboard} more\"")]
    [InlineData("\"C:\\Program Files\\app.exe {ask:File}")] // a quote never closed
    public void A_placeholder_inside_quotes_with_other_text_is_found(string template)
    {
        Assert.True(LaunchPlaceholders.InsideQuotes(template));
        Assert.Throws<ArgumentException>(() => LaunchPlaceholders.Fill(template, "x", ["y"], file: "z"));
    }

    [Theory]
    [InlineData("--title \"{ask:Title}\" {clipboard}")] // alone in quotes, and bare
    [InlineData("--in={file} \"{files}\"x")]
    [InlineData("\"a b\" {clipboard} \"c d\"")] // quoted spans around it, closed
    [InlineData("{ask:Say \"hi\"} {clipboard}")] // quotes in a prompt are the prompt's
    [InlineData("\\\"{clipboard}")] // an escaped quote is a character
    [InlineData("\"say \"\"hi\"\"\" {clipboard}")] // "" inside a span is a quote character
    public void A_placeholder_alone_or_outside_quotes_is_fine(string template)
    {
        Assert.False(LaunchPlaceholders.InsideQuotes(template));
    }

    [Fact]
    public void A_backslash_run_before_a_placeholder_is_doubled_and_a_quoted_pair_gives_way()
    {
        // Review 16 P3: %USERPROFILE%\{ask:File} became C:\Users\me\"a b", where \" is a quote character.
        Assert.Equal("C:\\Users\\me\\\\\"a b\"", LaunchPlaceholders.Fill("C:\\Users\\me\\{ask:File}", null, ["a b"]));
        Assert.Equal("--in=\"a b\" \"c\"", LaunchPlaceholders.Fill("--in=\"{ask:A}\" {clipboard}", "c", ["a b"]));
        Assert.Equal("\"C:\\t\\a&calc&.txt\"", Assert.Single(DropArguments.BuildArgumentSets("\"{file}\"", [@"C:\t\a&calc&.txt"])));
    }

    [Fact]
    public void Browser_profiles_are_read_from_Local_State()
    {
        const string localState = """
            {
              "profile": {
                "info_cache": {
                  "Default": { "name": "Personal" },
                  "Profile 1": { "name": "Work" },
                  "Profile 2": { "gaia_name": "no name" }
                },
                "last_used": "Profile 1"
              }
            }
            """;

        Assert.Equal(
            [new BrowserProfile("Default", "Personal"), new BrowserProfile("Profile 2", "Profile 2"), new BrowserProfile("Profile 1", "Work")],
            BrowserProfiles.Parse(localState));
        Assert.Empty(BrowserProfiles.Parse("{ \"profile\": 3 }"));
        Assert.Empty(BrowserProfiles.Parse("not json"));
        Assert.Empty(BrowserProfiles.Parse(null));
        Assert.Equal("msedge.exe", BrowserProfiles.Find(" EDGE ")!.Executable);
        Assert.Null(BrowserProfiles.Find("lynx"));
    }
}
