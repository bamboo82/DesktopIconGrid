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

    [Fact]
    public void The_show_at_pointer_hotkey_is_off_by_default_and_must_differ_from_the_show_hide_hotkey()
    {
        var viewModel = new SettingsViewModel(new LauncherSettings { ToggleHotkey = "Ctrl+Alt+Space" });
        Assert.True(viewModel.IsPointerHotkeyValid);
        Assert.Null(viewModel.ToSettings().ShowAtPointerHotkey);

        viewModel.ShowAtPointerHotkey = "Control+Alt+Space"; // the same keys, spelled differently
        Assert.True(viewModel.IsPointerHotkeySame);
        Assert.False(viewModel.CanSave);

        viewModel.ShowAtPointerHotkey = "Ctrl+Alt+F";
        Assert.False(viewModel.IsPointerHotkeySame);
        Assert.True(viewModel.CanSave);
        Assert.Equal("Ctrl+Alt+F", viewModel.ToSettings().ShowAtPointerHotkey);

        viewModel.ToggleHotkey = "Ctrl+Alt+F"; // changing the other one is checked as well
        Assert.False(viewModel.CanSave);

        viewModel.ShowAtPointerHotkey = string.Empty;
        Assert.True(viewModel.CanSave);
        Assert.Null(viewModel.ToSettings().ShowAtPointerHotkey);
    }
}
