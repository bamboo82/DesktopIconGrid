using DesktopIconGrid.Core.Imports;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Import ▸: reading what other programs keep.</summary>
public class ImportSourcesTests
{
    [Fact]
    public void Windows_Terminal_profiles_are_read_with_comments_and_without_hidden_ones()
    {
        const string settings = """
            // Windows Terminal settings
            {
                "defaultProfile": "{61c54bbd}",
                "profiles": {
                    "defaults": {},
                    "list": [
                        { "guid": "{61c54bbd}", "name": "Windows PowerShell" },
                        { "guid": "{0caa0dad}", "name": "Command Prompt", "hidden": false, },
                        { "guid": "{b453ae62}", "name": "Azure Cloud Shell", "hidden": true },
                        { "guid": "{2c4de342}", "name": " Ubuntu " },
                    ]
                }
            }
            """;

        Assert.Equal(["Windows PowerShell", "Command Prompt", "Ubuntu"], ImportSources.TerminalProfiles(settings));
        Assert.Equal(["Old style"], ImportSources.TerminalProfiles("""{ "profiles": [ { "name": "Old style" } ] }"""));
        Assert.Empty(ImportSources.TerminalProfiles("not json"));
        Assert.Empty(ImportSources.TerminalProfiles(null));
    }

    [Fact]
    public void SSH_hosts_are_the_aliases_without_patterns()
    {
        const string config = """
            Host *
                ServerAliveInterval 60
            Host build-server  build2 # the CI boxes
                HostName 10.0.0.5
            host nas
            Host *.internal !secret db?
            Host "quoted"
              User me
            """;

        Assert.Equal(["build-server", "build2", "nas", "quoted"], ImportSources.SshHosts(config));
        Assert.Empty(ImportSources.SshHosts(""));
    }

    [Fact]
    public void Saved_sessions_are_decoded_and_the_defaults_left_out()
    {
        const string ini = """
            [Configuration\Interface]
            Theme=1
            [Sessions\Default%20Settings]
            [Sessions\My%20Server]
            HostName=example.com
            [Sessions\Work/db%40prod]
            """;

        Assert.Equal(["My Server", "Work/db@prod"], ImportSources.WinScpIniSessions(ini));
        Assert.Equal(["router", "50% off"], ImportSources.SessionNames(["router", "Default%20Settings", "50% off", "router"]));
        Assert.Equal("100% bad", ImportSources.Decode("100% bad"));
    }

    [Fact]
    public void A_candidate_is_known_by_its_location_with_its_parameters()
    {
        Assert.Equal("wt.exe ssh nas", new ImportCandidate(ImportSource.SshHosts, "nas", "wt.exe", " ssh nas ").Key);
        Assert.Equal(@"C:\Tools\a.exe", new ImportCandidate(ImportSource.Desktop, "a", @" C:\Tools\a.exe ").Key);
    }
}
