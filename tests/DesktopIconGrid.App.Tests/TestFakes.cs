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

    /// <summary>What "the user" does in Properties before OK; <see langword="null"/> cancels.</summary>
    public Action<LauncherItem>? PropertiesEdit { get; set; }

    public bool ShowItemProperties(LauncherItem item, bool isNew)
    {
        if (PropertiesEdit is not { } edit)
        {
            return false;
        }

        edit(item);
        return true;
    }

    /// <summary>What "the user" saves in Settings; <see langword="null"/> cancels.</summary>
    public Func<LauncherSettings, LocalSettings, SettingsResult?> SettingsAnswer { get; set; } = (_, _) => null;

    public SettingsResult? ShowSettings(LauncherSettings current, LocalSettings local) => SettingsAnswer(current, local);

    /// <summary>The text of the last question asked.</summary>
    public string? LastMessage { get; private set; }

    public List<(string Folder, bool NewestFirst)> Stacks { get; } = [];

    /// <summary>What the last stack was given to open a dropped file with a program.</summary>
    public Func<string, IReadOnlyList<string>, DesktopIconGrid.Core.Launching.LaunchResult>? StackOpenWith { get; private set; }

    public void ShowFolderStack(string name, string folder, bool newestFirst, Func<string, IReadOnlyList<string>, DesktopIconGrid.Core.Launching.LaunchResult>? openWith = null)
    {
        Stacks.Add((folder, newestFirst));
        StackOpenWith = openWith;
    }

    public bool ApprovalAnswer { get; set; }

    public List<string> ApprovalsShown { get; } = [];

    public bool ConfirmApproval(string title, string intro, string details, string approveText)
    {
        ApprovalsShown.Add(details);
        return ApprovalAnswer;
    }

    public bool ConfirmDefaultNo(string title, string message) => Confirm(title, message);

    public bool Confirm(string title, string message)
    {
        Confirmations.Add(title);
        LastMessage = message;
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

    /// <summary>What a folder picker answers.</summary>
    public string? Folder { get; set; }

    public string? PickFolder(string title, string? initialFolder) => Folder;

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

    public DesktopIconGrid.Core.Storage.LayoutHistoryEntry? HistoryAnswer { get; set; }

    public IReadOnlyList<DesktopIconGrid.Core.Storage.LayoutHistoryEntry>? HistoryOffered { get; private set; }

    public DesktopIconGrid.Core.Storage.LayoutHistoryEntry? ShowLayoutHistory(Func<IReadOnlyList<DesktopIconGrid.Core.Storage.LayoutHistoryEntry>> list)
    {
        HistoryOffered = list();
        return HistoryAnswer;
    }

    public ViewModels.ImportRequest? ImportAnswer { get; set; }

    public bool? ImportCanGroupSeen { get; private set; }

    public ViewModels.ImportRequest? ShowImport(IReadOnlyList<ShortcutItem> onBar, bool canGroup)
    {
        ImportCanGroupSeen = canGroup;
        return ImportAnswer;
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

    public List<(string Location, IReadOnlyList<string> Paths)> OpenedWith { get; } = [];

    public LaunchResult LaunchWith(ShortcutItem shortcut, IReadOnlyList<string> paths)
    {
        OpenedWith.Add((shortcut.Location, paths));
        return LaunchResult.Failure("nope");
    }
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

    public string? LastPackageFamily { get; private set; }

    public bool TrySwitch(IReadOnlySet<int> processIds, string? packageFamily = null)
    {
        Calls++;
        LastProcessIds = processIds;
        LastPackageFamily = packageFamily;
        return Answer;
    }

    /// <summary>The windows on the screen, topmost first.</summary>
    public List<WindowInfo> Shown { get; } = [];

    /// <summary>
    /// Windows that show at this many looks from now (a program that takes a moment to start); a new one comes to the top.
    /// A workspace start looks once before it starts anything, so a window meant to open for a started member needs 2 or more.
    /// </summary>
    public List<(WindowInfo Window, int Looks)> Coming { get; } = [];

    /// <summary>Where each window is: what <see cref="Capture"/> answers.</summary>
    public Dictionary<IntPtr, DesktopIconGrid.Core.Launching.SavedWindow> Positions { get; } = [];

    public List<(IntPtr Window, DesktopIconGrid.Core.Launching.SavedWindow Target)> Placed { get; } = [];

    public IReadOnlyList<WindowInfo> Windows(bool withAppIds = false)
    {
        var still = new List<(WindowInfo Window, int Looks)>();
        foreach (var (window, looks) in Coming)
        {
            if (looks <= 1)
            {
                Shown.Insert(0, window);
            }
            else
            {
                still.Add((window, looks - 1));
            }
        }

        Coming.Clear();
        Coming.AddRange(still);
        return Shown.ToList();
    }

    public DesktopIconGrid.Core.Launching.SavedWindow? Capture(IntPtr window) =>
        Shown.Any(shown => shown.Handle == window) ? Positions.GetValueOrDefault(window) : null;

    public bool Place(IntPtr window, DesktopIconGrid.Core.Launching.SavedWindow target)
    {
        if (!Shown.Any(shown => shown.Handle == window))
        {
            return false;
        }

        Placed.Add((window, target));
        Positions[window] = target;
        return true;
    }

    /// <summary>A program's main window (<paramref name="bare"/>: no title bar and no border, like a splash screen).</summary>
    public static WindowInfo Window(int handle, int processId, bool bare = false) =>
        new(new IntPtr(handle), processId, "Main", IsVisible: true, IsCloaked: false, HasOwner: false, IsToolWindow: false, IsAppWindow: false, IsMinimized: false, IsBare: bare);
}

/// <summary>
/// A launcher bar on the fakes above, for tests that need a view model, or a window built on one. Nothing counts as
/// missing unless <c>isMissing</c> says so: the real check would find made-up paths missing on a CI machine.
/// </summary>
internal sealed class TestBar : IDisposable
{
    public TestBar(
        string directory,
        LauncherModel model,
        ILaunchService? launcher = null,
        IWindowSwitcher? windows = null,
        Func<ShortcutItem, bool>? isMissing = null,
        IEnumerable<ISpecialIconHandler>? handlers = null,
        DesktopIconGrid.Core.SpecialIcons.CommandTrust? trust = null)
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
            new SpecialIconRegistry(handlers ?? []),
            action => action(),
            windows,
            isMissing ?? (_ => false),
            trust: trust);
    }

    public LauncherModel Model { get; }

    public LauncherViewModel ViewModel { get; }

    public RecordingDialogs Dialogs { get; } = new();

    public ProcessTracker Tracker { get; }

    public void Dispose() => Tracker.Dispose();
}
