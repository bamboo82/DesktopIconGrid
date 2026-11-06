using DesktopIconGrid.Core.Imports;
using DesktopIconGrid.Resources;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>The Add apps dialog: the list, the filter, what is already on the bar and the Add button.</summary>
public class AddAppViewModelTests
{
    private static readonly IReadOnlyList<InstalledApp> Apps =
    [
        new("Calculator", @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", IsStoreApp: true),
        new("記事本", @"C:\Windows\notepad.exe", IsStoreApp: false),
        new("Visual Studio Code", @"C:\Programs\Code.exe", IsStoreApp: false),
    ];

    [Fact]
    public async Task The_list_filters_as_you_type_marks_what_is_on_the_bar_and_counts_the_ticked_apps()
    {
        var viewModel = new AddAppViewModel(new HashSet<string>([@"c:\windows\NOTEPAD.EXE"], StringComparer.OrdinalIgnoreCase));

        await viewModel.LoadAsync(() => Task.FromResult(Apps), icon: null, CancellationToken.None);

        Assert.Equal(3, viewModel.Apps.Count);
        Assert.True(viewModel.Apps[1].IsOnBar);
        Assert.StartsWith(Strings.AddApp_OnBar, viewModel.Apps[1].Detail, StringComparison.Ordinal);
        Assert.Equal(Strings.AddApp_StoreApp, viewModel.Apps[0].Detail);
        Assert.False(viewModel.CanAdd);

        viewModel.Filter = "CODE";
        Assert.Equal(["Visual Studio Code"], viewModel.View.Cast<AppChoice>().Select(a => a.Name));
        viewModel.Filter = "記事";
        Assert.Equal(["記事本"], viewModel.View.Cast<AppChoice>().Select(a => a.Name));

        viewModel.Apps[0].IsChosen = true;
        viewModel.Apps[2].IsChosen = true;

        Assert.True(viewModel.CanAdd);
        Assert.Equal(string.Format(Strings.AddApp_AddCount, 2), viewModel.AddText);
        Assert.Equal(["Calculator", "Visual Studio Code"], viewModel.Chosen.Select(a => a.Name));
    }

    [Fact]
    public async Task Nothing_listed_says_so()
    {
        var viewModel = new AddAppViewModel(new HashSet<string>());

        await viewModel.LoadAsync(() => Task.FromResult<IReadOnlyList<InstalledApp>>([]), icon: null, CancellationToken.None);

        Assert.Equal(Strings.AddApp_None, viewModel.Status);
        Assert.False(viewModel.IsLoading);
    }
}
