using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Resources;

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

    /// <summary>The sections in menu order, named in the current UI language (built on each access; the lists are small).</summary>
    public static IReadOnlyList<SpecialItemSection> Sections =>
    [
        new(CoreStrings.Section_System, [
            new(CoreStrings.Item_ThisPC, "shell:MyComputerFolder"),
            new(CoreStrings.Item_Network, "shell:NetworkPlacesFolder"),
            new(CoreStrings.Item_NetworkConnections, "shell:ConnectionsFolder"),
            new(CoreStrings.Item_ControlPanel, "shell:ControlPanelFolder"),
            new(CoreStrings.Item_Printers, "shell:PrintersFolder"),
            new(CoreStrings.Item_Settings, "ms-settings:", IconPath: "%SystemRoot%\\ImmersiveControlPanel\\SystemSettings.exe"),
        ]),
        new(CoreStrings.Section_Desktop, [
            new(CoreStrings.Item_RecycleBin, "shell:RecycleBinFolder"),
            new(CoreStrings.Item_ShowDesktop, LaunchPlanner.ShowDesktopLocation, IconPath: Shell32 + ",34"),
            new(CoreStrings.Item_Search, "search-ms:", IconPath: Shell32 + ",22"),
            new(CoreStrings.Item_Run, LaunchPlanner.RunDialogLocation, IconPath: Shell32 + ",24"),
        ]),
        new(CoreStrings.Section_Tools, [
            new(CoreStrings.Item_FileExplorer, "explorer.exe"),
            new(CoreStrings.Item_Calculator, "calc.exe"),
            new(CoreStrings.Item_Notepad, "notepad.exe"),
            new(CoreStrings.Item_Paint, "mspaint.exe"),
            new(CoreStrings.Item_WordPad, "write.exe", RequiredExecutable: "write.exe"),
            new(CoreStrings.Item_CommandPrompt, "cmd.exe"),
            new(CoreStrings.Item_WindowsTerminal, "wt.exe", RequiredExecutable: "wt.exe"),
            new(CoreStrings.Item_TaskManager, "taskmgr.exe"),
        ]),
        new(CoreStrings.Section_Folders, [
            new(CoreStrings.Item_Desktop, "shell:Desktop"),
            new(CoreStrings.Item_Documents, "shell:Personal"),
            new(CoreStrings.Item_Downloads, "shell:Downloads"),
            new(CoreStrings.Item_Pictures, "shell:My Pictures"),
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
