using System.IO;
using System.Security;
using Microsoft.Win32;

namespace DesktopIconGrid.Services;

/// <summary>Where Windows keeps installed programs, for the Repair dialog.</summary>
public static class InstalledPrograms
{
    /// <summary>Program Files, Program Files (x86) and <c>%LocalAppData%\Programs</c>, the ones that exist.</summary>
    public static IReadOnlyList<string> Folders()
    {
        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "Programs");
        return new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolderOption.DoNotVerify),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolderOption.DoNotVerify),
                localPrograms,
            }
            .Where(f => !string.IsNullOrWhiteSpace(f) && Directory.Exists(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The program registered under <c>App Paths</c> for a file name such as <c>WinSCP.exe</c> (the current user's
    /// entry first), when it exists; <see langword="null"/> otherwise.
    /// </summary>
    public static string? RegisteredPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(['\\', '/']) >= 0 || !OperatingSystem.IsWindows())
        {
            return null;
        }

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{fileName}");
                if (key?.GetValue(null) is string value && !string.IsNullOrWhiteSpace(value))
                {
                    var path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                // An entry that cannot be read is simply not offered.
            }
        }

        return null;
    }
}
