using System.IO;
using System.Net.Http;
using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>The sync dialog's choice of where the copies are kept.</summary>
public sealed class SyncViewModelTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("dig-sync-vm-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private sealed class NoDrive : IDriveClient
    {
        public Task<DriveUser> GetUserAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<IReadOnlyList<DriveFile>> ListFilesAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<DriveFile> UploadJsonAsync(string fileName, string json, IReadOnlyDictionary<string, string> appProperties, string? existingFileId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<string> DownloadTextAsync(string fileId, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task DeleteAsync(string fileId, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class Host : ISyncHost
    {
        public event EventHandler? LayoutChanged
        {
            add { }
            remove { }
        }

        public LauncherLayout ExportLayout() => new();

        public void ReplaceLayout(LauncherLayout layout)
        {
        }
    }

    /// <summary>Answers the folder picker and the confirmation as told.</summary>
    private sealed class Dialogs : IDialogService
    {
        public string? Folder { get; set; }

        public bool ConfirmAnswer { get; set; }

        public int Confirmations { get; private set; }

        public string? LastMessage { get; private set; }

        public bool ShowItemProperties(LauncherItem item, bool isNew) => false;

        public LauncherSettings? ShowSettings(LauncherSettings current) => null;

        public bool Confirm(string title, string message)
        {
            Confirmations++;
            LastMessage = message;
            return ConfirmAnswer;
        }

        public void ShowError(string title, string message)
        {
        }

        public void ShowInfo(string title, string message)
        {
        }

        public void Notify(string title, string message)
        {
        }

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

        public string? AskText(string title, string prompt, string? initialValue) => null;

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

        public ViewModels.ImportRequest? ImportAnswer { get; set; }

        public bool? ImportCanGroupSeen { get; private set; }

        public ViewModels.ImportRequest? ShowImport(IReadOnlyList<ShortcutItem> onBar, bool canGroup)
        {
            ImportCanGroupSeen = canGroup;
            return ImportAnswer;
        }
    }

    [Fact]
    public void Replacing_a_sync_programs_copy_says_this_PC_stays_with_it_and_leaves_the_original()
    {
        // Review 15, on entry 24: the copy keeps its own name when replaced, so this PC leaves "Work" for it.
        var settings = new SyncSettings { DeviceName = "PC-1", SyncFolder = _folder };
        var auth = new GoogleAuthorization(new HttpClient(), new InMemoryTokenStore(), () => new OAuthClientCredentials("id", "secret"));
        var dialogs = new Dialogs { ConfirmAnswer = false };
        var viewModel = new SyncViewModel(new SyncCoordinator(new SyncService(auth, new NoDrive(), settings, () => { }), new Host(), (_, _) => true), dialogs);
        viewModel.SelectedProfile = new RemoteProfile("Work-LAPTOP", "Work-LAPTOP.json", null, "LAPTOP", 10) { NameInside = "Work" };

        viewModel.OverwriteCommand.Execute(null);

        Assert.Equal(string.Format(Strings.Sync_ReplaceConflictCopy, "Work-LAPTOP", "Work"), dialogs.LastMessage);

        viewModel.SelectedProfile = new RemoteProfile("Home", "Home.json", null, "LAPTOP", 10) { NameInside = "Home" };
        viewModel.OverwriteCommand.Execute(null);
        Assert.Equal(string.Format(Strings.Sync_ReplaceSelected, "Home"), dialogs.LastMessage);
    }

    [Fact]
    public async Task Choosing_a_folder_asks_before_unlinking_then_keeps_the_copies_there_without_a_sign_in()
    {
        var settings = new SyncSettings { DeviceName = "PC-1", LinkedProfile = "Work", LinkedFileId = "drive-id" };
        var auth = new GoogleAuthorization(new HttpClient(), new InMemoryTokenStore(), () => new OAuthClientCredentials("id", "secret"));
        var service = new SyncService(auth, new NoDrive(), settings, () => { });
        var dialogs = new Dialogs { Folder = _folder, ConfirmAnswer = false };
        var viewModel = new SyncViewModel(new SyncCoordinator(service, new Host(), (_, _) => true), dialogs);
        Assert.False(viewModel.IsConnected);

        await viewModel.ChooseFolderAsync(); // "No" to unlinking

        Assert.Equal(1, dialogs.Confirmations);
        Assert.False(viewModel.UsesFolder);
        Assert.Equal("Work", settings.LinkedProfile);

        dialogs.ConfirmAnswer = true;
        await viewModel.ChooseFolderAsync(); // and the empty folder is listed

        Assert.True(viewModel.UsesFolder);
        Assert.Equal(Path.GetFullPath(_folder), viewModel.SyncFolder);
        Assert.Null(settings.LinkedProfile);
        Assert.True(viewModel.IsConnected);
        Assert.False(viewModel.IsGoogleConnected);
        Assert.Empty(viewModel.Profiles);

        await viewModel.UseGoogleDriveAsync(); // not linked any more: nothing to ask
        Assert.Equal(2, dialogs.Confirmations);
        Assert.True(viewModel.UsesGoogle);
        Assert.Equal(string.Empty, viewModel.SyncFolder);
    }

    [Fact]
    public void The_dialog_says_when_sync_json_could_not_be_read_at_start()
    {
        var store = new SyncSettingsStore(Path.Combine(_folder, SyncSettingsStore.FileName));
        store.Save(new SyncSettings { DeviceName = "PC-1", LinkedProfile = "Work" });
        SyncSettings standIn;
        using (new FileStream(store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            standIn = store.Load(); // another program holds it
        }

        var auth = new GoogleAuthorization(new HttpClient(), new InMemoryTokenStore(), () => new OAuthClientCredentials("id", "secret"));
        SyncViewModel Dialog(SyncSettings settings) =>
            new(new SyncCoordinator(new SyncService(auth, new NoDrive(), settings, () => store.Save(settings)), new Host(), (_, _) => true), new Dialogs());

        Assert.True(Dialog(standIn).SettingsNotRead);
        Assert.False(Dialog(store.Load()).SettingsNotRead);
    }

    [Fact]
    public async Task Cancelling_the_folder_picker_changes_nothing()
    {
        var settings = new SyncSettings { DeviceName = "PC-1" };
        var auth = new GoogleAuthorization(new HttpClient(), new InMemoryTokenStore(), () => new OAuthClientCredentials("id", "secret"));
        var viewModel = new SyncViewModel(new SyncCoordinator(new SyncService(auth, new NoDrive(), settings, () => { }), new Host(), (_, _) => true), new Dialogs());

        await viewModel.ChooseFolderAsync();

        Assert.False(viewModel.UsesFolder);
        Assert.Null(settings.SyncFolder);
    }
}
