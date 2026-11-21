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

    [Fact]
    public void The_key_held_at_the_pointer_is_off_by_default_and_differs_from_the_others()
    {
        // 0.15.0 WP2: a third row on the Keys tab; four system-wide hotkeys now, none two alike.
        var viewModel = new SettingsViewModel(new LauncherSettings { ToggleHotkey = "Ctrl+Alt+Space", ShowAtPointerHotkey = "Ctrl+Alt+F" });
        Assert.True(viewModel.IsHoldHotkeyValid);
        Assert.Null(viewModel.ToSettings().HoldAtPointerHotkey);

        viewModel.HoldAtPointerHotkey = "Control+Alt+Space"; // the show/hide hotkey, spelled differently
        Assert.True(viewModel.IsHoldHotkeySame);
        Assert.StartsWith("\u26A0 ", viewModel.KeysTab);
        Assert.False(viewModel.CanSave);

        viewModel.HoldAtPointerHotkey = "Ctrl+Alt+F"; // the show-at-pointer hotkey
        Assert.True(viewModel.IsHoldHotkeySame);

        viewModel.HoldAtPointerHotkey = "Ctrl+Alt+G";
        Assert.False(viewModel.IsHoldHotkeySame);
        Assert.True(viewModel.CanSave);
        Assert.Equal("Ctrl+Alt+G", viewModel.ToSettings().HoldAtPointerHotkey);

        viewModel.VoiceHotkey = "Control+Alt+G"; // the voice hotkey is checked against it too
        Assert.True(viewModel.IsVoiceHotkeySame);
        Assert.False(viewModel.CanSave);
        viewModel.VoiceHotkey = string.Empty;

        // A window key may not take it, and it may not take a window key's.
        var row = viewModel.WindowKeyRows[0];
        Assert.Equal(
            string.Format(DesktopIconGrid.Resources.Strings.Settings_WindowKeyInUse, "Ctrl+Alt+G", DesktopIconGrid.Resources.Strings.WindowKey_HoldHotkey),
            viewModel.SetWindowKey(row, "Ctrl+Alt+G"));
        viewModel.HoldAtPointerHotkey = row.Text;
        Assert.True(viewModel.HasHoldHotkeyClash);
        Assert.False(viewModel.IsHoldHotkeyValid);

        viewModel.HoldAtPointerHotkey = string.Empty;
        Assert.True(viewModel.CanSave);
        Assert.Null(viewModel.ToSettings().HoldAtPointerHotkey);
    }
    [Fact]
    public void A_tab_with_a_value_that_cannot_be_saved_is_marked_and_named_beside_Save()
    {
        var viewModel = new SettingsViewModel(new LauncherSettings { ToggleHotkey = "Ctrl+Alt+Space" });
        Assert.Equal(string.Empty, viewModel.ProblemText);
        Assert.Equal(DesktopIconGrid.Resources.Strings.Settings_TabAppearance, viewModel.AppearanceTab);

        viewModel.SelectedTheme = viewModel.Themes.First(theme => theme.IsCustom);
        viewModel.BackgroundColor = "no colour";
        Assert.Equal("\u26A0 " + DesktopIconGrid.Resources.Strings.Settings_TabAppearance, viewModel.AppearanceTab);
        Assert.Equal(string.Format(DesktopIconGrid.Resources.Strings.Settings_FixOnTab, DesktopIconGrid.Resources.Strings.Settings_TabAppearance), viewModel.ProblemText);
        Assert.False(viewModel.CanSave);

        viewModel.SelectedTheme = viewModel.Themes.First(theme => !theme.IsCustom); // the colour no longer matters
        Assert.Equal(string.Empty, viewModel.ProblemText);
        Assert.True(viewModel.CanSave);

        viewModel.ShowAtPointerHotkey = "Control+Alt+Space"; // the show/hide hotkey, spelled differently
        Assert.StartsWith("\u26A0 ", viewModel.KeysTab);
        Assert.Equal(string.Format(DesktopIconGrid.Resources.Strings.Settings_FixOnTab, DesktopIconGrid.Resources.Strings.Settings_TabKeys), viewModel.ProblemText);

        viewModel.ShowAtPointerHotkey = string.Empty;
        viewModel.VoiceHotkey = "Ctrl+Alt+Space";
        Assert.Equal(DesktopIconGrid.Resources.Strings.Settings_TabKeys, viewModel.KeysTab);
        Assert.StartsWith("\u26A0 ", viewModel.VoiceTab);
        Assert.Equal(string.Format(DesktopIconGrid.Resources.Strings.Settings_FixOnTab, DesktopIconGrid.Resources.Strings.Settings_TabVoice), viewModel.ProblemText);
        Assert.False(viewModel.CanSave);
        Assert.Equal(DesktopIconGrid.Resources.Strings.Settings_TabWindow, viewModel.WindowTab);
        Assert.Equal(DesktopIconGrid.Resources.Strings.Settings_TabGeneral, viewModel.GeneralTab);
    }
}
