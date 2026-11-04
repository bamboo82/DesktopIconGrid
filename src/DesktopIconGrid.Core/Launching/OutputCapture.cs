using System.Diagnostics;

namespace DesktopIconGrid.Core.Launching;

/// <summary>
/// Prepares a shortcut's process for output logging: console programs and batch files are started
/// directly with their standard output and error redirected and without a console window.
/// </summary>
public static class OutputCapture
{
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".com" };
    private static readonly HashSet<string> BatchExtensions = new(StringComparer.OrdinalIgnoreCase) { ".bat", ".cmd" };

    /// <summary>True when the planned process can be started with redirected output (an executable, a batch file, or a bare command found on PATH).</summary>
    public static bool CanCapture(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        var file = startInfo.FileName?.Trim() ?? string.Empty;
        if (file.Length == 0 || LaunchPlanner.IsUri(file))
        {
            return false;
        }

        var extension = Path.GetExtension(file);
        if (ExecutableExtensions.Contains(extension) || BatchExtensions.Contains(extension))
        {
            return true;
        }

        return extension.Length == 0 && ExecutableLocator.Find(file + ".exe") is not null;
    }

    /// <summary>A copy of <paramref name="source"/> that redirects output; batch files run through <c>cmd.exe /c</c>.</summary>
    public static ProcessStartInfo WithCapture(ProcessStartInfo source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ProcessStartInfo captured;
        if (BatchExtensions.Contains(Path.GetExtension(source.FileName)))
        {
            var arguments = string.IsNullOrWhiteSpace(source.Arguments) ? string.Empty : " " + source.Arguments;
            captured = new ProcessStartInfo("cmd.exe") { Arguments = $"/d /c \"\"{source.FileName}\"{arguments}\"" };
        }
        else
        {
            captured = new ProcessStartInfo(source.FileName) { Arguments = source.Arguments };
        }

        captured.WorkingDirectory = source.WorkingDirectory;
        captured.UseShellExecute = false;
        captured.RedirectStandardOutput = true;
        captured.RedirectStandardError = true;
        captured.RedirectStandardInput = false;
        captured.CreateNoWindow = true;
        return captured;
    }
}
