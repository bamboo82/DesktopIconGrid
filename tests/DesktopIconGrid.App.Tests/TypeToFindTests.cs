using System.Windows;
using System.Windows.Input;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>Which keys open the search box, and how big the Show-at-pointer window is.</summary>
public class TypeToFindTests
{
    [Theory]
    [InlineData(Key.A, ModifierKeys.None, true)]
    [InlineData(Key.Z, ModifierKeys.Shift, true)]
    [InlineData(Key.D7, ModifierKeys.None, true)]
    [InlineData(Key.NumPad3, ModifierKeys.None, true)]
    [InlineData(Key.OemPeriod, ModifierKeys.None, true)]
    [InlineData(Key.ImeProcessed, ModifierKeys.None, true)] // a Chinese IME composing
    [InlineData(Key.A, ModifierKeys.Control, false)]
    [InlineData(Key.F, ModifierKeys.Alt, false)]
    [InlineData(Key.Q, ModifierKeys.Control | ModifierKeys.Alt, false)]
    [InlineData(Key.S, ModifierKeys.Windows, false)]
    [InlineData(Key.Space, ModifierKeys.None, false)] // stays with the focused icon
    [InlineData(Key.Enter, ModifierKeys.None, false)]
    [InlineData(Key.F2, ModifierKeys.None, false)]
    [InlineData(Key.Delete, ModifierKeys.None, false)]
    [InlineData(Key.Down, ModifierKeys.None, false)]
    public void Only_keys_that_type_a_character_open_the_search_box(Key key, ModifierKeys modifiers, bool opens)
    {
        Assert.Equal(opens, TypeToFind.IsTextKey(key, modifiers));
    }

    [Fact]
    public void The_pointer_window_shows_three_to_six_columns_and_up_to_five_rows_then_scrolls()
    {
        var chrome = PointerWindow.SizeFor(3, 0, 0, 0); // border, padding and search box only

        Assert.Equal(new Size(chrome.Width + 3 * 60, chrome.Height + 70), PointerWindow.SizeFor(2, 60, 70, 17));
        Assert.Equal(new Size(chrome.Width + 3 * 60, chrome.Height + 70), PointerWindow.SizeFor(0, 60, 70, 17));
        Assert.Equal(new Size(chrome.Width + 6 * 60, chrome.Height + 2 * 70), PointerWindow.SizeFor(12, 60, 70, 17));
        Assert.Equal(new Size(chrome.Width + 6 * 60, chrome.Height + 5 * 70), PointerWindow.SizeFor(30, 60, 70, 17));
        Assert.Equal(new Size(chrome.Width + 6 * 60 + 17, chrome.Height + 5 * 70), PointerWindow.SizeFor(31, 60, 70, 17));
    }
}
