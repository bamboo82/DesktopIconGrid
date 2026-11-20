using System.IO;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Services;

/// <summary>
/// Where this copy of the program is, where its data is now, and what <c>portable.txt</c> says now (0.13.0 WP1): what
/// Settings ▸ General shows and what a move of the data starts from. Tests give their own.
/// </summary>
/// <param name="ProgramDirectory">The program's folder.</param>
/// <param name="ThisPcDirectory"><c>%LocalAppData%\DesktopIconGrid</c>.</param>
/// <param name="Directory">The data folder the running bar uses.</param>
/// <param name="HasMarker"><c>portable.txt</c> is next to the program now (it may have been written since the start).</param>
/// <param name="MarkerLine">Its first line as written; empty for an empty file or none.</param>
/// <param name="IsPortable">The running bar's data is where <c>portable.txt</c> said at the start.</param>
public sealed record DataFolders(string ProgramDirectory, string ThisPcDirectory, string Directory, bool HasMarker, string MarkerLine, bool IsPortable)
{
    /// <summary>As this copy is now; <c>portable.txt</c> read again.</summary>
    public static DataFolders Current
    {
        get
        {
            var marker = DataLocation.MarkerFile;
            var exists = File.Exists(marker);
            string line = string.Empty;
            if (exists)
            {
                try
                {
                    using var reader = new StreamReader(marker, detectEncodingFromByteOrderMarks: true);
                    var buffer = new char[DataLocation.MaxMarkerBytes];
                    line = DataLocation.FirstLine(new string(buffer, 0, reader.ReadBlock(buffer, 0, buffer.Length))) ?? string.Empty;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    line = string.Empty;
                }
            }

            return new DataFolders(DataLocation.ProgramDirectory, DataLocation.ThisPcDirectory, DataLocation.Directory, exists, line, DataLocation.IsPortable);
        }
    }

    /// <summary><c>portable.txt</c> beside the program.</summary>
    public string MarkerFile => Path.Combine(ProgramDirectory, DataLocation.PortableMarker);

    /// <summary>The drive a portable copy writes its paths on as <c>%APPDRIVE%</c> (<see cref="DataLocation.StorageDrive"/>).</summary>
    public string? AppDrive => DataLocation.StorageDrive(ProgramDirectory, Environment.GetEnvironmentVariable("SystemDrive"));
}
