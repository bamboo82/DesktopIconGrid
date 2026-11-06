using System.Runtime.InteropServices;

namespace DesktopIconGrid.Services;

/// <summary>Shell items and known folders, for listing <c>shell:AppsFolder</c> (every app the Start menu shows).</summary>
internal static partial class NativeMethods
{
    public static readonly Guid FOLDERID_AppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");
    public static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
    public static readonly Guid BHID_PropertyStore = new("0384e1a4-1523-439c-a4c8-ab911052f586");

    /// <summary><c>PKEY_Link_TargetParsingPath</c>: {B9B4B3FC-2B51-4A42-B5D8-324146AFCF25}, 2.</summary>
    public static readonly PROPERTYKEY PKEY_Link_TargetParsingPath = new() { fmtid = new Guid("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25"), pid = 2 };

    /// <summary><c>PKEY_Link_Arguments</c>: {436F2667-14E2-4FEB-B30A-146C53B5B674}, 100.</summary>
    public static readonly PROPERTYKEY PKEY_Link_Arguments = new() { fmtid = new Guid("436F2667-14E2-4FEB-B30A-146C53B5B674"), pid = 100 };

    public const uint SIGDN_NORMALDISPLAY = 0;
    public const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);

        void GetParent(out IShellItem ppsi);

        [PreserveSig]
        int GetDisplayName(uint sigdnName, out IntPtr ppszName);

        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [Guid("70629033-e363-4a28-a567-0db78006e6d7")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumShellItems
    {
        [PreserveSig]
        int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem rgelt, out uint pceltFetched);

        void Skip(uint celt);

        void Reset();

        void Clone(out IEnumShellItems ppenum);
    }

    [DllImport("shell32.dll")]
    public static extern int SHGetKnownFolderItem(ref Guid rfid, uint flags, IntPtr hToken, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [DllImport("shell32.dll")]
    public static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
