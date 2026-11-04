using System.Text.Json.Serialization;

namespace DesktopIconGrid.Core.Models;

/// <summary>
/// Base type for everything that can sit in a cell of the bar: a <see cref="ShortcutItem"/>,
/// a <see cref="GroupItem"/> (a folder of shortcuts), a <see cref="SpecialIconItem"/>, or an
/// <see cref="UnknownItem"/> kept from a newer version. Serialized with a <c>$type</c> discriminator
/// by <see cref="Storage.LauncherItemConverter"/>.
/// </summary>
public abstract class LauncherItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Caption shown as a tooltip, and under the icon when labels are enabled.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional icon override: an image file, or an <c>.exe</c>/<c>.dll</c>/<c>.ico</c>
    /// resource written as <c>path</c> or <c>path,index</c>. <see langword="null"/> uses the default icon.
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>Optional text drawn instead of an image. <see cref="IconPath"/> wins when both are set.</summary>
    public TextIcon? TextIcon { get; set; }

    /// <summary>When the item was added or last edited through the app.</summary>
    public DateTimeOffset? ModifiedAt { get; set; }

    [JsonIgnore]
    public bool HasCustomIcon => !string.IsNullOrWhiteSpace(IconPath);

    [JsonIgnore]
    public bool HasTextIcon => TextIcon is not null;

    /// <summary>Deep copy, keeping the same <see cref="Id"/>.</summary>
    public abstract LauncherItem Clone();

    /// <summary>Copies the editable fields (everything except <see cref="Id"/>) from another item of the same kind.</summary>
    public virtual void CopyContentFrom(LauncherItem source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Name = source.Name;
        IconPath = string.IsNullOrWhiteSpace(source.IconPath) ? null : source.IconPath.Trim();
        TextIcon = source.TextIcon?.Normalized();
    }
}
