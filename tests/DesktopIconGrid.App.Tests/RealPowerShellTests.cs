using System.Diagnostics;
using DesktopIconGrid.Core.Launching;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// The command lines the app builds for PowerShell script text, run by the real powershell.exe and pwsh: a value is
/// either refused or comes back as written, never evaluated. The review of round 11 found that the quotes the app adds
/// never reach the script, so <c>Write-Output "(1+1)"</c> printed 2.
/// </summary>
public sealed class RealPowerShellTests
{
    private const string Template = "-NoProfile -NonInteractive -Command Write-Output {clipboard}";

    /// <summary>
    /// Runs <paramref name="program"/> with <paramref name="arguments"/> and returns what it printed, one line per output
    /// object; what it wrote to the error stream goes into the assertion message.
    /// </summary>
    private static string[] Run(string program, string arguments)
    {
        var start = new ProcessStartInfo(program, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} did not start");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60_000), $"{program} did not finish");
        var lines = output.Result.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines : [$"(nothing printed; errors: {errors.Result.Trim()})"];
    }

    [Theory]
    [InlineData("powershell.exe")]
    [InlineData("pwsh.exe")]
    public void The_danger_is_real_PowerShell_evaluates_a_quoted_value_in_Command(string program)
    {
        // What 0.8.0 before this fix let through: no refused character, and PowerShell computes it.
        Assert.Equal(["2"], Run(program, LaunchPlaceholders.Fill(Template, "(1+1)", [])));
    }

    [Theory]
    [InlineData("powershell.exe")]
    [InlineData("pwsh.exe")]
    public void The_danger_is_real_a_word_of_the_value_that_starts_with_a_dash_is_a_parameter(string program)
    {
        // The review of round 12: -NoEnumerate is taken by Write-Output, so only "x" is printed.
        Assert.Equal(["x"], Run(program, LaunchPlaceholders.Fill(Template, "x -NoEnumerate", [])));
    }

    [Theory]
    [InlineData("powershell.exe", "x -NoEnumerate")]
    [InlineData("pwsh.exe", "x -NoEnumerate")]
    [InlineData("powershell.exe", "(1+1)")]
    [InlineData("powershell.exe", "x|Measure-Object|Select-Object -Expand Count")]
    [InlineData("powershell.exe", "a;b")]
    [InlineData("powershell.exe", "a\u2019,(1+1),\u2019b")]
    [InlineData("powershell.exe", @"C:\My Files\a.txt")]
    [InlineData("powershell.exe", "report-2026_v2.final")]
    [InlineData("pwsh.exe", "(1+1)")]
    [InlineData("pwsh.exe", "x|Measure-Object|Select-Object -Expand Count")]
    [InlineData("pwsh.exe", "a&b")]
    [InlineData("pwsh.exe", "a;b")]
    [InlineData("pwsh.exe", "a\u2019,(1+1),\u2019b")]
    [InlineData("pwsh.exe", @"C:\My Files\a.txt")]
    [InlineData("pwsh.exe", "report-2026_v2.final")]
    public void A_value_is_refused_or_comes_back_as_written(string program, string value)
    {
        Assert.Equal(ArgumentReader.PowerShellCommand, LaunchPlaceholders.ReaderOf(program, Template));
        if (LaunchPlaceholders.Refused(value, ArgumentReader.PowerShellCommand) is not null)
        {
            return; // refused: nothing starts
        }

        // Write-Output prints each word on a line of its own: the words are the value's, unevaluated.
        Assert.Equal(value, string.Join(' ', Run(program, LaunchPlaceholders.Fill(Template, value, []))));
    }
}
