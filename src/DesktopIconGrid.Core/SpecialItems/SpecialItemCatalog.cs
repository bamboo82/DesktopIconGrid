using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.SpecialItems;

/// <summary>A ready-made shortcut offered by the "Add special item" menu.</summary>
/// <param name="Name">Menu caption and default shortcut name.</param>
/// <param name="Location">Shortcut location.</param>
/// <param name="Parameters">Optional arguments.</param>
/// <param name="IconPath">Icon override for locations the shell cannot produce an icon for.</param>
/// <param name="RequiredExecutable">Executable that must exist for the entry to be offered.</param>
public sealed record SpecialItemDefinition(
    string Name,
    string Location,
    string? Parameters = null,
    string? IconPath = null,
    string? RequiredExecutable = null)
{
    public ShortcutItem CreateShortcut() => new()
    {
        Name = Name,
        Location = Location,
        Parameters = Parameters,
        IconPath = IconPath,
    };
}

/// <summary>A separator-delimited section of the "Add special item" menu.</summary>
public sealed record SpecialItemSection(string Title, IReadOnlyList<SpecialItemDefinition> Items);

/// <summary>The built-in catalog of Windows locations and tools.</summary>
public static class SpecialItemCatalog
{
    private const string Shell32 = "%SystemRoot%\\System32\\shell32.dll";

    public static IReadOnlyList<SpecialItemSection> Sections { get; } =
    [
        new("System", [
            new("This PC", "shell:MyComputerFolder"),
            new("Network", "shell:NetworkPlacesFolder"),
            new("Network Connections", "shell:ConnectionsFolder"),
            new("Control Panel", "shell:ControlPanelFolder"),
            new("Printers", "shell:PrintersFolder"),
            new("Settings", "ms-settings:", IconPath: "%SystemRoot%\\ImmersiveControlPanel\\SystemSettings.exe"),
        ]),
        new("Desktop", [
            new("Recycle Bin", "shell:RecycleBinFolder"),
            new("Show Desktop", LaunchPlanner.ShowDesktopLocation, IconPath: Shell32 + ",34"),
            new("Search", "search-ms:", IconPath: Shell32 + ",22"),
            new("Run…", LaunchPlanner.RunDialogLocation, IconPath: Shell32 + ",24"),
        ]),
        new("Tools", [
            new("File Explorer", "explorer.exe"),
            new("Calculator", "calc.exe"),
            new("Notepad", "notepad.exe"),
            new("Paint", "mspaint.exe"),
            new("WordPad", "write.exe", RequiredExecutable: "write.exe"),
            new("Command Prompt", "cmd.exe"),
            new("Windows Terminal", "wt.exe", RequiredExecutable: "wt.exe"),
            new("Task Manager", "taskmgr.exe"),
        ]),
        new("Folders", [
            new("Desktop", "shell:Desktop"),
            new("Documents", "shell:Personal"),
            new("Downloads", "shell:Downloads"),
            new("Pictures", "shell:My Pictures"),
        ]),
    ];

    /// <summary>All definitions in menu order, regardless of availability.</summary>
    public static IEnumerable<SpecialItemDefinition> All => Sections.SelectMany(s => s.Items);

    /// <summary>The sections with entries whose required executable is missing filtered out.</summary>
    public static IReadOnlyList<SpecialItemSection> GetAvailableSections(Func<string, bool>? executableExists = null)
    {
        executableExists ??= ExecutableLocator.Exists;
        return Sections
            .Select(section => new SpecialItemSection(
                section.Title,
                section.Items.Where(i => i.RequiredExecutable is null || executableExists(i.RequiredExecutable)).ToList()))
            .Where(section => section.Items.Count > 0)
            .ToList();
    }
}
