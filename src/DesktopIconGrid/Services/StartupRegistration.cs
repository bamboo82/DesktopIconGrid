using System.IO;
using Microsoft.Win32;

namespace DesktopIconGrid.Services;

/// <summary>The one registry key the startup registration touches; a real adapter in the app, an in-memory fake in tests.</summary>
public interface IRunKey
{
    string? GetValue(string name);

    void SetValue(string name, string value);

    void DeleteValue(string name);
}

/// <summary><c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, opened for each operation so no handle is held.</summary>
public sealed class RegistryRunKey : IRunKey
{
    public const string Path = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? GetValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path, writable: false);
        return key?.GetValue(name) as string;
    }

    public void SetValue(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Path, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>What <see cref="StartupRegistration.Apply"/> did to the Run entry.</summary>
public enum StartupChange
{
    /// <summary>It was already as the settings want it.</summary>
    None,

    /// <summary>This program's entry was written (or repaired, when it named a program that is gone).</summary>
    Written,

    /// <summary>This program's entry was removed.</summary>
    Removed,

    /// <summary>There was no entry, and adding one was not allowed (a portable copy asks first).</summary>
    NotAdded,

    /// <summary>The entry names another copy of the program that is still there; it was left as it is.</summary>
    LeftToAnotherCopy,
}

/// <summary>
/// Keeps the per-user Run entry in step with the "start with Windows" settings: written when the setting is on, removed
/// when it is off, but only when it names this program. One value serves every copy of the app, so an entry that
/// names another copy that is still there (the installed bar, while a portable one is tried) is left alone; one that
/// names a program that is gone (moved, uninstalled) is taken over. The command is the executable in quotes, followed
/// by <see cref="HiddenArgument"/> when the bar should start in the tray.
/// </summary>
public sealed class StartupRegistration(IRunKey key, string executablePath, Func<string, bool>? fileExists = null)
{
    public const string ValueName = "DesktopIconGrid";
    public const string HiddenArgument = "--hidden";

    /// <summary>A program on a network share or a removable drive counts as there without a look: an offline share would hold up the start.</summary>
    private readonly Func<string, bool> _fileExists = fileExists ?? (path => IconWorker.IsOnSlowStorage(path) || File.Exists(path));

    public string ExecutablePath { get; } = executablePath;

    /// <summary>True when a Run entry under this app's name exists, whatever it says.</summary>
    public bool IsRegistered => key.GetValue(ValueName) is not null;

    /// <summary>The program the Run entry names, or <see langword="null"/> when there is none or it is not one this app wrote.</summary>
    public string? RegisteredExecutable => TryParse(key.GetValue(ValueName), out var executable, out _) ? executable : null;

    /// <summary>The Run value for <paramref name="executable"/>: quoted, so paths with spaces survive.</summary>
    public static string BuildCommand(string executable, bool hidden)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        return hidden ? $"\"{executable}\" {HiddenArgument}" : $"\"{executable}\"";
    }

    /// <summary>Reads a value written by <see cref="BuildCommand"/>; false for anything else.</summary>
    public static bool TryParse(string? command, out string executable, out bool hidden)
    {
        executable = string.Empty;
        hidden = false;
        if (string.IsNullOrWhiteSpace(command) || command[0] != '"')
        {
            return false;
        }

        var close = command.IndexOf('"', 1);
        if (close <= 1)
        {
            return false;
        }

        executable = command[1..close];
        var rest = command[(close + 1)..].Trim();
        if (rest.Length == 0)
        {
            return true;
        }

        if (string.Equals(rest, HiddenArgument, StringComparison.OrdinalIgnoreCase))
        {
            hidden = true;
            return true;
        }

        executable = string.Empty;
        return false;
    }

    /// <summary>
    /// Makes this program's Run entry match the settings, without touching another copy's (see the class summary).
    /// <paramref name="mayAdd"/> false leaves a missing entry missing: a portable copy adds one only when asked.
    /// </summary>
    public StartupChange Apply(bool startWithWindows, bool startHidden, bool mayAdd = true)
    {
        var current = key.GetValue(ValueName);
        var ours = current is not null && NamesThisProgram(current);
        if (!startWithWindows)
        {
            if (!ours)
            {
                return StartupChange.None; // nothing, or another copy's entry
            }

            key.DeleteValue(ValueName);
            return StartupChange.Removed;
        }

        var wanted = BuildCommand(ExecutablePath, startHidden);
        if (string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
        {
            return StartupChange.None;
        }

        if (current is not null && !ours && !NamesAProgramThatIsGone(current))
        {
            return StartupChange.LeftToAnotherCopy;
        }

        if (current is null && !mayAdd)
        {
            return StartupChange.NotAdded;
        }

        key.SetValue(ValueName, wanted);
        return StartupChange.Written;
    }

    private bool NamesThisProgram(string command) => TryParse(command, out var executable, out _) && SamePath(executable, ExecutablePath);

    /// <summary>An entry this app wrote for a program that is not there any more; anything else may be another copy's.</summary>
    private bool NamesAProgramThatIsGone(string command) => TryParse(command, out var executable, out _) && !_fileExists(executable);

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
