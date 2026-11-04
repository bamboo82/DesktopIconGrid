using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Resources;

namespace DesktopIconGrid.Core.Storage;

/// <summary>
/// Loads and saves the layout file as JSON. Saves are durable (written through to disk, then swapped
/// in while the previous file becomes <c>layout.json.bak</c>); loading falls back to the backup.
/// </summary>
public sealed class LayoutStorage
{
    public const string ApplicationFolderName = "DesktopIconGrid";
    public const string FileName = "layout.json";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new LauncherItemConverter() },
    };

    private int? _loadedNewerVersion;
    private bool _newerVersionPreserved;

    public LayoutStorage(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public string BackupPath => FilePath + ".bak";

    public string Directory => Path.GetDirectoryName(FilePath)!;

    /// <summary><c>%LocalAppData%\DesktopIconGrid\layout.json</c>.</summary>
    public static string GetDefaultFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        ApplicationFolderName,
        FileName);

    public LoadResult Load()
    {
        _loadedNewerVersion = null;
        _newerVersionPreserved = false;

        if (!File.Exists(FilePath))
        {
            if (TryRead(BackupPath, out var fromBackup, out _))
            {
                return Finish(fromBackup, isFirstRun: false, corruptBackup: null, error: null, recoveredFromBackup: true);
            }

            return new LoadResult(new LauncherLayout(), IsFirstRun: true, CorruptFileBackupPath: null, Error: null);
        }

        if (TryRead(FilePath, out var layout, out var error))
        {
            return Finish(layout, isFirstRun: false, corruptBackup: null, error: null, recoveredFromBackup: false);
        }

        // The main file is unreadable: keep it for inspection and fall back to the last good save.
        var quarantined = QuarantineCorruptFile();
        if (TryRead(BackupPath, out var backup, out _))
        {
            return Finish(backup, isFirstRun: false, corruptBackup: quarantined, error: error, recoveredFromBackup: true);
        }

        return new LoadResult(new LauncherLayout(), IsFirstRun: false, CorruptFileBackupPath: quarantined, Error: error ?? CoreStrings.Layout_Empty);
    }

    public void Save(LauncherLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        System.IO.Directory.CreateDirectory(Directory);
        PreserveNewerVersionOnce();

        var json = JsonSerializer.Serialize(layout, JsonOptions);
        var tempPath = Path.Combine(Directory, Path.GetRandomFileName() + ".tmp");
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(FilePath))
            {
                File.Replace(tempPath, FilePath, BackupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, FilePath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private LoadResult Finish(LauncherLayout layout, bool isFirstRun, string? corruptBackup, string? error, bool recoveredFromBackup)
    {
        layout.Settings ??= new LauncherSettings();
        layout.Items ??= [];
        int? newerVersion = layout.Version > LauncherLayout.CurrentVersion ? layout.Version : null;
        _loadedNewerVersion = newerVersion;
        return new LoadResult(layout, isFirstRun, corruptBackup, error)
        {
            RecoveredFromBackup = recoveredFromBackup,
            NewerVersion = newerVersion,
        };
    }

    private static bool TryRead(string path, out LauncherLayout layout, out string? error)
    {
        layout = new LauncherLayout();
        error = null;
        if (!File.Exists(path))
        {
            error = CoreStrings.Layout_Missing;
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<LauncherLayout>(json, JsonOptions);
            if (parsed is null)
            {
                error = CoreStrings.Layout_Empty;
                return false;
            }

            layout = parsed;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Before the first save over a layout written by a newer version, keep that original next to the file.</summary>
    private void PreserveNewerVersionOnce()
    {
        if (_loadedNewerVersion is not { } version || _newerVersionPreserved)
        {
            return;
        }

        _newerVersionPreserved = true;
        try
        {
            if (File.Exists(FilePath))
            {
                File.Copy(FilePath, $"{FilePath}.v{version}.json", overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; the unknown items are preserved inside the normal save anyway.
        }
    }

    private string? QuarantineCorruptFile()
    {
        var quarantinePath = $"{FilePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
        try
        {
            File.Move(FilePath, quarantinePath, overwrite: true);
            return quarantinePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Outcome of <see cref="LayoutStorage.Load"/>. Always carries a usable layout.</summary>
public sealed record LoadResult(LauncherLayout Layout, bool IsFirstRun, string? CorruptFileBackupPath, string? Error)
{
    /// <summary>True when the main file was missing or unreadable and <c>layout.json.bak</c> was used instead.</summary>
    public bool RecoveredFromBackup { get; init; }

    /// <summary>Set when the file was written by a newer version of the app; its unknown items are kept as placeholders.</summary>
    public int? NewerVersion { get; init; }
}
