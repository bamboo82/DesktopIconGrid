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

    [Fact]
    public void An_unreadable_key_is_the_default_a_shared_key_is_the_first_actions_and_Shift_Delete_removes()
    {
        // Review 21's minor list.
        var map = WindowKeyMap.From(new LauncherSettings { WindowKeys = new() { ["NewShortcut"] = "Ctrl+Nonsense", ["NewGroup"] = "Ctrl+F" } });

        Assert.Equal(WindowKeyAction.NewShortcut, map.ActionFor(Key.N, ModifierKeys.Control)); // not turned off
        Assert.Equal("Ctrl+N", map.TextFor(WindowKeyAction.NewShortcut));
        Assert.Equal(WindowKeyAction.NewGroup, map.ActionFor(Key.F, ModifierKeys.Control)); // NewGroup comes before Find
        Assert.Equal("Ctrl+F", map.TextFor(WindowKeyAction.NewGroup));
        Assert.Equal(string.Empty, map.TextFor(WindowKeyAction.Find)); // one menu shows the key, not two
        Assert.Equal(WindowKeyAction.Remove, map.ActionFor(Key.Delete, ModifierKeys.Shift));

        var moved = WindowKeyMap.From(new LauncherSettings { WindowKeys = new() { ["Remove"] = "Ctrl+D" } });
        Assert.Null(moved.ActionFor(Key.Delete, ModifierKeys.Shift)); // only while Delete is the key
    }

    [Theory]
    [InlineData(Key.N, ModifierKeys.Control, true)]
    [InlineData(Key.OemComma, ModifierKeys.Control, true)]
    [InlineData(Key.G, ModifierKeys.Alt, true)]
    [InlineData(Key.Z, ModifierKeys.Control, false)] // the box's own undo
    [InlineData(Key.V, ModifierKeys.Control, false)]
    [InlineData(Key.Left, ModifierKeys.Control, false)]
    [InlineData(Key.N, ModifierKeys.None, false)]
    [InlineData(Key.N, ModifierKeys.Shift, false)]
    [InlineData(Key.Q, ModifierKeys.Control | ModifierKeys.Alt, false)] // AltGr types a character
    public void In_a_text_box_only_keys_the_box_does_not_use_are_the_bars(Key key, ModifierKeys modifiers, bool bars)
    {
        Assert.Equal(bars, WindowKeyMap.LeavesTextBoxAlone(key, modifiers));
    }

    [Fact]
    public void A_failing_pipe_is_tried_again_with_longer_pauses()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), CommandPipe.RetryDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(4), CommandPipe.RetryDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(60), CommandPipe.RetryDelay(6));
        Assert.Equal(TimeSpan.FromSeconds(60), CommandPipe.RetryDelay(100000));
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

    [Fact]
    public void A_system_wide_hotkey_may_not_take_a_key_of_the_bar_and_unknown_overrides_survive_a_save()
    {
        var original = new LauncherSettings { WindowKeys = new() { ["Find"] = "Ctrl+K", ["OpenWorkspace"] = "Ctrl+W" } };
        var viewModel = new SettingsViewModel(original);

        viewModel.ToggleHotkey = "control+k"; // the find key, spelled another way
        Assert.False(viewModel.IsHotkeyValid);
        Assert.False(viewModel.CanSave);
        Assert.NotNull(viewModel.WindowKeyUsing("Ctrl+K"));
        viewModel.ToggleHotkey = "Ctrl+Alt+K";
        Assert.True(viewModel.CanSave);

        var saved = viewModel.ToSettings();

        Assert.Equal("Ctrl+W", saved.WindowKeys!["OpenWorkspace"]); // a later version's action: kept
        Assert.Equal("Ctrl+K", saved.WindowKeys["Find"]);
    }
}
