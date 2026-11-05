namespace DesktopIconGrid.Core.Storage;

/// <summary>Turns names from elsewhere (Google Drive entries, for instance) into names Windows accepts for files and folders.</summary>
public static class FileNames
{
    /// <summary>The name used when nothing usable is left.</summary>
    public const string Fallback = "file";

    // Fixed rather than Path.GetInvalidFileNameChars(): that list depends on the OS the code runs on, and these names are for Windows.
    private static readonly HashSet<char> InvalidChars = ['"', '<', '>', '|', ':', '*', '?', '\\', '/'];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>
    /// Replaces the characters Windows forbids with <c>_</c>, drops trailing dots and spaces (Windows would), appends
    /// <c>_</c> to a reserved device name such as <c>CON</c> or <c>COM1</c> (with or without an extension: <c>nul.txt</c>
    /// is reserved too), and returns <see cref="Fallback"/> when nothing is left.
    /// </summary>
    public static string Safe(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        var chars = trimmed.Select(c => c < ' ' || InvalidChars.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).TrimEnd('.', ' ');
        if (result.Length == 0)
        {
            return Fallback;
        }

        var dot = result.IndexOf('.');
        var stem = (dot < 0 ? result : result[..dot]).TrimEnd(' ');
        if (ReservedNames.Contains(stem))
        {
            result = dot < 0 ? stem + "_" : stem + "_" + result[dot..];
        }

        return result;
    }
}
