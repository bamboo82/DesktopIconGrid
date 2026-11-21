using System.Windows;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.Services;

/// <summary>
/// The text icon window's Caption and Widget modes (0.15.0 WP7b), for the dialog service and for 文字外觀… in a widget's own
/// window, which has only a copy of the item.
/// </summary>
public static class TextLookWindows
{
    /// <summary>
    /// What the widget with this id shows now and how its text is drawn without a look, as its tile on the bar has them;
    /// set by the bar (<see cref="LauncherViewModel"/>). Without it, or for a widget the bar does not have, the window starts
    /// from the widget's name and the number style's text.
    /// </summary>
    public static Func<Guid, (string Sample, TextLookDefaults Defaults)?>? Tiles { get; set; }

    /// <summary>The number style's text without a look, in the bar's font: what a widget the bar does not know starts from.</summary>
    public static TextLookDefaults FallbackDefaults() => new(42, FontWeights.Bold, UiFonts.BarFontFamily());

    /// <summary>A row line's caption and its look; <see langword="null"/> on Cancel.</summary>
    public static CaptionAnswer? Caption(Window? owner, string? caption, TextLook? look, TextLookDefaults defaults)
    {
        var editor = TextIconViewModel.ForCaption(caption, look, defaults);
        var window = new TextIconWindow { DataContext = editor, Owner = owner };
        if (window.ShowDialog() != true)
        {
            return null;
        }

        var (text, saved) = editor.ToCaption();
        return new CaptionAnswer(text, saved);
    }

    /// <summary>A widget's look, with what it shows now; <see langword="null"/> on Cancel.</summary>
    public static LookAnswer? Widget(Window? owner, string name, string sample, TextLook? look, TextLookDefaults defaults)
    {
        var editor = TextIconViewModel.ForWidget(name, sample, look, defaults);
        var window = new TextIconWindow { DataContext = editor, Owner = owner };
        return window.ShowDialog() == true ? new LookAnswer(editor.ToLook()) : null;
    }

    /// <summary>
    /// 文字外觀… in a widget's own window: the widget with <paramref name="id"/> as the bar shows it (<see cref="Tiles"/>),
    /// under the name typed there.
    /// </summary>
    public static LookAnswer? Widget(Window? owner, Guid id, string name, TextLook? look)
    {
        var (sample, defaults) = Tiles?.Invoke(id) ?? (name, FallbackDefaults());
        return Widget(owner, name, sample, look, defaults);
    }
}
