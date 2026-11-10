using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.SpecialIcons;

/// <summary>Kind ids of the special icons the app knows.</summary>
public static class SpecialIconKinds
{
    public const string CpuUsage = "cpu-usage";
    public const string DiskUsage = "disk-usage";
    /// <summary>Physical memory in use (0.10.0).</summary>
    public const string MemoryUsage = "memory-usage";

    /// <summary>Network throughput, down and up (0.10.0).</summary>
    public const string Network = "network";

    /// <summary>The default microphone's mute switch (0.10.0).</summary>
    public const string MicrophoneMute = "microphone-mute";

    /// <summary>Battery charge (0.10.0).</summary>
    public const string Battery = "battery";

    /// <summary>The time, here or in another zone, with a countdown or Pomodoro on a click (0.10.0).</summary>
    public const string Clock = "clock";

    public const string GoogleDriveBucket = "google-drive-bucket";
    public const string SoundReplayer = "sound-replayer";

    /// <summary>Runs a command on a timer or a click and shows its output (0.9.0).</summary>
    public const string CommandTile = "command-tile";
}

/// <summary>How a gauge icon draws its percentage.</summary>
public enum GaugeStyle
{
    /// <summary>A ring that fills clockwise with the number in the middle.</summary>
    Ring,

    /// <summary>A horizontal bar under the caption.</summary>
    Bar,

    /// <summary>A large number with the caption beneath.</summary>
    Number,
}

/// <summary>Settings shared by the CPU and disk gauge icons.</summary>
/// <param name="Style">The design.</param>
/// <param name="Drive">Drive root such as <c>C:\</c>; only used by the disk gauge.</param>
public sealed record MonitorIconSettings(GaugeStyle Style, string? Drive)
{
    public const string StyleKey = "style";
    public const string DriveKey = "drive";

    public static MonitorIconSettings From(SpecialIconItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var style = Enum.TryParse<GaugeStyle>(item.GetSetting(StyleKey), ignoreCase: true, out var parsed) ? parsed : GaugeStyle.Ring;
        return new MonitorIconSettings(style, NormalizeDrive(item.GetSetting(DriveKey)));
    }

    public void ApplyTo(SpecialIconItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.SetSetting(StyleKey, Style.ToString().ToLowerInvariant());
        item.SetSetting(DriveKey, Drive);
    }

    /// <summary>"c", "C:", "c:\" and "C:\Users" all become <c>C:\</c>; anything else is <see langword="null"/>.</summary>
    public static string? NormalizeDrive(string? drive)
    {
        var trimmed = drive?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !char.IsAsciiLetter(trimmed[0]))
        {
            return null;
        }

        if (trimmed.Length > 1 && trimmed[1] != ':')
        {
            return null;
        }

        return $"{char.ToUpperInvariant(trimmed[0])}:\\";
    }
}
