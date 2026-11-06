using System.IO;
using System.Security;
using System.Security.Principal;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;
using Microsoft.Win32;

namespace DesktopIconGrid.Services;

/// <summary>This PC and Windows user as <see cref="MachineKey"/> sees them: Windows' machine id and the user's SID.</summary>
public static class MachineIdentity
{
    /// <summary>The <see cref="MachineKey"/> of this PC and user, or <see langword="null"/> when Windows does not say.</summary>
    public static string? Key()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var cryptography = hive.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            using var user = WindowsIdentity.GetCurrent();
            return MachineKey.From(cryptography?.GetValue("MachineGuid") as string, user.User?.Value);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            AppLog.Default.Warning("App", $"This PC's machine id could not be read; per-PC paths follow the sync device id: {ex.Message}");
            return null;
        }
    }
}
