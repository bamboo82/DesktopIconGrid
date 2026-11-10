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
            Check(link.SetIconLocation(program, 0));
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

    private static void Check(int hresult) => Marshal.ThrowExceptionForHR(hresult);
}
