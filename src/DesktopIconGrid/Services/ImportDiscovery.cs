using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using Microsoft.Win32;

namespace DesktopIconGrid.Services;

/// <summary>
/// Finds what <c>Import ▸</c> offers on this PC: the taskbar's pinned shortcuts, the desktop's shortcuts and links,
/// Windows Terminal profiles, WinSCP and PuTTY sessions, and the hosts in the SSH config. A source that cannot be read
/// is logged and skipped; the others are still offered. Runs on the icon worker (COM reads the <c>.lnk</c> files).
/// </summary>
public static class ImportDiscovery
{
    public static IReadOnlyList<ImportCandidate> FindAll(IReadOnlyList<ShortcutItem> onBar)
    {
        var found = new List<ImportCandidate>();
        foreach (var (source, find) in new (ImportSource, Func<IEnumerable<ImportCandidate>>)[]
                 {
                     (ImportSource.TaskbarPins, TaskbarPins),
                     (ImportSource.Desktop, DesktopShortcuts),
                     (ImportSource.WindowsTerminal, TerminalProfiles),
                     (ImportSource.WinScp, () => WinScpSessions(onBar)),
                     (ImportSource.Putty, PuttySessions),
                     (ImportSource.SshHosts, SshHosts),
                 })
        {
            try
            {
                found.AddRange(find());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or COMException or ArgumentException)
            {
                AppLog.Default.Warning("Import", $"{source} could not be read: {ex.Message}");
            }
        }

        var usable = found.Where(ImportPlan.IsUsable).ToList();
        if (usable.Count < found.Count)
        {
            AppLog.Default.Warning("Import", $"{found.Count - usable.Count} name(s) left out: a quote, a line break, a leading \"-\" or what the program after them would act on");
        }

        return usable;
    }

    private static IEnumerable<ImportCandidate> TaskbarPins()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
        return LinksIn(folder, ImportSource.TaskbarPins);
    }

    private static IEnumerable<ImportCandidate> DesktopShortcuts() =>
        LinksIn(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ImportSource.Desktop)
            .Concat(LinksIn(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), ImportSource.Desktop));

    /// <summary>The <c>.lnk</c> files (resolved to their target where it is a file) and <c>.url</c> links in a folder.</summary>
    private static List<ImportCandidate> LinksIn(string folder, ImportSource source)
    {
        var found = new List<ImportCandidate>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return found;
        }

        foreach (var path in Directory.EnumerateFiles(folder).OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".lnk":
                    found.Add(FromLink(path, name, source));
                    break;
                case ".url" when UrlOf(path) is { } url:
                    found.Add(new ImportCandidate(source, name, url));
                    break;
            }
        }

        return found;
    }

    /// <summary>A <c>.lnk</c> with a file target becomes that target (so the running badge and switching work); otherwise the <c>.lnk</c> itself.</summary>
    private static ImportCandidate FromLink(string path, string name, ImportSource source)
    {
        var (target, arguments, folder) = ReadLink(path);
        if (string.IsNullOrWhiteSpace(target) || !File.Exists(Environment.ExpandEnvironmentVariables(target)))
        {
            return new ImportCandidate(source, name, path);
        }

        var startIn = string.IsNullOrWhiteSpace(folder)
                      || string.Equals(Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(target)), Environment.ExpandEnvironmentVariables(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            ? null
            : folder;
        return new ImportCandidate(source, name, target, string.IsNullOrWhiteSpace(arguments) ? null : arguments, startIn);
    }

    private static (string? Target, string? Arguments, string? Folder) ReadLink(string path)
    {
        var link = (NativeMethods.IShellLinkW)new NativeMethods.CShellLink();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            return (
                ReadText((buffer, size) => link.GetPath(buffer, size, IntPtr.Zero, NativeMethods.SLGP_RAWPATH)),
                ReadText(link.GetArguments),
                ReadText(link.GetWorkingDirectory));
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    private static string? ReadText(Func<IntPtr, int, int> read)
    {
        const int Characters = 2048;
        var buffer = Marshal.AllocCoTaskMem(Characters * sizeof(char));
        try
        {
            Marshal.WriteInt16(buffer, 0);
            return read(buffer, Characters) == 0 ? Marshal.PtrToStringUni(buffer)?.Trim() : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    private static string? UrlOf(string urlFile) =>
        File.ReadLines(urlFile)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..].Trim() is { Length: > 0 } url
            ? url
            : null;

    private static IEnumerable<ImportCandidate> TerminalProfiles()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settings = new[]
            {
                Path.Combine(local, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json"),
                Path.Combine(local, "Packages", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "LocalState", "settings.json"),
                Path.Combine(local, "Microsoft", "Windows Terminal", "settings.json"),
            }
            .FirstOrDefault(File.Exists);
        return settings is null
            ? []
            : ImportSources.TerminalProfiles(File.ReadAllText(settings))
                .Select(profile => new ImportCandidate(ImportSource.WindowsTerminal, profile, "wt.exe", $"-p \"{profile}\""));
    }

    /// <summary>WinSCP sessions from its registry key and from the <c>WinSCP.ini</c> of a portable WinSCP on the bar.</summary>
    private static IEnumerable<ImportCandidate> WinScpSessions(IReadOnlyList<ShortcutItem> onBar)
    {
        var onBarExe = onBar
            .Select(s => Environment.ExpandEnvironmentVariables(s.EffectiveLocation.Trim()))
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), "WinSCP.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path));
        var exe = onBarExe ?? ProgramPath("WinSCP.exe", "WinSCP");
        var names = new List<string>(SubKeyNames(@"Software\Martin Prikryl\WinSCP 2\Sessions"));
        var ini = Path.Combine(Path.GetDirectoryName(exe) ?? string.Empty, "WinSCP.ini");
        if (onBarExe is not null && File.Exists(ini))
        {
            names.AddRange(ImportSources.WinScpIniSessions(File.ReadAllText(ini)).Select(Uri.EscapeDataString));
        }

        return ImportSources.SessionNames(names)
            .Select(session => new ImportCandidate(ImportSource.WinScp, session, exe, $"\"{session}\""));
    }

    private static IEnumerable<ImportCandidate> PuttySessions()
    {
        var exe = ProgramPath("putty.exe", "PuTTY");
        return ImportSources.SessionNames(SubKeyNames(@"Software\SimonTatham\PuTTY\Sessions"))
            .Select(session => new ImportCandidate(ImportSource.Putty, session, exe, $"-load \"{session}\""));
    }

    /// <summary>The hosts of <c>%UserProfile%\.ssh\config</c>, opened in Windows Terminal when it is installed, else in ssh.exe.</summary>
    private static IEnumerable<ImportCandidate> SshHosts()
    {
        var config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");
        if (!File.Exists(config))
        {
            return [];
        }

        var terminal = ExecutableLocator.Find("wt.exe") is not null;
        return ImportSources.SshHosts(File.ReadAllText(config))
            .Select(host => terminal
                ? new ImportCandidate(ImportSource.SshHosts, host, "wt.exe", $"ssh {host}")
                : new ImportCandidate(ImportSource.SshHosts, host, "ssh.exe", host));
    }

    /// <summary>A program by its App Paths entry, then its usual folders, then PATH; its bare name when none has it.</summary>
    private static string ProgramPath(string fileName, string folderName) =>
        InstalledPrograms.RegisteredPath(fileName)
        ?? InstalledPrograms.Folders().Select(root => Path.Combine(root, folderName, fileName)).FirstOrDefault(File.Exists)
        ?? ExecutableLocator.Find(fileName)
        ?? fileName;

    private static string[] SubKeyNames(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetSubKeyNames() ?? [];
    }
}
