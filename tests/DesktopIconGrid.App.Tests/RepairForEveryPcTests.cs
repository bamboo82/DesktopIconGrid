using System.IO;
using System.Windows.Threading;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// Repair for this PC only or for every PC. These tests set <see cref="ThisDevice.Id"/>, which per-PC locations and every
/// approval in <see cref="Core.SpecialIcons.CommandTrust"/> read, so they run on their own, after the parallel ones: in
/// run 254 a Send to PC test running beside them approved under one id and looked under the other.
/// </summary>
[Collection(ThisDeviceCollection.Name)]
public sealed class RepairForEveryPcTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-repair-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>A bar of three shortcuts to made-up paths, none of them counted as missing.</summary>
    private (LauncherViewModel ViewModel, LauncherModel Model, RecordingDialogs Dialogs) Create()
    {
        var model = new LauncherModel();
        model.Add(new ShortcutItem { Name = "A", Location = "C:\\a.exe" });
        model.Add(new ShortcutItem { Name = "B", Location = "C:\\b.exe" });
        model.Add(new ShortcutItem { Name = "C", Location = "C:\\c.exe" });
        var dialogs = new RecordingDialogs();
        var tracker = new ProcessTracker(Dispatcher.CurrentDispatcher, Path.Combine(_directory, "logs"));
        var viewModel = new LauncherViewModel(
            model,
            new LayoutStorage(Path.Combine(_directory, "layout.json")),
            new NoIcons(),
            dialogs,
            new RecordingLauncher(),
            tracker,
            new SpecialIconRegistry([]),
            action => action(),
            null,
            _ => false);
        return (viewModel, model, dialogs);
    }

    [Fact]
    public void Repair_for_every_PC_keeps_another_PCs_own_location_and_moves_a_start_in_that_was_the_program_folder()
    {
        var previous = ThisDevice.Id;
        ThisDevice.Id = "this-pc";
        try
        {
            var (viewModel, model, dialogs) = Create();
            var shortcut = (ShortcutItem)model.Items[0];
            shortcut.SetLocationFor("other-pc", @"D:\Tools\a.exe"); // that PC's path differs, so it has its own
            shortcut.StartIn = @"C:\"; // the folder of C:\a.exe, as a Windows shortcut sets it
            var other = (ShortcutItem)model.Items[1];
            other.StartIn = @"D:\Projects";
            dialogs.RepairAnswer = new RepairResult(@"E:\Apps\a.exe", OnlyThisPc: false);

            viewModel.RepairItem(viewModel.RootItems.Items[0]);

            Assert.Equal(@"E:\Apps\a.exe", shortcut.Location);
            Assert.Equal(@"D:\Tools\a.exe", shortcut.LocationFor("other-pc"));
            Assert.Null(shortcut.StartIn); // it moves with the program: each PC starts it in its own folder

            dialogs.RepairAnswer = new RepairResult(@"E:\Apps\b.exe", OnlyThisPc: true);
            viewModel.RepairItem(viewModel.RootItems.Items[1]);

            Assert.Equal(@"D:\Projects", other.StartIn); // a folder of its own stays
        }
        finally
        {
            ThisDevice.Id = previous;
        }
    }

    [Fact]
    public void Repair_points_the_shortcut_at_the_chosen_path_for_this_PC_only_or_for_every_PC()
    {
        var previous = ThisDevice.Id;
        ThisDevice.Id = "this-pc";
        try
        {
            var (viewModel, model, dialogs) = Create();
            var item = viewModel.RootItems.Items[0];
            var shortcut = (ShortcutItem)model.Items[0];
            dialogs.RepairAnswer = new RepairResult(@"D:\Apps\a.exe", OnlyThisPc: true);

            viewModel.RepairItem(item);

            Assert.Equal(@"C:\a.exe", shortcut.Location); // other PCs keep theirs
            Assert.Equal(@"D:\Apps\a.exe", shortcut.EffectiveLocation);
            Assert.Equal(@"D:\Apps\a.exe", item.Location);

            dialogs.RepairAnswer = new RepairResult(@"E:\a.exe", OnlyThisPc: false);
            viewModel.RepairItem(item);

            Assert.Equal(@"E:\a.exe", shortcut.Location);
            Assert.Null(shortcut.Locations);

            dialogs.RepairAnswer = null; // Cancel
            viewModel.RepairItem(item);

            Assert.Equal(@"E:\a.exe", shortcut.Location);
            Assert.Equal(3, dialogs.RepairRequests.Count);
        }
        finally
        {
            ThisDevice.Id = previous;
        }
    }
}

/// <summary>
/// The App tests that set <see cref="ThisDevice.Id"/> (CLAUDE.md: such classes go in "This device"). Parallel tests read
/// the id through approvals and per-PC locations, so this collection runs alone, after them.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ThisDeviceCollection
{
    public const string Name = "This device";
}
