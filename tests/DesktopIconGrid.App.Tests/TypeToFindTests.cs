using System.Windows.Input;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>Which keys open the search box. (The Show-at-pointer window's size is checked on the real window, in <see cref="PointerWindowTests"/>.)</summary>
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
}
