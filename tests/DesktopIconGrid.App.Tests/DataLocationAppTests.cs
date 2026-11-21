using System.IO;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>0.13.0 WP1: Settings ▸ General ▸ Data location, a move of the data, and this PC's bar brought into a portable copy.</summary>
public sealed class DataLocationAppTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dig-data-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>A copy in <c>program</c>, this PC's data in <c>local</c>, the data now in <paramref name="now"/>.</summary>
    private DataFolders Folders(string now, bool hasMarker, string line = "", bool portable = false) =>
        new(Folder("program"), Folder("local"), now, hasMarker, line, portable);

    private static SettingsViewModel Settings(DataFolders folders)
    {
        var viewModel = new SettingsViewModel(new LauncherSettings());
        viewModel.UseDataFolders(folders);
        return viewModel;
    }

    [Fact]
    public void Settings_shows_the_choice_portable_txt_holds_and_where_the_data_is_now()
    {
        var thisPc = Settings(Folders(Path.Combine(_root, "local"), hasMarker: false));
        Assert.True(thisPc.DataIsThisPc);
        Assert.False(thisPc.DataIsPortable);
        Assert.Equal(".", thisPc.DataFolder);
        Assert.Equal(Path.Combine(_root, "local"), thisPc.DataNow);
        Assert.False(thisPc.DataLocationChanged);
        Assert.Equal(string.Format(Strings.Settings_DataThisPc, @"%LocalAppData%\DesktopIconGrid"), thisPc.DataThisPcText);

        var portable = Settings(Folders(Path.Combine(_root, "program", "data"), hasMarker: true, line: "data", portable: true));
        Assert.True(portable.DataIsPortable);
        Assert.Equal("data", portable.DataFolder);
        Assert.False(portable.DataLocationChanged);
        portable.DataFolder = @".\data\"; // the same folder, written another way
        Assert.False(portable.DataLocationChanged);
        portable.DataFolder = "other";
        Assert.True(portable.DataLocationChanged);
    }

    [Fact]
    public void A_name_that_is_no_folders_holds_Save_back_on_the_General_tab()
    {
        var viewModel = Settings(Folders(Path.Combine(_root, "local"), hasMarker: false));
        viewModel.DataIsPortable = true;
        viewModel.DataFolder = @"D:\what?";

        Assert.False(viewModel.CanSave);
        Assert.Equal("\u26A0 " + Strings.Settings_TabPortable, viewModel.PortableTab); // 0.14.0 WP4: on its own tab
        Assert.Equal(string.Format(Strings.Settings_FixOnTab, Strings.Settings_TabPortable), viewModel.ProblemText);
        Assert.Equal(Strings.Settings_TabGeneral, viewModel.GeneralTab);
        Assert.Equal(string.Format(Strings.Settings_DataNotAFolder, @"D:\what?"), viewModel.DataProblem);

        viewModel.DataIsThisPc = true; // back to where it was: nothing to fix
        Assert.True(viewModel.CanSave);
        Assert.Equal(Strings.Settings_TabGeneral, viewModel.GeneralTab);
    }

    [Fact]
    public void Save_checks_the_folder_can_be_made_and_written_and_plans_the_move_without_writing()
    {
        var viewModel = Settings(Folders(Path.Combine(_root, "local"), hasMarker: false));
        viewModel.DataIsPortable = true;
        viewModel.DataFolder = "data";
        var full = Path.Combine(_root, "program") + @"\data";

        Assert.False(viewModel.TryPlanDataMove(_ => false, out var refused));
        Assert.Null(refused);
        Assert.Equal(string.Format(Strings.Settings_DataCannotUse, full), viewModel.DataProblem);
        Assert.False(viewModel.CanSave);

        viewModel.DataFolder = "data "; // a change takes the reason away
        Assert.Equal(string.Empty, viewModel.DataProblem);
        Assert.True(viewModel.TryPlanDataMove(folder => folder == full, out var move));
        Assert.Equal(new DataMove(true, "data", full, Copy: false), move);
        Assert.False(File.Exists(Path.Combine(_root, "program", DataLocation.PortableMarker))); // nothing written yet

        // Back to this PC from a portable copy: portable.txt is to go.
        var back = Settings(Folders(Path.Combine(_root, "program"), hasMarker: true, line: ".", portable: true));
        back.DataIsThisPc = true;
        Assert.True(back.TryPlanDataMove(_ => true, out var home));
        Assert.Equal(new DataMove(false, string.Empty, Path.Combine(_root, "local"), Copy: false), home);

        // Nothing changed: nothing to do.
        Assert.True(Settings(Folders(Path.Combine(_root, "local"), hasMarker: false)).TryPlanDataMove(_ => throw new InvalidOperationException(), out var none));
        Assert.Null(none);
    }

    [Fact]
    public void A_copy_is_offered_only_to_another_folder_from_one_with_a_bar_and_Import_only_in_a_portable_copy()
    {
        var local = Path.Combine(_root, "local");
        var viewModel = Settings(Folders(local, hasMarker: false));
        var elsewhere = new DataMove(true, "data", Path.Combine(_root, "program", "data"), Copy: false);
        Assert.False(viewModel.OffersCopy(elsewhere)); // no bar to copy yet
        File.WriteAllText(Path.Combine(local, LayoutStorage.FileName), "{}");
        Assert.True(viewModel.OffersCopy(elsewhere));
        Assert.False(viewModel.OffersCopy(elsewhere with { Folder = local + Path.DirectorySeparatorChar }));
        Assert.False(viewModel.ShowsDataImport);

        var portable = Settings(Folders(Folder("stick-data"), hasMarker: true, line: @"..\stick-data", portable: true));
        Assert.True(portable.ShowsDataImport);
        File.Delete(Path.Combine(local, LayoutStorage.FileName));
        Assert.False(Settings(Folders(Folder("stick-data"), hasMarker: true, line: @"..\stick-data", portable: true)).ShowsDataImport); // nothing to bring
    }

    [Fact]
    public async Task A_move_copies_writes_portable_txt_and_offers_a_restart()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var local = Folder("local");
        File.WriteAllText(Path.Combine(local, LayoutStorage.FileName), """{"items":[]}""");
        File.WriteAllText(Path.Combine(local, Core.SpecialIcons.CommandTrust.FileName), "{}");
        var target = Path.Combine(_root, "program", "data");
        using var bar = new TestBar(Folder("bar"), new LauncherModel());
        bar.ViewModel.DataFoldersNow = () => Folders(local, hasMarker: false);
        var restarts = 0;
        bar.ViewModel.RestartRequested += (_, _) => restarts++;
        bar.Dialogs.ChoiceAnswers.Enqueue(0); // Restart now

        await bar.ViewModel.MoveDataAsync(new DataMove(true, "data", target, Copy: true));

        Assert.True(File.Exists(Path.Combine(target, LayoutStorage.FileName)));
        Assert.False(File.Exists(Path.Combine(target, Core.SpecialIcons.CommandTrust.FileName))); // approvals stay where they were given
        Assert.Equal("data" + Environment.NewLine, File.ReadAllText(Path.Combine(_root, "program", DataLocation.PortableMarker)));
        Assert.Equal(string.Format(Strings.Data_RestartQuestion, target, local), Assert.Single(bar.Dialogs.Choices));
        Assert.Equal(1, restarts);

        // Back to this PC, Later: portable.txt goes, nothing restarts.
        bar.ViewModel.DataFoldersNow = () => Folders(target, hasMarker: true, line: "data", portable: true);
        bar.Dialogs.ChoiceAnswers.Enqueue(1);
        await bar.ViewModel.MoveDataAsync(new DataMove(false, string.Empty, local, Copy: false));
        Assert.False(File.Exists(Path.Combine(_root, "program", DataLocation.PortableMarker)));
        Assert.Equal(1, restarts);
    }

    [Fact]
    public async Task A_program_folder_that_cannot_be_written_changes_nothing_and_says_so()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var bar = new TestBar(Folder("bar"), new LauncherModel());
        var gone = Path.Combine(_root, "no-such-program-folder"); // as unwritable as Program Files for this test
        bar.ViewModel.DataFoldersNow = () => new DataFolders(gone, Folder("local"), Path.Combine(_root, "local"), false, string.Empty, false);

        await bar.ViewModel.MoveDataAsync(new DataMove(true, ".", gone, Copy: true));

        Assert.Equal([Strings.Data_Title], bar.Dialogs.Modals);
        Assert.Empty(bar.Dialogs.Choices);
        Assert.False(Directory.Exists(gone));
    }

    [Fact]
    public void Importing_this_PCs_bar_leaves_a_request_for_the_start_and_restarts()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var data = Folder("stick-data");
        using var bar = new TestBar(Folder("bar"), new LauncherModel());
        bar.ViewModel.DataFoldersNow = () => Folders(data, hasMarker: true, line: @"..\stick-data", portable: true);
        var restarts = 0;
        bar.ViewModel.RestartRequested += (_, _) => restarts++;

        bar.ViewModel.BringThisPcBar();

        Assert.True(File.Exists(Path.Combine(data, DataImport.RequestFile)));
        Assert.Equal(1, restarts);
    }

    [Fact]
    public void Settings_with_Import_closes_without_keeping_its_other_changes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var data = Folder("stick-data");
        using var bar = new TestBar(Folder("bar"), new LauncherModel());
        bar.ViewModel.DataFoldersNow = () => Folders(data, hasMarker: true, line: ".", portable: true);
        var before = bar.ViewModel.Model.Settings.IconSize;
        bar.Dialogs.SettingsAnswer = (settings, local) =>
        {
            var changed = settings.Clone();
            changed.IconSize = before == 48 ? 32 : 48;
            return new SettingsResult(changed, local, ImportThisPc: true);
        };

        bar.ViewModel.OpenSettings();

        Assert.Equal(before, bar.ViewModel.Model.Settings.IconSize);
        Assert.True(File.Exists(Path.Combine(data, DataImport.RequestFile)));
    }
}
