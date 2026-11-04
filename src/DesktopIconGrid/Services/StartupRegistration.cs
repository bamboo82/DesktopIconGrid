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

/// <summary>
/// Keeps the per-user Run entry in step with the "start with Windows" settings: written when the setting is on
/// (rewritten when the program was moved), removed when it is off. The command is the executable in quotes,
/// followed by <see cref="HiddenArgument"/> when the bar should start in the tray.
/// </summary>
public sealed class StartupRegistration(IRunKey key, string executablePath)
{
    public const string ValueName = "DesktopIconGrid";
    public const string HiddenArgument = "--hidden";

    public string ExecutablePath { get; } = executablePath;

    /// <summary>True when a Run entry under this app's name exists, whatever it says.</summary>
    public bool IsRegistered => key.GetValue(ValueName) is not null;

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

    /// <summary>Makes the Run entry match the settings. Returns true when the registry was changed.</summary>
    public bool Apply(bool startWithWindows, bool startHidden)
    {
        var current = key.GetValue(ValueName);
        if (!startWithWindows)
        {
            if (current is null)
            {
                return false;
            }

            key.DeleteValue(ValueName);
            return true;
        }

        var wanted = BuildCommand(ExecutablePath, startHidden);
        if (string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        key.SetValue(ValueName, wanted);
        return true;
    }
}
