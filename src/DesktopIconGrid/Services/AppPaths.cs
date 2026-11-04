using System.IO;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Services;

/// <summary>Folders under <c>%LocalAppData%\DesktopIconGrid</c>.</summary>
public static class AppPaths
{
    public static string DataDirectory => Path.GetDirectoryName(LayoutStorage.GetDefaultFilePath())!;

    /// <summary>Icons downloaded from the web.</summary>
    public static string IconsDirectory => Path.Combine(DataDirectory, "icons");

    /// <summary>Recordings of Sound Replayer icons.</summary>
    public static string SoundsDirectory => Path.Combine(DataDirectory, "sounds");

    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public static string DriveCacheDirectory => Path.Combine(DataDirectory, "drive-cache");
}
