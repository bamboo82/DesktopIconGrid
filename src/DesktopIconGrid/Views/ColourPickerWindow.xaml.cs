using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

/// <summary>
/// The colour picker's dialog (0.15.0 WP4): the custom background's, and the text icon's colour and tile colour. The
/// square and the strip follow the mouse while its button is held, and the arrow keys by one unit (Shift: ten). Enter is
/// OK and Esc is Cancel.
/// </summary>
public partial class ColourPickerWindow : Window
{
    private const double RingSize = 14;
    private const double ThumbWidth = 6;

    public ColourPickerWindow(ColourPickerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        DialogTheme.Apply(this);
        viewModel.PropertyChanged += OnViewModelChanged;
        PlaceMarks();
    }

    public ColourPickerViewModel ViewModel { get; }

    /// <summary>
    /// Shows the picker over <paramref name="owner"/>, opened at <paramref name="current"/>, and returns the colour chosen
    /// (<c>#RRGGBB</c>) or <see langword="null"/> for Cancel. <paramref name="preview"/> gets each new colour while it is
    /// open; after a Cancel the caller puts back what it had.
    /// </summary>
    public static string? Pick(Window? owner, string? current, Action<string>? preview = null, IReadOnlyList<string>? presets = null)
    {
        var viewModel = new ColourPickerViewModel(current, presets);
        if (preview is not null)
        {
            viewModel.ColourChanged += preview;
        }

        var window = new ColourPickerWindow(viewModel) { Owner = owner };
        return window.ShowDialog() == true ? viewModel.Hex : null;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ColourPickerViewModel.MarkerX) or nameof(ColourPickerViewModel.MarkerY) or nameof(ColourPickerViewModel.HueX):
                PlaceMarks();
                break;
            case nameof(ColourPickerViewModel.IsHexTextValid) or nameof(ColourPickerViewModel.IsRedTextValid)
                or nameof(ColourPickerViewModel.IsGreenTextValid) or nameof(ColourPickerViewModel.IsBlueTextValid):
                NotAColour.Visibility = ViewModel.IsHexTextValid && ViewModel.IsRedTextValid && ViewModel.IsGreenTextValid && ViewModel.IsBlueTextValid
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                break;
        }
    }

    /// <summary>The ring centred on the colour's point of the square, the thumb on its hue.</summary>
    private void PlaceMarks()
    {
        Canvas.SetLeft(RingOuter, ViewModel.MarkerX - (RingSize / 2));
        Canvas.SetTop(RingOuter, ViewModel.MarkerY - (RingSize / 2));
        Canvas.SetLeft(RingInner, ViewModel.MarkerX - ((RingSize - 2) / 2));
        Canvas.SetTop(RingInner, ViewModel.MarkerY - ((RingSize - 2) / 2));
        Canvas.SetLeft(HueThumb, ViewModel.HueX - (ThumbWidth / 2));
    }

    private static int Step => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;

    private void Square_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Square.Focus();
        Square.CaptureMouse();
        var point = e.GetPosition(Square);
        ViewModel.SetFromSquare(point.X, point.Y);
        e.Handled = true;
    }

    private void Square_MouseMove(object sender, MouseEventArgs e)
    {
        if (Square.IsMouseCaptured)
        {
            var point = e.GetPosition(Square);
            ViewModel.SetFromSquare(point.X, point.Y);
        }
    }

    private void Square_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Square.ReleaseMouseCapture();

    private void Square_KeyDown(object sender, KeyEventArgs e)
    {
        var (right, up) = e.Key switch
        {
            Key.Left => (-Step, 0),
            Key.Right => (Step, 0),
            Key.Up => (0, Step),
            Key.Down => (0, -Step),
            _ => (0, 0),
        };
        if (right != 0 || up != 0)
        {
            ViewModel.MoveMarker(right, up);
            e.Handled = true;
        }
    }

    private void HueStrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HueStrip.Focus();
        HueStrip.CaptureMouse();
        ViewModel.SetHueAt(e.GetPosition(HueStrip).X);
        e.Handled = true;
    }

    private void HueStrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (HueStrip.IsMouseCaptured)
        {
            ViewModel.SetHueAt(e.GetPosition(HueStrip).X);
        }
    }

    private void HueStrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => HueStrip.ReleaseMouseCapture();

    private void HueStrip_KeyDown(object sender, KeyEventArgs e)
    {
        var right = e.Key switch
        {
            Key.Left or Key.Down => -Step,
            Key.Right or Key.Up => Step,
            _ => 0,
        };
        if (right != 0)
        {
            ViewModel.MoveHue(right);
            e.Handled = true;
        }
    }

    /// <summary>A box left with what is no colour shows the colour again.</summary>
    private void Box_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ViewModel.RefreshBoxes();

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelChanged;
        base.OnClosed(e);
    }
}
