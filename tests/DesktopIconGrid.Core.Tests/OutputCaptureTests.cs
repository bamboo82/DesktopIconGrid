using System.Diagnostics;
using DesktopIconGrid.Core.Launching;

namespace DesktopIconGrid.Core.Tests;

public class OutputCaptureTests
{
    [Theory]
    [InlineData("C:\\Tools\\build.exe", true)]
    [InlineData("C:\\Tools\\run.BAT", true)]
    [InlineData("C:\\Tools\\run.cmd", true)]
    [InlineData("C:\\Docs\\readme.txt", false)]
    [InlineData("https://example.com", false)]
    [InlineData("", false)]
    public void CanCapture_depends_on_the_file_kind(string fileName, bool expected)
    {
        Assert.Equal(expected, OutputCapture.CanCapture(new ProcessStartInfo(fileName)));
    }

    [Fact]
    public void WithCapture_redirects_output_and_keeps_working_directory()
    {
        var source = new ProcessStartInfo("C:\\Tools\\build.exe") { Arguments = "--all", WorkingDirectory = "C:\\Tools", UseShellExecute = true };

        var captured = OutputCapture.WithCapture(source);

        Assert.Equal("C:\\Tools\\build.exe", captured.FileName);
        Assert.Equal("--all", captured.Arguments);
        Assert.Equal("C:\\Tools", captured.WorkingDirectory);
        Assert.False(captured.UseShellExecute);
        Assert.True(captured.RedirectStandardOutput);
        Assert.True(captured.RedirectStandardError);
        Assert.False(captured.RedirectStandardInput);
        Assert.True(captured.CreateNoWindow);
    }

    [Fact]
    public void WithCapture_runs_batch_files_through_cmd()
    {
        var captured = OutputCapture.WithCapture(new ProcessStartInfo("C:\\My Tools\\run.bat") { Arguments = "one two" });

        Assert.EndsWith("cmd.exe", captured.FileName, StringComparison.OrdinalIgnoreCase); // by its full path on Windows
        Assert.Equal("/d /c \"\"C:\\My Tools\\run.bat\" one two\"", captured.Arguments);

        var noArguments = OutputCapture.WithCapture(new ProcessStartInfo("C:\\x.cmd"));
        Assert.Equal("/d /c \"\"C:\\x.cmd\"\"", noArguments.Arguments);
    }
}
