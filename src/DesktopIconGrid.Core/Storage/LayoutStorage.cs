using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Storage;

/// <summary>Loads and saves the layout file as JSON. Saves are atomic (temp file + rename).</summary>
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
    };

    public LayoutStorage(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public string Directory => Path.GetDirectoryName(FilePath)!;

    /// <summary><c>%LocalAppData%\DesktopIconGrid\layout.json</c>.</summary>
    public static string GetDefaultFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        ApplicationFolderName,
        FileName);

    public LoadResult Load()
    {
        if (!File.Exists(FilePath))
        {
            return new LoadResult(new LauncherLayout(), IsFirstRun: true, CorruptFileBackupPath: null, Error: null);
        }

        string json;
        try
        {
            json = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new LoadResult(new LauncherLayout(), IsFirstRun: false, CorruptFileBackupPath: null, Error: ex.Message);
        }

        LauncherLayout? layout = null;
        string? error = null;
        try
        {
            layout = JsonSerializer.Deserialize<LauncherLayout>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            error = ex.Message;
        }
        catch (NotSupportedException ex)
        {
            error = ex.Message;
        }

        if (layout is null)
        {
            var backup = BackupCorruptFile();
            return new LoadResult(new LauncherLayout(), IsFirstRun: false, CorruptFileBackupPath: backup, Error: error ?? "The layout file is empty.");
        }

        layout.Settings ??= new LauncherSettings();
        layout.Items ??= [];
        return new LoadResult(layout, IsFirstRun: false, CorruptFileBackupPath: null, Error: null);
    }

    public void Save(LauncherLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        System.IO.Directory.CreateDirectory(Directory);

        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(layout, JsonOptions));
        File.Move(tempPath, FilePath, overwrite: true);
    }

    private string? BackupCorruptFile()
    {
        var backupPath = $"{FilePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
        try
        {
            File.Move(FilePath, backupPath, overwrite: true);
            return backupPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Outcome of <see cref="LayoutStorage.Load"/>. Always carries a usable layout.</summary>
public sealed record LoadResult(LauncherLayout Layout, bool IsFirstRun, string? CorruptFileBackupPath, string? Error);
