using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

/// <summary>
/// The row of colour swatches of Settings > Appearance; the Text icon window's tile colours are the same list in the
/// same control (0.12.0 WP3). A click runs <see cref="Command"/> with the swatch's <c>#RRGGBB</c>.
/// </summary>
public partial class ColourSwatches : UserControl
{
    public static readonly DependencyProperty ColoursProperty = DependencyProperty.Register(
        nameof(Colours), typeof(IEnumerable<string>), typeof(ColourSwatches), new PropertyMetadata(SettingsViewModel.ColorPresets));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(ColourSwatches));

    public ColourSwatches()
    {
        InitializeComponent();
    }

    /// <summary>The colours offered: those of Settings > Appearance unless set.</summary>
    public IEnumerable<string> Colours
    {
        get => (IEnumerable<string>)GetValue(ColoursProperty);
        set => SetValue(ColoursProperty, value);
    }

    /// <summary>Run with the chosen colour.</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
}
