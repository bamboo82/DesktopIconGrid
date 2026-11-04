using DesktopIconGrid.Core.Launching;

namespace DesktopIconGrid.Core.Tests;

public sealed class ExecutableLocatorTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-locator-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Rooted_paths_are_found_as_given_or_with_a_default_extension()
    {
        var exe = Path.Combine(_directory, "tool.exe");
        File.WriteAllBytes(exe, []);

        Assert.Equal(exe, ExecutableLocator.Find(exe));
        Assert.Equal(exe, ExecutableLocator.Find(Path.Combine(_directory, "tool")));
        Assert.Null(ExecutableLocator.Find(Path.Combine(_directory, "tool.com")));
        Assert.Null(ExecutableLocator.Find(Path.Combine(_directory, "other")));
        Assert.True(ExecutableLocator.Exists(exe));
        Assert.False(ExecutableLocator.Exists(Path.Combine(_directory, "other")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_is_not_found(string executable) => Assert.Null(ExecutableLocator.Find(executable));
}
