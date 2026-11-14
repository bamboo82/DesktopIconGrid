using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Services;

public interface IDialogService
{
    /// <summary>Shows the Shortcut/Group Properties dialog. On Save the edits are written into <paramref name="item"/>.</summary>
    bool ShowItemProperties(LauncherItem item, bool isNew);

    /// <summary>Shows the Settings dialog; returns the edited settings or <see langword="null"/> on cancel.</summary>
    /// <summary>The Settings dialog over copies of the bar's settings and this PC's own; what was saved, or <see langword="null"/>.</summary>
    SettingsResult? ShowSettings(LauncherSettings current, DesktopIconGrid.Core.Storage.LocalSettings local);

    bool Confirm(string title, string message);

    /// <summary>
    /// A Yes/No question whose default is No, for something that leaves this PC (pasted text): Enter right after the
    /// key that asked must not be a yes.
    /// </summary>
    bool ConfirmDefaultNo(string title, string message);

    /// <summary>
    /// Asks whether something that came with the bar may act on this PC (a health check), showing its full details in a
    /// wrapped, scrollable box; Cancel is the default.
    /// </summary>
    bool ConfirmApproval(string title, string intro, string details, string approveText);

    void ShowError(string title, string message);

    void ShowInfo(string title, string message);

    /// <summary>
    /// A notice that needs no decision and reports a failure or a problem: shown without interrupting (tray balloon), and
    /// logged as a warning.
    /// </summary>
    void Notify(string title, string message);

    /// <summary>
    /// A notice that reports no failure (a file received, a timer's phase): shown as <see cref="Notify"/> shows it, and
    /// logged as information, not as a warning (the release check of 0.11.0).
    /// </summary>
    void Inform(string title, string message);

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

    /// <summary>
    /// Opens a folder as a stack at the pointer (not modal; it closes itself when it loses the focus).
    /// <paramref name="openWith"/> opens files with a program of the stack (a file dropped onto it there).
    /// </summary>
    void ShowFolderStack(string name, string folder, bool newestFirst, Func<string, IReadOnlyList<string>, LaunchResult>? openWith = null);

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

/// <summary>What the Settings dialog saved: the bar's settings, which travel with it, and this PC's own, which do not.</summary>
public sealed record SettingsResult(LauncherSettings Settings, DesktopIconGrid.Core.Storage.LocalSettings Local);
