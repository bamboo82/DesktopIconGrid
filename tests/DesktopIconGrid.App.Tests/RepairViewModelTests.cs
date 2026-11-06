using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>The Repair dialog: what it offers and what it returns.</summary>
public class RepairViewModelTests
{
    private static readonly ShortcutItem Missing = new() { Name = "WinSCP", Location = @"U:\E\WinSCP\WinSCP.exe" };

    [Fact]
    public async Task The_places_found_are_listed_with_why_and_the_first_is_chosen()
    {
        var viewModel = new RepairViewModel(
            Missing,
            _ => [new RepairCandidate(@"C:\Apps\WinSCP-6.3\WinSCP.exe", RepairReason.OtherVersion), new RepairCandidate(@"C:\Program Files (x86)\WinSCP\WinSCP.exe", RepairReason.InstalledProgram)],
            canBeOnlyThisPc: true);

        await viewModel.SearchAsync(CancellationToken.None);

        Assert.Equal([Strings.Repair_ReasonOtherVersion, Strings.Repair_ReasonInstalled], viewModel.Choices.Select(c => c.Reason));
        Assert.Same(viewModel.Choices[0], viewModel.SelectedChoice);
        Assert.Equal(string.Format(Strings.Repair_FoundMany, 2), viewModel.Status);
        Assert.Equal(new RepairResult(@"C:\Apps\WinSCP-6.3\WinSCP.exe", OnlyThisPc: true), viewModel.Result); // this PC only unless told otherwise

        viewModel.OnlyThisPc = false;
        viewModel.AddChosen(@"D:\Portable\WinSCP.exe");

        Assert.Equal(new RepairResult(@"D:\Portable\WinSCP.exe", OnlyThisPc: false), viewModel.Result);
        Assert.Equal(Strings.Repair_ReasonChosen, viewModel.SelectedChoice!.Reason);
    }

    [Fact]
    public async Task Nothing_found_leaves_Browse_and_without_a_device_id_the_fix_is_for_every_PC()
    {
        var viewModel = new RepairViewModel(Missing, _ => [], canBeOnlyThisPc: false);

        await viewModel.SearchAsync(CancellationToken.None);

        Assert.Empty(viewModel.Choices);
        Assert.False(viewModel.CanRepair);
        Assert.Equal(Strings.Repair_NothingFound, viewModel.Status);

        viewModel.AddChosen(@"C:\Tools\WinSCP.exe");
        Assert.Equal(new RepairResult(@"C:\Tools\WinSCP.exe", OnlyThisPc: false), viewModel.Result);
    }
}
