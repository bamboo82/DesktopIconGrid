using System.Runtime.InteropServices;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Imports;

namespace DesktopIconGrid.Services;

/// <summary>Every installed app the Start menu lists, read from <c>shell:AppsFolder</c>, Store apps included.</summary>
public static class InstalledApps
{
    /// <summary>The apps by name; empty when the shell cannot list them. Runs on an STA thread (the icon worker).</summary>
    public static IReadOnlyList<InstalledApp> Enumerate()
    {
        var apps = new List<InstalledApp>();
        NativeMethods.IShellItem? folder = null;
        NativeMethods.IEnumShellItems? items = null;
        try
        {
            var folderId = NativeMethods.FOLDERID_AppsFolder;
            var shellItemId = typeof(NativeMethods.IShellItem).GUID;
            if (NativeMethods.SHGetKnownFolderItem(ref folderId, 0, IntPtr.Zero, ref shellItemId, out folder) != 0 || folder is null)
            {
                return apps;
            }

            var handler = NativeMethods.BHID_EnumItems;
            var enumId = typeof(NativeMethods.IEnumShellItems).GUID;
            folder.BindToHandler(IntPtr.Zero, ref handler, ref enumId, out var pointer);
            items = (NativeMethods.IEnumShellItems)Marshal.GetObjectForIUnknown(pointer);
            Marshal.Release(pointer);

            while (items.Next(1, out var item, out var fetched) == 0 && fetched == 1)
            {
                try
                {
                    var app = AppsFolderEntry.ToApp(
                        DisplayName(item, NativeMethods.SIGDN_NORMALDISPLAY),
                        DisplayName(item, NativeMethods.SIGDN_PARENTRELATIVEPARSING),
                        KnownFolderPath);
                    if (app is not null)
                    {
                        apps.Add(app);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
        {
            AppLog.Default.Warning("Apps", $"The list of installed apps could not be read: {ex.Message}");
        }
        finally
        {
            if (items is not null)
            {
                Marshal.ReleaseComObject(items);
            }

            if (folder is not null)
            {
                Marshal.ReleaseComObject(folder);
            }
        }

        AppLog.Default.Debug("Apps", $"{apps.Count} installed apps listed");
        return apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string? DisplayName(NativeMethods.IShellItem item, uint kind)
    {
        if (item.GetDisplayName(kind, out var pointer) != 0 || pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static string? KnownFolderPath(Guid id)
    {
        var result = NativeMethods.SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var pointer);
        try
        {
            return result == 0 && pointer != IntPtr.Zero ? Marshal.PtrToStringUni(pointer) : null;
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
    }
}
