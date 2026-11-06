using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// Both hotkeys applied together, against a stand-in for Windows that refuses a combination any hotkey holds, as
/// <c>RegisterHotKey</c> does: a swap in one save, setting a hotkey back after a refusal, a refusal shown once, and
/// what each hotkey holds afterwards.
/// </summary>
public sealed class HotkeySetTests
{
    /// <summary>Windows' rule: one holder per combination, whoever asks; MOD_NOREPEAT does not make it another one.</summary>
    private sealed class FakeWindows : IHotkeyRegistrar
    {
        private const uint NoRepeat = 0x4000;
        private const int AnotherProgram = -1;
        private const int AlreadyRegistered = 1409;
        private readonly Dictionary<(uint Modifiers, uint Key), int> _held = [];

        public bool Register(int id, uint modifiers, uint virtualKey, out int errorCode)
        {
            var combination = (modifiers & ~NoRepeat, virtualKey);
            if (_held.ContainsKey(combination))
            {
                errorCode = AlreadyRegistered;
                return false;
            }

            _held[combination] = id;
            errorCode = 0;
            return true;
        }

        public void Unregister(int id)
        {
            foreach (var combination in _held.Where(h => h.Value == id).Select(h => h.Key).ToList())
            {
                _held.Remove(combination);
            }
        }

        public void TakenByAnotherProgram(string text) => _held[Combination(text)] = AnotherProgram;

        public void ReleasedByAnotherProgram(string text) => _held.Remove(Combination(text));

        /// <summary>The id that holds <paramref name="text"/>, or null.</summary>
        public int? HolderOf(string text) => _held.TryGetValue(Combination(text), out var id) ? id : null;

        private static (uint, uint) Combination(string text)
        {
            Assert.True(HotkeyGesture.TryParse(text, out var gesture));
            return (gesture.NativeModifiers, gesture.VirtualKey);
        }
    }

    private readonly FakeWindows _windows = new();

    private HotkeySet Create() => new(
        (new GlobalHotkey(_windows), "Show/hide hotkey"),
        (new GlobalHotkey(_windows, GlobalHotkey.PointerIds), "Show-at-pointer hotkey"));

    private bool HeldByShowHide(string text) => _windows.HolderOf(text) is GlobalHotkey.ShowHideIds or GlobalHotkey.ShowHideIds + 1;

    private bool HeldByPointer(string text) => _windows.HolderOf(text) is GlobalHotkey.PointerIds or GlobalHotkey.PointerIds + 1;

    private static void NothingShown(IReadOnlyList<string?> refusals) => Assert.All(refusals, refusal => Assert.Null(refusal));

    [Fact]
    public void Swapping_the_two_in_one_save_swaps_them()
    {
        var hotkeys = Create();
        NothingShown(hotkeys.Apply(["Ctrl+Alt+A", "Ctrl+Alt+B"]));

        NothingShown(hotkeys.Apply(["Ctrl+Alt+B", "Ctrl+Alt+A"])); // 0.7.0: refused for both, about the app's own keys

        Assert.True(HeldByShowHide("Ctrl+Alt+B"));
        Assert.True(HeldByPointer("Ctrl+Alt+A"));
    }

    [Fact]
    public void One_taking_the_others_combination_while_that_one_moves_on_works_both_ways()
    {
        var hotkeys = Create();
        NothingShown(hotkeys.Apply(["Ctrl+Alt+A", "Ctrl+Alt+B"]));

        NothingShown(hotkeys.Apply(["Ctrl+Alt+B", "Ctrl+Alt+C"]));

        Assert.True(HeldByShowHide("Ctrl+Alt+B"));
        Assert.True(HeldByPointer("Ctrl+Alt+C"));
        Assert.Null(_windows.HolderOf("Ctrl+Alt+A"));

        NothingShown(hotkeys.Apply(["Ctrl+Alt+D", "Ctrl+Alt+B"]));

        Assert.True(HeldByShowHide("Ctrl+Alt+D"));
        Assert.True(HeldByPointer("Ctrl+Alt+B"));
    }

    [Fact]
    public void A_hotkey_set_back_after_a_refusal_keeps_working_without_a_message()
    {
        var hotkeys = Create();
        _windows.TakenByAnotherProgram("Ctrl+Alt+C");
        NothingShown(hotkeys.Apply(["Ctrl+Alt+A", null]));

        var refusals = hotkeys.Apply(["Ctrl+Alt+C", null]);

        Assert.Contains("Ctrl+Alt+C", refusals[0]);
        Assert.Contains(Strings.Hotkey_PreviousStays, refusals[0]);
        Assert.True(HeldByShowHide("Ctrl+Alt+A"));
        Assert.Equal("Ctrl+Alt+A", hotkeys.Held[0]?.ToString());

        NothingShown(hotkeys.Apply(["Ctrl+Alt+A", null])); // 0.7.0: "already used by another program", about itself

        Assert.True(HeldByShowHide("Ctrl+Alt+A"));
    }

    [Fact]
    public void A_refusal_is_shown_once_and_tried_again_on_later_saves()
    {
        var hotkeys = Create();
        _windows.TakenByAnotherProgram("Ctrl+Alt+C");
        Assert.NotNull(hotkeys.Apply(["Ctrl+Alt+C", null])[0]);

        NothingShown(hotkeys.Apply(["Ctrl+Alt+C", null])); // another save with the same setting: logged only

        _windows.ReleasedByAnotherProgram("Ctrl+Alt+C");
        NothingShown(hotkeys.Apply(["Ctrl+Alt+C", null]));

        Assert.True(HeldByShowHide("Ctrl+Alt+C"));
    }

    [Fact]
    public void A_refusal_at_start_up_says_there_is_no_hotkey()
    {
        var hotkeys = Create();
        _windows.TakenByAnotherProgram("Ctrl+Alt+X");

        var refusals = hotkeys.Apply(["Ctrl+Alt+X", null]);

        Assert.Contains(Strings.Hotkey_NoneActive, refusals[0]);
        Assert.DoesNotContain(Strings.Hotkey_PreviousStays, refusals[0]);
        Assert.Null(hotkeys.Held[0]);
    }

    [Fact]
    public void A_hotkey_that_gives_its_combination_away_and_is_refused_a_new_one_says_it_has_none()
    {
        var hotkeys = Create();
        _windows.TakenByAnotherProgram("Ctrl+Alt+X");
        NothingShown(hotkeys.Apply(["Ctrl+Alt+A", "Ctrl+Alt+B"]));

        var refusals = hotkeys.Apply(["Ctrl+Alt+X", "Ctrl+Alt+A"]);

        Assert.Contains(Strings.Hotkey_NoneActive, refusals[0]);
        Assert.Null(refusals[1]);
        Assert.Null(hotkeys.Held[0]);
        Assert.True(HeldByPointer("Ctrl+Alt+A"));
        Assert.Null(_windows.HolderOf("Ctrl+Alt+B"));
    }

    [Fact]
    public void Another_spelling_of_the_combination_held_changes_nothing()
    {
        var hotkeys = Create();
        NothingShown(hotkeys.Apply(["Ctrl+Alt+Space", null]));
        var holder = _windows.HolderOf("Ctrl+Alt+Space");

        NothingShown(hotkeys.Apply(["control+alt+space", null]));

        Assert.Equal(holder, _windows.HolderOf("Ctrl+Alt+Space"));
    }

    [Fact]
    public void The_same_combination_for_both_goes_to_show_hide()
    {
        var hotkeys = Create();

        var refusals = hotkeys.Apply(["Ctrl+Alt+A", "Ctrl+Alt+A"]);

        Assert.Null(refusals[0]);
        Assert.Equal(Strings.Settings_PointerHotkeySame, refusals[1]);
        Assert.True(HeldByShowHide("Ctrl+Alt+A"));
        Assert.Null(hotkeys.Held[1]);
    }

    [Fact]
    public void An_empty_setting_lets_the_combination_go()
    {
        var hotkeys = Create();
        NothingShown(hotkeys.Apply(["Ctrl+Alt+A", "Ctrl+Alt+B"]));

        NothingShown(hotkeys.Apply([null, string.Empty]));

        Assert.Null(_windows.HolderOf("Ctrl+Alt+A"));
        Assert.Null(_windows.HolderOf("Ctrl+Alt+B"));
        Assert.All(hotkeys.Held, held => Assert.Null(held));
    }
}
