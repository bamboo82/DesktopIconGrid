using System.Runtime.InteropServices;

namespace DesktopIconGrid.Services;

/// <summary>The shell's <c>.lnk</c> object, for reading a shortcut file's target, arguments and working folder.</summary>
internal static partial class NativeMethods
{
    /// <summary>GetPath: the target as stored, with environment variables left in.</summary>
    public const uint SLGP_RAWPATH = 0x4;

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    public class CShellLink
    {
    }

    /// <summary>The first methods of <c>IShellLinkW</c>, in vtable order, up to the last one used.</summary>
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellLinkW
    {
        [PreserveSig]
        int GetPath(IntPtr pszFile, int cch, IntPtr pfd, uint fFlags);

        [PreserveSig]
        int GetIDList(out IntPtr ppidl);

        [PreserveSig]
        int SetIDList(IntPtr pidl);

        [PreserveSig]
        int GetDescription(IntPtr pszName, int cch);

        [PreserveSig]
        int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        [PreserveSig]
        int GetWorkingDirectory(IntPtr pszDir, int cch);

        [PreserveSig]
        int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        [PreserveSig]
        int GetArguments(IntPtr pszArgs, int cch);
    }
}
