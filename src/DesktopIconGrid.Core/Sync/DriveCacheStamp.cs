using System.Text.Json;

namespace DesktopIconGrid.Core.Sync;

/// <summary>
/// What a cached copy of a Drive file was downloaded from, kept next to the copy. Decides whether the copy can be
/// reused: the same MD5 checksum when Drive reports one for both, otherwise the same size and modification time.
/// </summary>
public sealed record DriveCacheStamp(long? Size, DateTimeOffset? ModifiedTime, string? Md5Checksum)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static DriveCacheStamp Of(DriveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new DriveCacheStamp(entry.Size, entry.ModifiedTime, entry.Md5Checksum);
    }

    /// <summary>
    /// True when the copy this stamp describes is still <paramref name="entry"/> as it is on Drive. Folders and
    /// Google formats (exports, no checksum) are never fresh; neither is a file without a size or time to compare.
    /// </summary>
    public bool IsFreshFor(DriveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsFolder || entry.IsGoogleFormat)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(Md5Checksum) && !string.IsNullOrEmpty(entry.Md5Checksum))
        {
            return string.Equals(Md5Checksum, entry.Md5Checksum, StringComparison.OrdinalIgnoreCase)
                   && (entry.Size is null || Size == entry.Size);
        }

        return Size is { } size && entry.Size == size
               && ModifiedTime is { } time && entry.ModifiedTime == time;
    }

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    /// <summary>The stamp in <paramref name="json"/>, or <see langword="null"/> when it is missing or unreadable.</summary>
    public static DriveCacheStamp? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DriveCacheStamp>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
