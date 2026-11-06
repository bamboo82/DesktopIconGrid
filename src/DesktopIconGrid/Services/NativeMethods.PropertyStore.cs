using System.Runtime.InteropServices;

namespace DesktopIconGrid.Services;

/// <summary>
/// A window's property store, for the AppUserModelID of a Store app's frame: the frame keeps it while the app is
/// suspended (minimised for a few seconds), when the app's own window inside the frame is gone.
/// </summary>
internal static partial class NativeMethods
{
    public const ushort VT_LPWSTR = 31;

    /// <summary><c>PKEY_AppUserModel_ID</c>: {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 5.</summary>
    public static readonly PROPERTYKEY PKEY_AppUserModel_ID = new() { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    /// <summary>
    /// The head of a PROPVARIANT and its union as two pointer-sized fields: 16 bytes in a 32-bit process, 24 in a 64-bit
    /// one, as Windows has it. Only <see cref="VT_LPWSTR"/> (a string at <see cref="Pointer"/>) is read; the value is
    /// freed with <see cref="PropVariantClear"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr Pointer;
        public IntPtr Rest;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int GetAt(uint index, out PROPERTYKEY key);

        [PreserveSig]
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);

        [PreserveSig]
        int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);

        [PreserveSig]
        int Commit();
    }

    /// <summary>A string property of <paramref name="store"/> (<see cref="VT_LPWSTR"/>), or <see langword="null"/> when it has none.</summary>
    public static string? StringOf(IPropertyStore store, PROPERTYKEY key)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (store.GetValue(ref key, out var value) != 0)
        {
            return null;
        }

        try
        {
            return value.vt == VT_LPWSTR && value.Pointer != IntPtr.Zero ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    [DllImport("shell32.dll")]
    public static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? propertyStore);

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PROPVARIANT value);

    /// <summary>The window's AppUserModelID, or <see langword="null"/> when it has none or cannot be read.</summary>
    public static string? AppUserModelIdOf(IntPtr hwnd)
    {
        var iid = typeof(IPropertyStore).GUID;
        IPropertyStore? store = null;
        try
        {
            return SHGetPropertyStoreForWindow(hwnd, ref iid, out store) != 0 || store is null
                ? null
                : StringOf(store, PKEY_AppUserModel_ID);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            if (store is not null)
            {
                Marshal.ReleaseComObject(store);
            }
        }
    }
}
