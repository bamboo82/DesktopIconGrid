using System.IO;
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
                    var parsing = DisplayName(item, NativeMethods.SIGDN_PARENTRELATIVEPARSING);
                    var (target, arguments) = AppsFolderEntry.NeedsLinkTarget(parsing) ? LinkOf(item) : (null, null);
                    var app = AppsFolderEntry.ToApp(DisplayName(item, NativeMethods.SIGDN_NORMALDISPLAY), parsing, KnownFolderPath, target, arguments);
                    if (app is not null)
                    {
                        apps.Add(app);
                    }
                }
                catch (Exception ex) when (IsShellFailure(ex))
                {
                    AppLog.Default.Debug("Apps", $"An installed app was skipped: {ex.Message}"); // one bad entry does not cost the others
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
        }
        catch (Exception ex) when (IsShellFailure(ex))
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

    /// <summary>
    /// What the shell throws for an entry or a list it cannot read; <c>E_OUTOFMEMORY</c> from a shell extension comes
    /// back as <see cref="OutOfMemoryException"/>.
    /// </summary>
    private static bool IsShellFailure(Exception ex) =>
        ex is COMException or InvalidCastException or ArgumentException or UnauthorizedAccessException or IOException
            or NotImplementedException or OutOfMemoryException;

    /// <summary>
    /// What the Start menu shortcut behind a registered id starts, from the entry's property store: its target and
    /// arguments, when the target is a file on a local drive that exists (a network target is not touched here).
    /// </summary>
    private static (string? Target, string? Arguments) LinkOf(NativeMethods.IShellItem item)
    {
        NativeMethods.IPropertyStore? store = null;
        try
        {
            var handler = NativeMethods.BHID_PropertyStore;
            var storeId = typeof(NativeMethods.IPropertyStore).GUID;
            item.BindToHandler(IntPtr.Zero, ref handler, ref storeId, out var pointer);
            store = (NativeMethods.IPropertyStore)Marshal.GetObjectForIUnknown(pointer);
            Marshal.Release(pointer);
            var target = NativeMethods.StringOf(store, NativeMethods.PKEY_Link_TargetParsingPath);
            return target is null || IconWorker.IsOnSlowStorage(target) || !File.Exists(target)
                ? (null, null)
                : (target, NativeMethods.StringOf(store, NativeMethods.PKEY_Link_Arguments));
        }
        catch (Exception ex) when (IsShellFailure(ex))
        {
            return (null, null); // left to the shell, as before
        }
        finally
        {
            if (store is not null)
            {
                Marshal.ReleaseComObject(store);
            }
        }
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
