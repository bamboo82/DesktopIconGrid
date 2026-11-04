using System.Text.RegularExpressions;

namespace DesktopIconGrid.Core.Sync;

/// <summary>A file or folder as listed by the Drive API when browsing.</summary>
public sealed record DriveEntry(string Id, string Name, string MimeType, long? Size, DateTimeOffset? ModifiedTime)
{
    public const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string GooglePrefix = "application/vnd.google-apps.";

    public bool IsFolder => string.Equals(MimeType, FolderMimeType, StringComparison.Ordinal);

    /// <summary>Google Docs, Sheets, Slides and the like: no binary content, only exports.</summary>
    public bool IsGoogleFormat => MimeType.StartsWith(GooglePrefix, StringComparison.Ordinal) && !IsFolder;

    /// <summary>The export format for a Google document, or <see langword="null"/> when it can only be opened in the browser.</summary>
    public (string MimeType, string Extension)? Export => MimeType switch
    {
        "application/vnd.google-apps.document" => ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx"),
        "application/vnd.google-apps.spreadsheet" => ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx"),
        "application/vnd.google-apps.presentation" => ("application/vnd.openxmlformats-officedocument.presentationml.presentation", ".pptx"),
        "application/vnd.google-apps.drawing" => ("application/pdf", ".pdf"),
        _ => null,
    };

    public bool CanDownload => !IsFolder && (!IsGoogleFormat || Export is not null);

    /// <summary>Local file name for a download: Google formats get their export extension.</summary>
    public string DownloadFileName => IsGoogleFormat && Export is { } export && !Name.EndsWith(export.Extension, StringComparison.OrdinalIgnoreCase)
        ? Name + export.Extension
        : Name;

    public string WebUrl => IsFolder
        ? $"https://drive.google.com/drive/folders/{Id}"
        : $"https://drive.google.com/open?id={Id}";

    /// <summary>"Folder", "Google Doc", "PDF file", "File".</summary>
    public string KindText
    {
        get
        {
            if (IsFolder)
            {
                return "Folder";
            }

            if (IsGoogleFormat)
            {
                return MimeType[GooglePrefix.Length..] switch
                {
                    "document" => "Google Doc",
                    "spreadsheet" => "Google Sheet",
                    "presentation" => "Google Slides",
                    "drawing" => "Google Drawing",
                    "form" => "Google Form",
                    "shortcut" => "Shortcut",
                    var other => "Google " + other,
                };
            }

            var extension = Path.GetExtension(Name);
            return extension.Length > 1 ? $"{extension[1..].ToUpperInvariant()} file" : "File";
        }
    }
}

/// <summary>Turns pasted Google Drive links or ids into a folder/file id.</summary>
public static partial class DriveFolderLink
{
    public static bool TryParseId(string? input, out string id)
    {
        id = string.Empty;
        var trimmed = input?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && uri.Host.EndsWith("google.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i] is "folders" or "d")
                {
                    return Accept(segments[i + 1], out id);
                }
            }

            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in query)
            {
                if (pair.StartsWith("id=", StringComparison.OrdinalIgnoreCase))
                {
                    return Accept(Uri.UnescapeDataString(pair[3..]), out id);
                }
            }

            return false;
        }

        return Accept(trimmed, out id);
    }

    private static bool Accept(string candidate, out string id)
    {
        id = candidate.Trim();
        return IdPattern().IsMatch(id);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{10,}$")]
    private static partial Regex IdPattern();
}

/// <summary>Content types for uploads, by file extension.</summary>
public static class MimeTypes
{
    public const string Binary = "application/octet-stream";

    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain", [".md"] = "text/markdown", [".csv"] = "text/csv", [".json"] = "application/json",
        [".xml"] = "application/xml", [".html"] = "text/html", [".htm"] = "text/html", [".pdf"] = "application/pdf",
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif",
        [".bmp"] = "image/bmp", [".webp"] = "image/webp", [".svg"] = "image/svg+xml", [".ico"] = "image/x-icon",
        [".mp3"] = "audio/mpeg", [".wav"] = "audio/wav", [".mp4"] = "video/mp4", [".mkv"] = "video/x-matroska",
        [".zip"] = "application/zip", [".7z"] = "application/x-7z-compressed", [".rar"] = "application/vnd.rar",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    };

    public static string FromFileName(string fileName) =>
        ByExtension.TryGetValue(Path.GetExtension(fileName ?? string.Empty), out var type) ? type : Binary;
}
