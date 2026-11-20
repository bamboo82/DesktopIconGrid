using System.Globalization;
using System.Text.Json;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Storage;

/// <summary>What an import copied, what it kept aside, and what went wrong.</summary>
/// <param name="Copied">The files copied, by name.</param>
/// <param name="KeptAside">The target's files that were in the way, as they are named now (<c>….before-import-&lt;date&gt;</c>).</param>
/// <param name="Problem">Why it stopped, when it did; the files copied before that stay.</param>
public sealed record DataImportResult(IReadOnlyList<string> Copied, IReadOnlyList<string> KeptAside, string? Problem);

/// <summary>
/// Copies a bar's data from one data folder to another (0.13.0 WP1): this PC's bar into a portable copy, or the data to
/// the folder chosen in Settings. Only what makes the bar: <c>layout.json</c>, <c>workspaces.json</c> and
/// <c>usage.json</c>. Not what belongs to one copy or one PC: <c>command-trust.json</c> (approvals are given where a
/// command runs), the Google sign-in, <c>sync.json</c> (its link to an account or a folder, and the device id: two
/// copies must not be one device), <c>this-pc.json</c>, the logs, the layout's history, the Drive cache, icons downloaded
/// from the web and recordings (the bar names them where they are). A file already in the target is renamed
/// <c>&lt;name&gt;.before-import-&lt;date&gt;</c> first: nothing is written over.
/// </summary>
public static class DataImport
{
    /// <summary>The files copied, in this order: the bar last, so a stop half way leaves the target's bar as it was.</summary>
    public static IReadOnlyList<string> Files { get; } = [WorkspacePositions.FileName, UsageLog.FileName, LayoutStorage.FileName];

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
    public static DataImportResult Run(string source, string target, string? appDrive, DateTimeOffset now)
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
            foreach (var name in Files)
            {
                var from = Path.Combine(source, name);
                if (!File.Exists(from))
                {
                    continue;
                }

                var to = Path.Combine(target, name);
                if (File.Exists(to))
                {
                    var aside = FreeName(KeptAsideName(to, now));
                    File.Move(to, aside);
                    keptAside.Add(Path.GetFileName(aside));
                }

                if (name == LayoutStorage.FileName)
                {
                    CopyLayout(from, to, appDrive);
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

    /// <summary>
    /// The bar, with its paths on the program's drive as <c>%APPDRIVE%</c>, written by the layout's own save. The source is
    /// only read: a damaged one is not moved aside there, and is copied as it is, as is one written by a newer version
    /// (what this version does not know is not rewritten) or one with nothing to change.
    /// </summary>
    private static void CopyLayout(string from, string to, string? appDrive)
    {
        LauncherLayout? layout = null;
        if (appDrive is not null)
        {
            try
            {
                layout = JsonSerializer.Deserialize<LauncherLayout>(File.ReadAllBytes(from), LayoutStorage.JsonOptions);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
            {
                layout = null;
            }
        }

        if (layout is null || LauncherLayout.FormatOf(layout.Version) > LauncherLayout.CurrentVersion)
        {
            File.Copy(from, to);
            return;
        }

        layout.Settings ??= new LauncherSettings();
        layout.Items ??= [];
        new LayoutStorage(to).Save(WithAppDrive(layout, appDrive));
    }

    /// <summary>
    /// <paramref name="layout"/> with every path on <paramref name="appDrive"/> written as <c>%APPDRIVE%</c>: each
    /// shortcut's location (this PC's own too), start folder and icon, and each group's and special icon's icon. The
    /// layout itself, unchanged, when there is no such drive.
    /// </summary>
    public static LauncherLayout WithAppDrive(LauncherLayout layout, string? appDrive)
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

            if (shortcut.Locations is { } byDevice)
            {
                foreach (var device in byDevice.Keys.ToList())
                {
                    byDevice[device] = DataLocation.WithAppDrive(byDevice[device], appDrive);
                }
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
