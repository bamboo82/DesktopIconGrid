using System.Globalization;
using System.Text.Json;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Storage;

/// <summary>What an import copied, what it kept aside, and what went wrong.</summary>
/// <param name="Copied">The files and folders copied, by name.</param>
/// <param name="KeptAside">The target's files that were in the way, as they are named now (<c>….before-import-&lt;date&gt;</c>).</param>
/// <param name="Problem">Why it stopped, when it did; the files copied before that stay.</param>
public sealed record DataImportResult(IReadOnlyList<string> Copied, IReadOnlyList<string> KeptAside, string? Problem);

/// <summary>
/// Copies a bar's data from one data folder to another (0.13.0 WP1): this PC's bar into a portable copy, or the data to
/// the folder chosen in Settings. What makes the bar: <c>layout.json</c>, <c>workspaces.json</c>, <c>usage.json</c>,
/// and the folders of the files it uses, <c>icons\</c> (pictures from the web) and <c>sounds\</c> (recordings), with
/// the bar's paths to them made the target's (review 33 R4: a portable copy is made to travel). Not what belongs to one
/// copy or one PC: <c>command-trust.json</c> (approvals are given where a command runs), the Google sign-in,
/// <c>sync.json</c> (its link to an account or a folder, and the device id: two copies must not be one device),
/// <c>this-pc.json</c>, the logs, the layout's history and the Drive cache. A file already in the target is renamed
/// <c>&lt;name&gt;.before-import-&lt;date&gt;</c> first: nothing is written over.
/// </summary>
public static class DataImport
{
    /// <summary>The files copied, in this order: the bar last, so a stop half way leaves the target's bar as it was.</summary>
    public static IReadOnlyList<string> Files { get; } = [WorkspacePositions.FileName, UsageLog.FileName, LayoutStorage.FileName];

    /// <summary>The folders of files the bar uses, copied whole before the bar; the background pictures since 0.15.0 (WP5).</summary>
    public static IReadOnlyList<string> Folders { get; } = ["icons", "sounds", Models.BackgroundImage.FolderName];

    /// <summary>The name a file in the way is given: <c>layout.json.before-import-20261121-093000</c>.</summary>
    public static string KeptAsideName(string path, DateTimeOffset now) =>
        $"{path}.before-import-{now.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";

    /// <summary>Whether <paramref name="source"/> has a bar to bring.</summary>
    public static bool HasBar(string source) => File.Exists(Path.Combine(source, LayoutStorage.FileName));

    /// <summary>
    /// The file Settings leaves in a portable copy's data folder when its user asked to bring this PC's bar in: the bar
    /// restarts, and the start does it before it reads the bar, so the bar that asked writes nothing over it.
    /// </summary>
    public const string RequestFile = "import-this-pc.request";

    /// <summary>
    /// Whether a portable copy asks at start to use this PC's bar: its data is where <c>portable.txt</c> says, that folder
    /// is not <c>%LocalAppData%</c>'s and has no bar of its own yet, and <c>%LocalAppData%</c> has one.
    /// </summary>
    public static bool OffersThisPcBar(bool portable, string dataDirectory, string thisPcDirectory) =>
        portable
        && !DataLocation.SameFolder(dataDirectory, thisPcDirectory)
        && !HasBar(dataDirectory)
        && HasBar(thisPcDirectory);

    /// <param name="source">The data folder copied from.</param>
    /// <param name="target">The data folder copied to; made when missing.</param>
    /// <param name="appDrive">
    /// For a portable target, the drive its program runs from as <see cref="DataLocation.StorageDrive"/> gives it: the
    /// bar's paths on that drive are written with <c>%APPDRIVE%</c>, as a portable copy writes them when they are entered.
    /// </param>
    /// <param name="deviceId">This PC's id: of a shortcut's own locations, only this PC's is written so (review 33 R6).</param>
    public static DataImportResult Run(string source, string target, string? appDrive, DateTimeOffset now, string? deviceId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        var copied = new List<string>();
        var keptAside = new List<string>();
        if (DataLocation.SameFolder(source, target))
        {
            return new DataImportResult(copied, keptAside, null); // nothing to bring
        }

        try
        {
            Directory.CreateDirectory(target);
            foreach (var folder in Folders)
            {
                if (CopyFolder(Path.Combine(source, folder), Path.Combine(target, folder), folder, now, keptAside))
                {
                    copied.Add(folder);
                }
            }

            foreach (var name in Files)
            {
                var from = Path.Combine(source, name);
                if (!File.Exists(from))
                {
                    continue;
                }

                var to = Path.Combine(target, name);
                KeepAside(to, name, now, keptAside);
                if (name == LayoutStorage.FileName)
                {
                    CopyLayout(from, to, source, target, appDrive, deviceId);
                }
                else
                {
                    File.Copy(from, to);
                }

                copied.Add(name);
            }

            return new DataImportResult(copied, keptAside, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new DataImportResult(copied, keptAside, ex.Message);
        }
    }

    /// <summary>Every file of <paramref name="from"/> into <paramref name="to"/>, those in the way kept aside; false when there is no such folder.</summary>
    private static bool CopyFolder(string from, string to, string name, DateTimeOffset now, List<string> keptAside)
    {
        if (!Directory.Exists(from))
        {
            return false;
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            var destination = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            KeepAside(destination, Path.Combine(name, relative), now, keptAside);
            File.Copy(file, destination);
        }

        return true;
    }

    /// <summary>Renames a file in the way to <see cref="KeptAsideName"/>, and notes it as <paramref name="shown"/> is named, kept aside.</summary>
    private static void KeepAside(string path, string shown, DateTimeOffset now, List<string> keptAside)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var aside = FreeName(KeptAsideName(path, now));
        File.Move(path, aside);
        keptAside.Add(Path.Combine(Path.GetDirectoryName(shown) ?? string.Empty, Path.GetFileName(aside)));
    }

    /// <summary>
    /// The bar, with its paths to the source's pictures made the target's and those on the program's drive written with
    /// <c>%APPDRIVE%</c>, written by the layout's own save. With nothing to change it is copied as it is; so is one that
    /// cannot be read (it is not moved aside in the source) and one written by a newer version (what this version does not
    /// know is not rewritten). The source is only read.
    /// </summary>
    private static void CopyLayout(string from, string to, string source, string target, string? appDrive, string? deviceId)
    {
        LauncherLayout? layout;
        try
        {
            layout = JsonSerializer.Deserialize<LauncherLayout>(File.ReadAllBytes(from), LayoutStorage.JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            layout = null;
        }

        if (layout is null || LauncherLayout.FormatOf(layout.Version) > LauncherLayout.CurrentVersion)
        {
            File.Copy(from, to);
            return;
        }

        layout.Settings ??= new LauncherSettings();
        layout.Items ??= [];
        var moved = WithFilesOf(layout, source, target);
        if (!moved && appDrive is null)
        {
            File.Copy(from, to);
            return;
        }

        new LayoutStorage(to).Save(WithAppDrive(layout, appDrive, deviceId));
    }

    /// <summary>
    /// Points every icon of <paramref name="layout"/> that is a picture in the source's <c>icons\</c> to the same picture
    /// in the target's (review 33 R4); true when one was. A path is compared as it is used: variables expanded,
    /// <c>%APPDRIVE%</c> too, either separator. Recordings need nothing: their files are named after their icons.
    /// </summary>
    public static bool WithFilesOf(LauncherLayout layout, string source, string target)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var moved = false;
        foreach (var item in Walk(layout.Items))
        {
            if (item.IconPath is { } icon && Inside(icon, Path.Combine(source, "icons")) is { } rest)
            {
                item.IconPath = Path.Combine(target, "icons", rest);
                moved = true;
            }
        }

        return moved;
    }

    /// <summary>The part of <paramref name="path"/> under <paramref name="folder"/>, or <see langword="null"/> when it is not under it.</summary>
    private static string? Inside(string path, string folder)
    {
        var used = Environment.ExpandEnvironmentVariables(path.Trim()).Replace('/', '\\');
        var prefix = folder.Replace('/', '\\').TrimEnd('\\') + "\\";
        return used.Length > prefix.Length && used.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? used[prefix.Length..].Replace('\\', Path.DirectorySeparatorChar)
            : null;
    }

    /// <summary>
    /// <paramref name="layout"/> with every path on <paramref name="appDrive"/> written as <c>%APPDRIVE%</c>: each
    /// shortcut's shared location, start folder and icon, each group's and special icon's icon, and of the locations kept
    /// per PC only this PC's (<paramref name="deviceId"/>): another PC's own path on the same letter is that PC's
    /// (review 33 R6). The layout itself, unchanged, when there is no such drive.
    /// </summary>
    public static LauncherLayout WithAppDrive(LauncherLayout layout, string? appDrive, string? deviceId = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (appDrive is null)
        {
            return layout;
        }

        foreach (var item in Walk(layout.Items))
        {
            if (item.IconPath is { } icon)
            {
                item.IconPath = DataLocation.WithAppDrive(icon, appDrive);
            }

            if (item is not ShortcutItem shortcut)
            {
                continue;
            }

            shortcut.Location = DataLocation.WithAppDrive(shortcut.Location, appDrive);
            if (shortcut.StartIn is { } startIn)
            {
                shortcut.StartIn = DataLocation.WithAppDrive(startIn, appDrive);
            }

            if (deviceId is not null && shortcut.Locations is { } byDevice && byDevice.TryGetValue(deviceId, out var own))
            {
                byDevice[deviceId] = DataLocation.WithAppDrive(own, appDrive);
            }
        }

        return layout;
    }

    private static IEnumerable<LauncherItem> Walk(IEnumerable<LauncherItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            if (item is GroupItem group)
            {
                foreach (var inner in Walk(group.Items))
                {
                    yield return inner;
                }
            }
        }
    }

    /// <summary><paramref name="path"/>, or <c>path-2</c>, <c>path-3</c>… when a file of that name is there already.</summary>
    private static string FreeName(string path)
    {
        var candidate = path;
        for (var number = 2; File.Exists(candidate) || Directory.Exists(candidate); number++)
        {
            candidate = $"{path}-{number}";
        }

        return candidate;
    }
}
