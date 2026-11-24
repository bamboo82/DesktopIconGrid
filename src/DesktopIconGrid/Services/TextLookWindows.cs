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

    /// <summary>
    /// The name of a widget without a look, as the bar has it (0.16.0 WP4): its name size and weight, in its font; set by the
    /// bar (<see cref="LauncherViewModel"/>). Without it, the bar's name at 100 %.
    /// </summary>
    public static Func<TextLookDefaults>? NameDefaults { get; set; }

    /// <summary>The bar's name without a look at 100 %, in the bar's font: what a widget's name starts from without the bar.</summary>
    public static TextLookDefaults FallbackNameDefaults() => new(11, FontWeights.Normal, UiFonts.BarFontFamily());

    /// <summary>
    /// A widget's looks (0.15.0 WP7b, 0.16.0 WP4): the window opens on the value's part, with what the widget shows now, and
    /// has the name's part beside it; switching keeps both, OK saves both and Cancel neither. <see langword="null"/> on Cancel.
    /// </summary>
    public static LookAnswer? Widget(Window? owner, WidgetLookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parts = new WidgetLookParts(
            TextIconViewModel.ForWidget(request.Name, request.Sample, request.Look, request.Defaults),
            TextIconViewModel.ForWidgetName(request.Name, request.NameLook, request.NameDefaults),
            request.IsClock);
        var window = new TextIconWindow { DataContext = parts.Value, Parts = parts, Owner = owner };
        return window.ShowDialog() == true ? parts.ToAnswer() : null;
    }

    /// <summary>
    /// 文字外觀… in a widget's own window: the widget with <paramref name="id"/> as the bar shows it (<see cref="Tiles"/>),
    /// under the name typed there, with its value's and its name's looks.
    /// </summary>
    public static LookAnswer? Widget(Window? owner, Guid id, string name, TextLook? look, TextLook? nameLook, bool isClock)
    {
        var (sample, defaults) = Tiles?.Invoke(id) ?? (name, FallbackDefaults());
        var nameDefaults = NameDefaults?.Invoke() ?? FallbackNameDefaults();
        return Widget(owner, new WidgetLookRequest(name, sample, look, defaults, nameLook, nameDefaults, isClock));
    }
}
