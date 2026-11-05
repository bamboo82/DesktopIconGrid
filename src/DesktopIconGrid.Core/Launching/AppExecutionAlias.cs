using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DesktopIconGrid.Core.Launching;

/// <summary>
/// App-execution aliases (<c>%LocalAppData%\Microsoft\WindowsApps\*.exe</c>): zero-byte reparse points that stand
/// for an executable inside a Store or winget package. The package folder is what the running badge watches, since
/// the alias target can be a launcher (<c>wt.exe</c> starts <c>WindowsTerminal.exe</c> from the same folder).
/// </summary>
public static class AppExecutionAlias
{
    /// <summary>IO_REPARSE_TAG_APPEXECLINK.</summary>
    public const uint ReparseTag = 0x8000001B;

    private const uint FsctlGetReparsePoint = 0x000900A8;
    private const int MaximumReparseDataBufferSize = 16 * 1024;
    private const int HeaderLength = 8; // ReparseTag, ReparseDataLength, Reserved
    private const int VersionLength = 4;
    private const uint FileShareAll = 0x1 | 0x2 | 0x4;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;

    /// <summary>The executable the alias stands for, or <see langword="null"/> when the path is not an alias or cannot be read.</summary>
    public static string? ResolveTarget(string aliasPath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(aliasPath))
        {
            return null;
        }

        using var handle = NativeMethods.CreateFile(aliasPath, 0, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagOpenReparsePoint | FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = new byte[MaximumReparseDataBufferSize];
        if (!NativeMethods.DeviceIoControl(handle, FsctlGetReparsePoint, IntPtr.Zero, 0, buffer, (uint)buffer.Length, out var returned, IntPtr.Zero))
        {
            return null;
        }

        return TryParseTarget(buffer.AsSpan(0, (int)Math.Min(returned, (uint)buffer.Length)), out var target) ? target : null;
    }

    /// <summary>
    /// Reads the executable out of an APPEXECLINK reparse buffer: the header, a version, then package id, entry
    /// point, executable and application type as NUL-terminated UTF-16 strings.
    /// </summary>
    public static bool TryParseTarget(ReadOnlySpan<byte> reparseBuffer, out string target)
    {
        target = string.Empty;
        if (reparseBuffer.Length < HeaderLength + VersionLength)
        {
            return false;
        }

        var tag = BinaryPrimitives.ReadUInt32LittleEndian(reparseBuffer);
        var dataLength = BinaryPrimitives.ReadUInt16LittleEndian(reparseBuffer[4..]);
        if (tag != ReparseTag || dataLength < VersionLength || HeaderLength + dataLength > reparseBuffer.Length)
        {
            return false;
        }

        var strings = Encoding.Unicode.GetString(reparseBuffer.Slice(HeaderLength + VersionLength, dataLength - VersionLength)).Split('\0');
        if (strings.Length < 3 || !LooksLikeWindowsPath(strings[2]))
        {
            return false;
        }

        target = strings[2];
        return true;
    }

    // Not Path.IsPathRooted: that follows the OS the code runs on, and these paths are always Windows paths.
    private static bool LooksLikeWindowsPath(string path) =>
        (path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\')
        || path.StartsWith(@"\\", StringComparison.Ordinal);

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, IntPtr inBuffer, uint inBufferSize, byte[] outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);
    }
}
