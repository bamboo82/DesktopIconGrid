using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>Stands in for the dialogs: records what the bar asked or told, and answers as a test says.</summary>
internal sealed class RecordingDialogs : IDialogService
{
    public bool ConfirmAnswer { get; set; } = true;

    public List<string> Confirmations { get; } = [];

    public List<string> Notices { get; } = [];

    public List<string> Modals { get; } = [];

    public bool ShowItemProperties(LauncherItem item, bool isNew) => false;

    public LauncherSettings? ShowSettings(LauncherSettings current) => null;

    public bool Confirm(string title, string message)
    {
        Confirmations.Add(title);
        return ConfirmAnswer;
    }

    public void ShowError(string title, string message) => Modals.Add(title);

    public void ShowInfo(string title, string message) => Modals.Add(title);

    public void Notify(string title, string message) => Notices.Add(title);

    public void ShowSync()
    {
    }

    public void ShowLog(LogViewerViewModel viewModel)
    {
    }

    public void ShowNameTable(LauncherViewModel launcher)
    {
    }

    public void ShowAppLog()
    {
    }

    public string? TextAnswer { get; set; }

    /// <summary>The text the last question offered to start from.</summary>
    public string? OfferedText { get; private set; }

    public string? AskText(string title, string prompt, string? initialValue)
    {
        OfferedText = initialValue;
        return TextAnswer;
    }

    public string? PickFolder(string title, string? initialFolder) => null;

    public List<string> RepairRequests { get; } = [];

    public RepairResult? RepairAnswer { get; set; }

    public RepairResult? ShowRepair(ShortcutItem shortcut)
    {
        RepairRequests.Add(shortcut.Name);
        return RepairAnswer;
    }

    public IReadOnlyList<InstalledApp> AddAppAnswer { get; set; } = [];

    public IReadOnlySet<string>? OnBarSeen { get; private set; }

    public IReadOnlyList<InstalledApp> ShowAddApp(IReadOnlySet<string> locationsOnBar)
    {
        OnBarSeen = locationsOnBar;
        return AddAppAnswer;
    }
}

/// <summary>Records every launch and answers with <see cref="Result"/> (a failure unless a test says otherwise).</summary>
internal sealed class RecordingLauncher : ILaunchService
{
    public List<(string Location, bool AsAdministrator)> Launches { get; } = [];

    public LaunchResult Result { get; set; } = LaunchResult.Failure("nope");

    public LaunchResult Launch(ShortcutItem shortcut, bool asAdministrator = false)
    {
        Launches.Add((shortcut.Location, asAdministrator));
        return Result;
    }

    public LaunchResult LaunchWith(ShortcutItem shortcut, IReadOnlyList<string> paths) => LaunchResult.Failure("nope");
}

internal sealed class NoIcons : IIconProvider
{
    public ImageSource? GetIcon(string source, int pixelSize) => null;

    public ImageSource? GetItemIcon(LauncherItem item, int pixelSize) => null;

    public ImageSource? GetFolderIcon(int pixelSize) => null;

    public bool CanResolve(string? source) => false;

    public void ClearCache()
    {
    }
}

/// <summary>Stands in for the window switcher: records what it was asked and answers as told.</summary>
internal sealed class FakeSwitcher : IWindowSwitcher
{
    public bool Answer { get; set; } = true;

    public int Calls { get; private set; }

    public IReadOnlySet<int>? LastProcessIds { get; private set; }

    public bool TrySwitch(IReadOnlySet<int> processIds)
    {
        Calls++;
        LastProcessIds = processIds;
        return Answer;
    }
}

/// <summary>
/// A launcher bar on the fakes above, for tests that need a view model, or a window built on one. Nothing counts as
/// missing unless <c>isMissing</c> says so: the real check would find made-up paths missing on a CI machine.
/// </summary>
internal sealed class TestBar : IDisposable
{
    public TestBar(string directory, LauncherModel model, ILaunchService? launcher = null, IWindowSwitcher? windows = null, Func<ShortcutItem, bool>? isMissing = null)
    {
        Model = model;
        Tracker = new ProcessTracker(Dispatcher.CurrentDispatcher, Path.Combine(directory, "logs"));
        ViewModel = new LauncherViewModel(
            model,
            new LayoutStorage(Path.Combine(directory, "layout.json")),
            new NoIcons(),
            Dialogs,
            launcher ?? new RecordingLauncher(),
            Tracker,
            new SpecialIconRegistry([]),
            action => action(),
            windows,
            isMissing ?? (_ => false));
    }

    public LauncherModel Model { get; }

    public LauncherViewModel ViewModel { get; }

    public RecordingDialogs Dialogs { get; } = new();

    public ProcessTracker Tracker { get; }

    public void Dispose() => Tracker.Dispose();
}
