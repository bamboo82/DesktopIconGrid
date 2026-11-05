using System.Text.Json;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>The settings dialog edits a copy of the settings, so what it does not show is kept.</summary>
public class SettingsViewModelTests
{
    [Fact]
    public void Saving_the_dialog_keeps_settings_it_does_not_show()
    {
        using var future = JsonDocument.Parse("\"kept\"");
        var current = new LauncherSettings
        {
            IconSize = 48,
            Language = LauncherSettings.LanguageTraditionalChinese,
            ExtensionData = new() { ["futureSetting"] = future.RootElement.Clone() },
        };

        var viewModel = new SettingsViewModel(current) { ShowLabels = true };
        var result = viewModel.ToSettings();

        Assert.True(result.ShowLabels);
        Assert.Equal(48, result.IconSize);
        Assert.Equal(LauncherSettings.LanguageTraditionalChinese, result.Language);
        Assert.Equal("kept", result.ExtensionData!["futureSetting"].GetString());
        Assert.NotSame(current.ExtensionData, result.ExtensionData);
        Assert.False(current.ShowLabels); // the original is untouched until the caller applies the result
    }
}
