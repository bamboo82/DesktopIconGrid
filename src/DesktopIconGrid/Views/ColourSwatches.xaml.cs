using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

/// <summary>One swatch: its colour, and whether it is the colour in use.</summary>
public sealed record Swatch(string Color, bool IsSelected);

/// <summary>
/// The row of colour swatches of Settings > Appearance; the Text icon window's tile colours are the same list in the
/// same control (0.12.0 WP3). A click runs <see cref="Command"/> with the swatch's <c>#RRGGBB</c>; the swatch of
/// <see cref="Selected"/>, when it is one of them, is drawn with a ring (release check V3).
/// </summary>
public partial class ColourSwatches : UserControl
{
    public static readonly DependencyProperty ColoursProperty = DependencyProperty.Register(
        nameof(Colours), typeof(IEnumerable<string>), typeof(ColourSwatches), new PropertyMetadata(SettingsViewModel.ColorPresets, Refresh));

    public static readonly DependencyProperty SelectedProperty = DependencyProperty.Register(
        nameof(Selected), typeof(string), typeof(ColourSwatches), new PropertyMetadata(null, Refresh));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(ColourSwatches));

    private static readonly DependencyPropertyKey ItemsKey = DependencyProperty.RegisterReadOnly(
        nameof(Items), typeof(IReadOnlyList<Swatch>), typeof(ColourSwatches), new PropertyMetadata(Array.Empty<Swatch>()));

    public static readonly DependencyProperty ItemsProperty = ItemsKey.DependencyProperty;

    public ColourSwatches()
    {
        InitializeComponent();
        Update();
    }

    /// <summary>The colours offered: those of Settings > Appearance unless set.</summary>
    public IEnumerable<string> Colours
    {
        get => (IEnumerable<string>)GetValue(ColoursProperty);
        set => SetValue(ColoursProperty, value);
    }

    /// <summary>The colour in use, <c>#RRGGBB</c> in any case; its swatch is marked. None when not set.</summary>
    public string? Selected
    {
        get => (string?)GetValue(SelectedProperty);
        set => SetValue(SelectedProperty, value);
    }

    /// <summary>Run with the chosen colour.</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>The swatches drawn.</summary>
    public IReadOnlyList<Swatch> Items => (IReadOnlyList<Swatch>)GetValue(ItemsProperty);

    private static void Refresh(DependencyObject owner, DependencyPropertyChangedEventArgs e) => ((ColourSwatches)owner).Update();

    private void Update()
    {
        var selected = Selected?.Trim();
        SetValue(ItemsKey, (Colours ?? []).Select(color => new Swatch(color, string.Equals(color, selected, StringComparison.OrdinalIgnoreCase))).ToList());
    }
}
