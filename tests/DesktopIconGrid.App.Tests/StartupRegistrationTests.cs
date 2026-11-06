using System.IO;
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

    /// <summary>A drive letter with no drive behind it on this PC (GetDriveType's NoRootDirectory).</summary>
    internal static char UnusedDriveLetter()
    {
        for (var letter = 'Z'; letter >= 'D'; letter--)
        {
            if (IconWorker.DriveTypeOf(letter + @":\") == DriveType.NoRootDirectory)
            {
                return letter;
            }
        }

        throw new InvalidOperationException("Every drive letter is in use");
    }

    [Fact]
    public void An_entry_on_a_drive_letter_this_PC_lacks_names_a_program_that_is_gone()
    {
        // Review 15 R5′: a stick's entry (E:) counted as another copy that is still there once the stick was gone, so
        // the installed bar could neither replace nor remove it. GetDriveType says so without touching any drive.
        var letter = UnusedDriveLetter();
        Assert.False(IconWorker.IsOnSlowStorage(letter + @":\DIG\DesktopIconGrid.exe"));
        var key = new FakeRunKey();
        key.Values[StartupRegistration.ValueName] = $"\"{letter}:\\DIG\\DesktopIconGrid.exe\" --hidden";
        var installed = new StartupRegistration(key, Exe); // the default look

        Assert.Equal(StartupChange.Written, installed.Apply(startWithWindows: true, startHidden: false));
        Assert.Equal($"\"{Exe}\"", key.Values[StartupRegistration.ValueName]);
    }

    [Fact]
    public void A_portable_copy_counts_its_entry_from_another_drive_letter_as_its_own_but_never_a_fixed_disks()
    {
        const string stickBefore = @"E:\DIG\DesktopIconGrid.exe";
        const string stickNow = @"F:\DIG\DesktopIconGrid.exe";
        static bool OnFixedDisk(string path) => path.StartsWith("C:", StringComparison.OrdinalIgnoreCase);

        // The stick, back as F:, can remove the entry it made as E: ...
        var key = new FakeRunKey();
        key.Values[StartupRegistration.ValueName] = $"\"{stickBefore}\"";
        var portable = new StartupRegistration(key, stickNow, fileExists: _ => false, portable: true, couldBeThisStick: path => !OnFixedDisk(path));
        Assert.Equal(StartupChange.Removed, portable.Apply(startWithWindows: false, startHidden: false));

        // ... or keep it in step, without asking: it is its own entry.
        key.Values[StartupRegistration.ValueName] = $"\"{stickBefore}\"";
        Assert.Equal(StartupChange.Written, portable.Apply(startWithWindows: true, startHidden: true, mayAdd: false));
        Assert.Equal($"\"{stickNow}\" --hidden", key.Values[StartupRegistration.ValueName]);

        // The same folders on C: are the installed bar's, not the stick's.
        const string installed = @"C:\DIG\DesktopIconGrid.exe";
        key.Values[StartupRegistration.ValueName] = $"\"{installed}\"";
        var fromE = new StartupRegistration(key, stickBefore, fileExists: path => path == installed, portable: true, couldBeThisStick: path => !OnFixedDisk(path));
        Assert.Equal(StartupChange.None, fromE.Apply(startWithWindows: false, startHidden: false));
        Assert.Equal(StartupChange.LeftToAnotherCopy, fromE.Apply(startWithWindows: true, startHidden: false));
        Assert.Equal($"\"{installed}\"", key.Values[StartupRegistration.ValueName]);

        // An installed copy has no such rule.
        key.Values[StartupRegistration.ValueName] = $"\"{stickBefore}\"";
        var notPortable = new StartupRegistration(key, stickNow, fileExists: _ => true, couldBeThisStick: path => !OnFixedDisk(path));
        Assert.Equal(StartupChange.None, notPortable.Apply(startWithWindows: false, startHidden: false));
    }

    [Fact]
    public void A_network_letter_with_the_same_folders_is_never_the_sticks()
    {
        // Review 16 R9: a Tools share mapped as T:, set to start with Windows, lost its entry to a stick copy of it.
        var key = new FakeRunKey();
        key.Values[StartupRegistration.ValueName] = "\"T:\\Tools\\DIG\\DesktopIconGrid.exe\"";
        var stick = new StartupRegistration(key, @"F:\Tools\DIG\DesktopIconGrid.exe", fileExists: _ => true, portable: true,
            couldBeThisStick: path => path.StartsWith("E:", StringComparison.OrdinalIgnoreCase)); // T: is a network letter here

        Assert.Equal(StartupChange.None, stick.Apply(startWithWindows: false, startHidden: false));
        Assert.Equal(StartupChange.LeftToAnotherCopy, stick.Apply(startWithWindows: true, startHidden: false));
        Assert.Equal("\"T:\\Tools\\DIG\\DesktopIconGrid.exe\"", key.Values[StartupRegistration.ValueName]);
    }

    [Fact]
    public void Taking_over_a_gone_programs_entry_needs_leave_to_add_one()
    {
        // Review 15 R5″: a portable copy with the option on wrote its entry over a leftover one at start, without asking.
        var key = new FakeRunKey();
        key.Values[StartupRegistration.ValueName] = "\"C:\\Old\\DesktopIconGrid.exe\"";
        var registration = new StartupRegistration(key, Exe, fileExists: _ => false);

        Assert.Equal(StartupChange.NotAdded, registration.Apply(startWithWindows: true, startHidden: false, mayAdd: false));
        Assert.Equal(0, key.Writes);
        Assert.Equal(StartupChange.Written, registration.Apply(startWithWindows: true, startHidden: false, mayAdd: true));
        Assert.Equal($"\"{Exe}\"", key.Values[StartupRegistration.ValueName]);
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
