using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Sync;

/// <summary>One named copy of the settings and icons as stored on Google Drive.</summary>
public sealed class SyncProfile
{
    public const int CurrentFormat = 1;

    public int Format { get; set; } = CurrentFormat;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// In a sync folder, the name of the file the copy was written to (<see cref="FolderSyncClient"/> puts it in, 0.11.0
    /// on). A sync program's conflict copy, or a file someone copied, still says the original's file name, which tells
    /// it from a copy this app gave a numbered file (<c>config (2).json</c> beside another program's
    /// <c>config.json</c>). Older versions do not know it and keep comparing file names.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? File { get; set; }

    public DateTimeOffset SavedAt { get; set; }

    /// <summary>Device name of the machine that saved the copy.</summary>
    public string SavedBy { get; set; } = string.Empty;

    /// <summary>Installation id of the machine that saved the copy.</summary>
    public string? SavedById { get; set; }

    public string? AppVersion { get; set; }

    /// <summary>
    /// A random id of this save. Another PC's save is told by its revision, not its time: a sync program carries the
    /// writer's own file time from PC to PC, and a PC's clock can be wrong (see <see cref="SyncService.IsNewerThanLocal"/>).
    /// </summary>
    public string? Revision { get; set; }

    /// <summary>Settings and items. The window placement is never included; it is device-specific.</summary>
    public LauncherLayout Layout { get; set; } = new();

    /// <summary>Properties written by a newer version that this one does not know; kept so saving never drops them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public static SyncProfile FromLayout(string name, LauncherLayout layout, string savedBy, DateTimeOffset savedAt, string? appVersion, string? savedById = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return new SyncProfile
        {
            Name = name,
            SavedAt = savedAt,
            SavedBy = savedBy,
            SavedById = savedById,
            AppVersion = appVersion,
            Revision = NewRevision(),
            Layout = new LauncherLayout
            {
                Version = layout.Version,
                Settings = layout.Settings.Clone(),
                Items = layout.Items.Select(i => i.Clone()).ToList(),
                Window = null,
                ExtensionData = UnknownProperties.Copy(layout.ExtensionData),
            },
        };
    }

    public static string NewRevision() => Guid.NewGuid().ToString("N");

    public string ToJson() => JsonSerializer.Serialize(this, LayoutStorage.JsonOptions);

    /// <summary>Parses a profile file. Throws <see cref="FormatException"/> when the content is not a profile.</summary>
    public static SyncProfile FromJson(string json)
    {
        SyncProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<SyncProfile>(json, LayoutStorage.JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The file on Google Drive is not a Desktop Icon Grid settings copy.", ex);
        }

        if (profile is null)
        {
            throw new FormatException("The file on Google Drive is empty.");
        }

        profile.Layout ??= new LauncherLayout();
        profile.Layout.Settings ??= new LauncherSettings();
        profile.Layout.Items ??= [];
        profile.Layout.Window = null;
        return profile;
    }

    /// <summary>Metadata attached to the Drive file so listings can show who saved it and when without downloading.</summary>
    public IReadOnlyDictionary<string, string> ToAppProperties()
    {
        var properties = new Dictionary<string, string>
        {
            ["app"] = "DesktopIconGrid",
            ["profile"] = Name,
            ["device"] = SavedBy,
            ["savedAt"] = SavedAt.ToString("O", CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrEmpty(SavedById))
        {
            properties["deviceId"] = SavedById;
        }

        if (!string.IsNullOrEmpty(Revision))
        {
            properties["revision"] = Revision;
        }

        return properties;
    }
}

/// <summary>
/// A settings copy as it appears in the Drive listing (no content downloaded yet). <see cref="SavedAt"/> is
/// Drive's own modification time when available, so devices with wrong clocks cannot win conflicts.
/// </summary>
public sealed record RemoteProfile(string Name, string FileId, DateTimeOffset? SavedAt, string? SavedBy, long? Size)
{
    /// <summary>Installation id of the device that saved the copy, when it recorded one.</summary>
    public string? SavedById { get; init; }

    /// <summary>The save's <see cref="SyncProfile.Revision"/>, when it recorded one.</summary>
    public string? Revision { get; init; }

    /// <summary>The copy could not be read when it was listed (a sync program may be writing it), so who saved it is not known.</summary>
    public bool Unreadable { get; init; }

    /// <summary>
    /// The name saved inside a copy in a folder, when it says one. A sync program's conflict copy (<c>Work-LAPTOP.json</c>,
    /// which says "Work" inside) is listed under its file name, so the two differ (<see cref="IsConflictCopy"/>).
    /// </summary>
    public string? NameInside { get; init; }

    /// <summary>
    /// A copy a sync program kept of another one, or one renamed by hand: listed under its file name because the name
    /// inside it is another copy's (<see cref="FolderSyncClient.DisplayName"/>).
    /// </summary>
    public bool IsConflictCopy => NameInside is { Length: > 0 } inside && !string.Equals(inside, Name, StringComparison.OrdinalIgnoreCase);

    public const string FileExtension = ".json";

    public string FileName => Name + FileExtension;

    /// <summary>Save time in the local time zone, for display.</summary>
    public DateTime? SavedAtLocal => SavedAt?.LocalDateTime;

    /// <summary>Maps a Drive file to a profile; <see langword="null"/> for files that are not profiles.</summary>
    public static RemoteProfile? FromFile(DriveFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (string.IsNullOrEmpty(file.Id) || !file.Name.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = file.Name[..^FileExtension.Length];
        if (file.AppProperties.TryGetValue("profile", out var profileName) && !string.IsNullOrWhiteSpace(profileName))
        {
            name = profileName;
        }

        DateTimeOffset? savedAt = file.ModifiedTime;
        if (savedAt is null
            && file.AppProperties.TryGetValue("savedAt", out var savedAtText)
            && DateTimeOffset.TryParse(savedAtText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            savedAt = parsed;
        }

        file.AppProperties.TryGetValue("device", out var device);
        file.AppProperties.TryGetValue("deviceId", out var deviceId);
        file.AppProperties.TryGetValue("revision", out var revision);
        file.AppProperties.TryGetValue(FolderSyncClient.NameInsideProperty, out var nameInside);
        return new RemoteProfile(name, file.Id, savedAt, string.IsNullOrWhiteSpace(device) ? null : device, file.Size)
        {
            SavedById = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId,
            Revision = string.IsNullOrWhiteSpace(revision) ? null : revision,
            NameInside = string.IsNullOrWhiteSpace(nameInside) ? null : nameInside.Trim(),
            Unreadable = file.AppProperties.TryGetValue(FolderSyncClient.UnreadableProperty, out var unreadable) && unreadable == "true",
        };
    }
}
