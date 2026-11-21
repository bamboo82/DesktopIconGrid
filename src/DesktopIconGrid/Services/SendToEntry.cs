using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using DesktopIconGrid.Core.Commands;
using DesktopIconGrid.Core.Diagnostics;

namespace DesktopIconGrid.Services;

/// <summary>
/// The bar's entry in Explorer's <em>Send to</em> menu: a shortcut in this user's SendTo folder that starts this
/// program with <c>--add</c>, so the chosen files and folders reach the running bar (<see cref="CommandPipe"/>).
/// </summary>
public static class SendToEntry
{
    /// <summary>The shortcut file, or <see langword="null"/> when this user has no SendTo folder.</summary>
    public static string? FilePath
    {
        get
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "Desktop Icon Grid.lnk");
        }
    }

    public static bool Exists => FilePath is { } path && File.Exists(path);

    /// <summary>Adds the entry (pointing at this copy of the program) or removes it; throws when the folder cannot be written.</summary>
    public static void Set(bool on)
    {
        var path = FilePath ?? throw new IOException("This user has no Send To folder.");
        if (!on)
        {
            File.Delete(path);
            AppLog.Default.Info("Commands", "Removed the Send To entry");
            return;
        }

        var program = Environment.ProcessPath ?? throw new IOException("The program's path is not known.");
        var link = (NativeMethods.IShellLinkW)new NativeMethods.CShellLink();
        try
        {
            Check(link.SetPath(program));
            Check(link.SetArguments(BarCommands.AddArgument));
            var (icon, index) = IconFor(program);
            Check(link.SetIconLocation(icon, index));
            Check(link.SetWorkingDirectory(Path.GetDirectoryName(program) ?? string.Empty));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ((IPersistFile)link).Save(path, true);
            AppLog.Default.Info("Commands", "Added the Send To entry");
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>The file Explorer's Send to menu shows for the entry (0.14.0 WP7), next to the program in both zips.</summary>
    public const string IconFileName = "sendto.ico";

    /// <summary>The icon the entry shows: <see cref="IconFileName"/> next to <paramref name="program"/>, else the program's own.</summary>
    public static (string Path, int Index) IconFor(string program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var icon = Path.Combine(Path.GetDirectoryName(program) ?? string.Empty, IconFileName);
        return File.Exists(icon) ? (icon, 0) : (program, 0);
    }

    /// <summary>
    /// At start: the entry, when this program made it and it starts this copy, gets the icon it should have now (one made
    /// before 0.14.0 has the program's), and nothing else of it changes. An entry that starts another copy, or one that
    /// moved, or that is not this program's, is left as it is.
    /// </summary>
    public static void RefreshIcon()
    {
        if (FilePath is not { } path || !File.Exists(path) || Environment.ProcessPath is not { } program)
        {
            return;
        }

        var link = (NativeMethods.IShellLinkW)new NativeMethods.CShellLink();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var target = NativeMethods.ReadLinkText((buffer, size) => link.GetPath(buffer, size, IntPtr.Zero, NativeMethods.SLGP_RAWPATH));
            var arguments = NativeMethods.ReadLinkText(link.GetArguments);
            if (!IsThisCopy(target, arguments, program))
            {
                return;
            }

            var shown = 0;
            var iconPath = NativeMethods.ReadLinkText((buffer, size) => link.GetIconLocation(buffer, size, out shown));
            var (icon, index) = IconFor(program);
            if (string.Equals(iconPath, icon, StringComparison.OrdinalIgnoreCase) && shown == index)
            {
                return;
            }

            Check(link.SetIconLocation(icon, index));
            ((IPersistFile)link).Save(path, true);
            AppLog.Default.Info("Commands", $"The Send To entry shows {Path.GetFileName(icon)} now");
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Debug("Commands", $"The Send To entry's icon could not be brought up to date: {ex.Message}");
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>Whether a link starts <paramref name="program"/> with <c>--add</c>: the entry this program makes, for this copy.</summary>
    internal static bool IsThisCopy(string? target, string? arguments, string program) =>
        target is not null
        && string.Equals(Path.GetFullPath(target), Path.GetFullPath(program), StringComparison.OrdinalIgnoreCase)
        && string.Equals(arguments?.Trim(), BarCommands.AddArgument, StringComparison.Ordinal);

    private static void Check(int hresult) => Marshal.ThrowExceptionForHR(hresult);
}
