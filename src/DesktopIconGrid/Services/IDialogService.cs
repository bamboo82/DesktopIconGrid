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
}
