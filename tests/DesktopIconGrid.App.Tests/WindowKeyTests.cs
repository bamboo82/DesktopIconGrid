using System.Windows.Input;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>The bar window's keys as the settings give them, and how Settings changes them.</summary>
public class WindowKeyTests
{
    [Fact]
    public void The_map_follows_the_settings_and_a_turned_off_key_does_nothing()
    {
        var defaults = WindowKeyMap.From(new LauncherSettings());
        Assert.Equal(WindowKeyAction.NewShortcut, defaults.ActionFor(Key.N, ModifierKeys.Control));
        Assert.Equal(WindowKeyAction.OpenSettings, defaults.ActionFor(Key.OemComma, ModifierKeys.Control));
        Assert.Equal(WindowKeyAction.Properties, defaults.ActionFor(Key.F2, ModifierKeys.None));
        Assert.Equal("Ctrl+N", defaults.TextFor(WindowKeyAction.NewShortcut));

        var changed = WindowKeyMap.From(new LauncherSettings { WindowKeys = new() { ["NewShortcut"] = "Ctrl+Shift+N", ["Remove"] = "" } });
        Assert.Null(changed.ActionFor(Key.N, ModifierKeys.Control));
        Assert.Equal(WindowKeyAction.NewShortcut, changed.ActionFor(Key.N, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Null(changed.ActionFor(Key.Delete, ModifierKeys.None));
        Assert.Equal(string.Empty, changed.TextFor(WindowKeyAction.Remove));
    }

    [Theory]
    [InlineData(ModifierKeys.Control, Key.N, true)]
    [InlineData(ModifierKeys.None, Key.F2, true)]
    [InlineData(ModifierKeys.None, Key.Delete, true)]
    [InlineData(ModifierKeys.None, Key.N, false)] // would type into the search
    [InlineData(ModifierKeys.Shift, Key.N, false)]
    [InlineData(ModifierKeys.Control, Key.Escape, false)]
    [InlineData(ModifierKeys.None, Key.Enter, false)]
    [InlineData(ModifierKeys.Alt, Key.Left, false)]
    public void A_window_key_needs_a_modifier_unless_it_types_nothing(ModifierKeys modifiers, Key key, bool allowed)
    {
        Assert.Equal(allowed, new HotkeyGesture(modifiers, key).IsAllowedInWindow);
    }

    [Fact]
    public void Settings_refuses_a_key_another_action_or_a_hotkey_has_and_stores_only_changes()
    {
        var viewModel = new SettingsViewModel(new LauncherSettings { ToggleHotkey = "Ctrl+Alt+Space" });
        var find = viewModel.WindowKeyRows.Single(r => r.Action == WindowKeyAction.Find);
        var remove = viewModel.WindowKeyRows.Single(r => r.Action == WindowKeyAction.Remove);

        Assert.Equal(string.Format(Strings.Settings_WindowKeyInUse, "Ctrl+N", Strings.WindowKey_NewShortcut), viewModel.SetWindowKey(find, "Ctrl+N"));
        Assert.Equal(string.Format(Strings.Settings_WindowKeyInUse, "Ctrl+Alt+Space", Strings.WindowKey_ToggleHotkey), viewModel.SetWindowKey(find, "Ctrl+Alt+Space"));
        Assert.Equal("Ctrl+F", find.Text);

        Assert.Null(viewModel.SetWindowKey(find, "Ctrl+K"));
        Assert.Null(viewModel.SetWindowKey(remove, string.Empty));
        Assert.Equal(new Dictionary<string, string> { ["Find"] = "Ctrl+K", ["Remove"] = "" }, viewModel.ToSettings().WindowKeys);

        Assert.Null(viewModel.SetWindowKey(find, find.DefaultText));
        Assert.Null(viewModel.SetWindowKey(remove, remove.DefaultText));
        Assert.Null(viewModel.ToSettings().WindowKeys);
    }
}
