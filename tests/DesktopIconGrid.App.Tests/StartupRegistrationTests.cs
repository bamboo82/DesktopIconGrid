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

        Assert.True(registration.Apply(startWithWindows: true, startHidden: false));
        Assert.Equal($"\"{Exe}\"", key.Values[StartupRegistration.ValueName]);
        Assert.True(registration.IsRegistered);
        Assert.False(registration.Apply(startWithWindows: true, startHidden: false)); // already right: no write
        Assert.Equal(1, key.Writes);

        Assert.True(registration.Apply(startWithWindows: true, startHidden: true));
        Assert.Equal($"\"{Exe}\" --hidden", key.Values[StartupRegistration.ValueName]);

        Assert.True(registration.Apply(startWithWindows: false, startHidden: true));
        Assert.Empty(key.Values);
        Assert.False(registration.Apply(startWithWindows: false, startHidden: false));
    }

    [Fact]
    public void A_moved_program_repairs_its_entry_at_startup()
    {
        var key = new FakeRunKey();
        key.Values[StartupRegistration.ValueName] = "\"D:\\old place\\DesktopIconGrid.exe\" --hidden";

        var changed = new StartupRegistration(key, Exe).Apply(startWithWindows: true, startHidden: true);

        Assert.True(changed);
        Assert.Equal($"\"{Exe}\" --hidden", key.Values[StartupRegistration.ValueName]);
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
