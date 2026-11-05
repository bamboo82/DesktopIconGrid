using System.Windows.Input;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

/// <summary>The keys of type-to-find, shared by the bar and the Show-at-pointer window.</summary>
internal static class TypeToFind
{
    /// <summary>
    /// True for a key that types a character and so opens the search box: letters, digits and punctuation, with or
    /// without Shift, and keys an IME or a dead key is composing. Ctrl, Alt and Win combinations are shortcuts, and
    /// Space and Enter stay with the focused icon.
    /// </summary>
    public static bool IsTextKey(Key key, ModifierKeys modifiers) =>
        (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0
        && key is (>= Key.A and <= Key.Z) or (>= Key.D0 and <= Key.D9) or (>= Key.NumPad0 and <= Key.NumPad9)
            or Key.Multiply or Key.Add or Key.Subtract or Key.Decimal or Key.Divide
            or Key.OemSemicolon or Key.OemPlus or Key.OemComma or Key.OemMinus or Key.OemPeriod or Key.OemQuestion
            or Key.OemTilde or Key.OemOpenBrackets or Key.OemPipe or Key.OemCloseBrackets or Key.OemQuotes
            or Key.Oem8 or Key.OemBackslash or Key.ImeProcessed or Key.DeadCharProcessed;

    /// <summary>The search box's own keys: the arrows move the frame, Enter opens the framed match. True when the key was used.</summary>
    public static bool HandleSearchBoxKey(LauncherViewModel viewModel, Key key)
    {
        switch (key)
        {
            case Key.Down when viewModel.IsSearching:
                viewModel.MoveSearchSelection(1);
                return true;
            case Key.Up when viewModel.IsSearching:
                viewModel.MoveSearchSelection(-1);
                return true;
            case Key.Enter:
                viewModel.LaunchSearchSelection(IconGridControl.CurrentModifiers());
                return true;
            default:
                return false;
        }
    }
}
