using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Services;

public interface IDialogService
{
    /// <summary>Shows the Shortcut/Group Properties dialog. On Save the edits are written into <paramref name="item"/>.</summary>
    bool ShowItemProperties(LauncherItem item, bool isNew);

    /// <summary>Shows the Settings dialog; returns the edited settings or <see langword="null"/> on cancel.</summary>
    LauncherSettings? ShowSettings(LauncherSettings current);

    bool Confirm(string title, string message);

    /// <summary>
    /// Asks whether something that came with the bar may act on this PC (a health check), showing its full details in a
    /// wrapped, scrollable box; Cancel is the default.
    /// </summary>
    bool ConfirmApproval(string title, string intro, string details, string approveText);

    void ShowError(string title, string message);

    void ShowInfo(string title, string message);

    /// <summary>A notice that needs no decision: shown without interrupting (tray balloon), and logged.</summary>
    void Notify(string title, string message);

    /// <summary>Shows the Google Drive sync dialog.</summary>
    void ShowSync();

    /// <summary>Opens a non-modal window with a shortcut's captured output.</summary>
    void ShowLog(LogViewerViewModel viewModel);

    /// <summary>Shows the table of all item names and applies the edits.</summary>
    void ShowNameTable(LauncherViewModel launcher);

    /// <summary>Opens (or brings to the front) the application log viewer.</summary>
    void ShowAppLog();

    /// <summary>Asks for a line of text, starting from <paramref name="initialValue"/>; <see langword="null"/> on Cancel.</summary>
    string? AskText(string title, string prompt, string? initialValue);

    /// <summary>Asks for a folder, starting in <paramref name="initialFolder"/> when it exists; <see langword="null"/> on Cancel.</summary>
    string? PickFolder(string title, string? initialFolder);

    /// <summary>The Repair dialog for a shortcut whose program is not on this PC; <see langword="null"/> on Cancel.</summary>
    RepairResult? ShowRepair(ShortcutItem shortcut);

    /// <summary>Opens a folder as a stack at the pointer (not modal; it closes itself when it loses the focus).</summary>
    void ShowFolderStack(string name, string folder, bool newestFirst);

    /// <summary>The Add apps dialog; the apps the user ticked (none on Cancel).</summary>
    IReadOnlyList<InstalledApp> ShowAddApp(IReadOnlySet<string> locationsOnBar);

    /// <summary>
    /// The Import dialog: what other programs keep on this PC, against the shortcuts on the bar (<paramref name="onBar"/>);
    /// <see langword="null"/> on Cancel. <paramref name="canGroup"/> offers a group per source.
    /// </summary>
    ViewModels.ImportRequest? ShowImport(IReadOnlyList<ShortcutItem> onBar, bool canGroup);

    /// <summary>
    /// Restore an earlier layout: the entry picked from what <paramref name="list"/> reads (off the window's thread: up to
    /// twenty files, maybe on a stick), or <see langword="null"/> on Cancel.
    /// </summary>
    Core.Storage.LayoutHistoryEntry? ShowLayoutHistory(Func<IReadOnlyList<Core.Storage.LayoutHistoryEntry>> list);
}
