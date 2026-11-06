using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

public class StartupRegistrationTests
{
    private sealed class FakeRunKey : IRunKey
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Writes { get; private set; }

        public string? GetValue(string name) => Values.GetValueOrDefault(name);

        public void SetValue(string name, string value)
        {
            Writes++;
            Values[name] = value;
        }

        public void DeleteValue(string name)
        {
            Writes++;
            Values.Remove(name);
        }
    }

    private const string Exe = @"C:\Program Files\Desktop Icon Grid\DesktopIconGrid.exe";

    [Fact]
    public void Turning_the_setting_on_writes_the_quoted_command_and_off_removes_it()
    {
        var key = new FakeRunKey();
        var registration = new StartupRegistration(key, Exe);
        Assert.False(registration.IsRegistered);

        Assert.Equal(StartupChange.Written, registration.Apply(startWithWindows: true, startHidden: false));
        Assert.Equal($"\"{Exe}\"", key.Values[StartupRegistration.ValueName]);
        Assert.True(registration.IsRegistered);
        Assert.Equal(StartupChange.None, registration.Apply(startWithWindows: true, startHidden: false)); // already right: no write
        Assert.Equal(1, key.Writes);

        Assert.Equal(StartupChange.Written, registration.Apply(startWithWindows: true, startHidden: true));
        Assert.Equal($"\"{Exe}\" --hidden", key.Values[StartupRegistration.ValueName]);

        Assert.Equal(StartupChange.Removed, registration.Apply(startWithWindows: false, startHidden: true));
        Assert.Empty(key.Values);
        Assert.Equal(StartupChange.None, registration.Apply(startWithWindows: false, startHidden: false));
    }

    [Fact]
    public void A_moved_program_repairs_its_entry_at_startup()
    {
        var key = new FakeRunKey();
        key.Values[StartupRegistration.ValueName] = "\"D:\\old place\\DesktopIconGrid.exe\" --hidden";

        var changed = new StartupRegistration(key, Exe, fileExists: _ => false).Apply(startWithWindows: true, startHidden: true);

        Assert.Equal(StartupChange.Written, changed); // the program it named is gone
        Assert.Equal($"\"{Exe}\" --hidden", key.Values[StartupRegistration.ValueName]);
    }

    [Fact]
    public void A_portable_copy_never_removes_or_replaces_the_installed_bars_entry()
    {
        // The review's R5: starting a portable copy with the option off deleted the installed bar's autostart.
        const string installed = @"C:\Program Files\Desktop Icon Grid\DesktopIconGrid.exe";
        var key = new FakeRunKey();
        key.Values[StartupRegistration.ValueName] = $"\"{installed}\" --hidden";
        var portable = new StartupRegistration(key, @"E:\Tools\DesktopIconGrid\DesktopIconGrid.exe", fileExists: path => path == installed);

        Assert.Equal(StartupChange.None, portable.Apply(startWithWindows: false, startHidden: false));
        Assert.Equal(StartupChange.LeftToAnotherCopy, portable.Apply(startWithWindows: true, startHidden: false));

        Assert.Equal($"\"{installed}\" --hidden", key.Values[StartupRegistration.ValueName]);
        Assert.Equal(0, key.Writes);
        Assert.Equal(installed, portable.RegisteredExecutable);
    }

    [Fact]
    public void An_entry_this_app_did_not_write_is_left_alone()
    {
        var key = new FakeRunKey();
        key.Values[StartupRegistration.ValueName] = @"C:\Tools\launcher.exe /tray";
        var registration = new StartupRegistration(key, Exe, fileExists: _ => false);

        Assert.Equal(StartupChange.None, registration.Apply(startWithWindows: false, startHidden: false));
        Assert.Equal(StartupChange.LeftToAnotherCopy, registration.Apply(startWithWindows: true, startHidden: false));
        Assert.Equal(0, key.Writes);
    }

    [Fact]
    public void Without_leave_to_add_an_entry_only_this_programs_own_is_kept_in_step()
    {
        // A portable copy at start: it removes or updates its own entry, and adds one only when the user asks.
        var key = new FakeRunKey();
        var registration = new StartupRegistration(key, Exe);

        Assert.Equal(StartupChange.NotAdded, registration.Apply(startWithWindows: true, startHidden: false, mayAdd: false));
        Assert.Empty(key.Values);

        Assert.Equal(StartupChange.Written, registration.Apply(startWithWindows: true, startHidden: false, mayAdd: true));
        Assert.Equal(StartupChange.Written, registration.Apply(startWithWindows: true, startHidden: true, mayAdd: false)); // its own: updated
        Assert.Equal($"\"{Exe}\" --hidden", key.Values[StartupRegistration.ValueName]);
        Assert.Equal(StartupChange.Removed, registration.Apply(startWithWindows: false, startHidden: true, mayAdd: false));
    }

    [Theory]
    [InlineData("\"C:\\a b\\x.exe\"", "C:\\a b\\x.exe", false)]
    [InlineData("\"C:\\a b\\x.exe\" --hidden", "C:\\a b\\x.exe", true)]
    [InlineData("\"C:\\a b\\x.exe\"   --HIDDEN", "C:\\a b\\x.exe", true)]
    public void Commands_round_trip(string command, string executable, bool hidden)
    {
        Assert.True(StartupRegistration.TryParse(command, out var parsedExecutable, out var parsedHidden));
        Assert.Equal(executable, parsedExecutable);
        Assert.Equal(hidden, parsedHidden);
        Assert.Equal(command.Contains("--hidden", StringComparison.OrdinalIgnoreCase) ? StartupRegistration.BuildCommand(executable, true) : StartupRegistration.BuildCommand(executable, false),
            StartupRegistration.BuildCommand(parsedExecutable, parsedHidden));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C:\\unquoted\\x.exe")]
    [InlineData("\"\"")]
    [InlineData("\"C:\\x.exe\" --other")]
    public void Foreign_values_are_not_parsed(string? command)
    {
        Assert.False(StartupRegistration.TryParse(command, out _, out _));
    }
}
