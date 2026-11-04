using System.Globalization;
using System.Text.Json;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Sync;

/// <summary>One named copy of the settings and icons as stored on Google Drive.</summary>
public sealed class SyncProfile
{
    public const int CurrentFormat = 1;

    public int Format { get; set; } = CurrentFormat;

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset SavedAt { get; set; }

    /// <summary>Device name of the machine that saved the copy.</summary>
    public string SavedBy { get; set; } = string.Empty;

    public string? AppVersion { get; set; }

    /// <summary>Settings and items. The window placement is never included; it is device-specific.</summary>
    public LauncherLayout Layout { get; set; } = new();

    public static SyncProfile FromLayout(string name, LauncherLayout layout, string savedBy, DateTimeOffset savedAt, string? appVersion)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return new SyncProfile
        {
            Name = name,
            SavedAt = savedAt,
            SavedBy = savedBy,
            AppVersion = appVersion,
            Layout = new LauncherLayout
            {
                Version = layout.Version,
                Settings = layout.Settings.Clone(),
                Items = layout.Items.Select(i => i.Clone()).ToList(),
                Window = null,
            },
        };
    }

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
    public IReadOnlyDictionary<string, string> ToAppProperties() => new Dictionary<string, string>
    {
        ["app"] = "DesktopIconGrid",
        ["profile"] = Name,
        ["device"] = SavedBy,
        ["savedAt"] = SavedAt.ToString("O", CultureInfo.InvariantCulture),
    };
}

/// <summary>A settings copy as it appears in the Drive listing (no content downloaded yet).</summary>
public sealed record RemoteProfile(string Name, string FileId, DateTimeOffset? SavedAt, string? SavedBy, long? Size)
{
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
        if (file.AppProperties.TryGetValue("savedAt", out var savedAtText)
            && DateTimeOffset.TryParse(savedAtText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            savedAt = parsed;
        }

        file.AppProperties.TryGetValue("device", out var device);
        return new RemoteProfile(name, file.Id, savedAt, string.IsNullOrWhiteSpace(device) ? null : device, file.Size);
    }
}
