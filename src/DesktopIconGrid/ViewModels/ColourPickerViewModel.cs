using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;

namespace DesktopIconGrid.ViewModels;

/// <summary>
/// The colour picker (0.15.0 WP4): a saturation and brightness square, a hue strip, the colour code and its red, green
/// and blue as boxes, the colour it opened with and the new one, and the swatches. All of them show one colour; the hue,
/// saturation and value are kept as they were set, so a grey or black taken from the boxes leaves the marks where they
/// were. A box with what is not a colour changes nothing and says so (<see cref="IsHexTextValid"/> and the others).
/// </summary>
public sealed class ColourPickerViewModel : ObservableObject
{
    /// <summary>The square's size and the strip's width, in units: one step of the arrow keys is one unit.</summary>
    public const double SquareWidth = 240;
    public const double SquareHeight = 160;
    public const double StripWidth = 240;

    private double _hue;
    private double _saturation;
    private double _value;
    private Rgb _rgb;
    private string _hexText = string.Empty;
    private string _redText = string.Empty;
    private string _greenText = string.Empty;
    private string _blueText = string.Empty;

    /// <param name="current">The colour the picker opens with, <c>#RRGGBB</c>; mid-grey when it is none.</param>
    /// <param name="presets">The swatches: Settings' unless given.</param>
    public ColourPickerViewModel(string? current, IReadOnlyList<string>? presets = null)
    {
        _rgb = ColourMath.TryParseHex(current, out var rgb) ? rgb : new Rgb(128, 128, 128);
        Current = ColourMath.ToHex(_rgb);
        CurrentBrush = Frozen(_rgb);
        (_hue, _saturation, _value) = ColourMath.ToHsv(_rgb);
        Presets = presets ?? SettingsViewModel.ColorPresets;
        SelectPresetCommand = new RelayCommand<string>(preset =>
        {
            if (ColourMath.TryParseHex(preset, out var picked))
            {
                SetRgb(picked, except: null);
            }
        });
        RefreshBoxes();
    }

    /// <summary>Raised with the new colour each time it changes: Settings' preview follows it while the picker is open.</summary>
    public event Action<string>? ColourChanged;

    /// <summary>The colour the picker opened with.</summary>
    public string Current { get; }

    public Brush CurrentBrush { get; }

    /// <summary>The new colour, <c>#RRGGBB</c>.</summary>
    public string Hex => ColourMath.ToHex(_rgb);

    public Brush NewBrush => Frozen(_rgb);

    /// <summary>The pure hue the square is drawn from (full saturation and brightness).</summary>
    public Brush HueBrush => Frozen(ColourMath.ToRgb(new Hsv(_hue, 1, 1)));

    public double Hue => _hue;

    public double Saturation => _saturation;

    public double Value => _value;

    /// <summary>Where the square's ring is, in its units.</summary>
    public double MarkerX => _saturation * SquareWidth;

    public double MarkerY => (1 - _value) * SquareHeight;

    /// <summary>Where the strip's thumb is, in its units.</summary>
    public double HueX => _hue / 360 * StripWidth;

    public IReadOnlyList<string> Presets { get; }

    public RelayCommand<string> SelectPresetCommand { get; }

    /// <summary>The colour code box: a colour code as typed takes effect at once, anything else does nothing.</summary>
    public string HexText
    {
        get => _hexText;
        set
        {
            _hexText = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsHexTextValid));
            if (ColourMath.TryParseHex(_hexText, out var rgb))
            {
                SetRgb(rgb, except: nameof(HexText));
            }
        }
    }

    public bool IsHexTextValid => ColourMath.TryParseHex(_hexText, out _);

    public string RedText
    {
        get => _redText;
        set => SetChannel(ref _redText, value, nameof(RedText), (rgb, channel) => rgb with { R = channel });
    }

    public string GreenText
    {
        get => _greenText;
        set => SetChannel(ref _greenText, value, nameof(GreenText), (rgb, channel) => rgb with { G = channel });
    }

    public string BlueText
    {
        get => _blueText;
        set => SetChannel(ref _blueText, value, nameof(BlueText), (rgb, channel) => rgb with { B = channel });
    }

    public bool IsRedTextValid => ColourMath.TryParseChannel(_redText, out _);

    public bool IsGreenTextValid => ColourMath.TryParseChannel(_greenText, out _);

    public bool IsBlueTextValid => ColourMath.TryParseChannel(_blueText, out _);

    /// <summary>Saturation and brightness from a point of the square (in its units, held to it).</summary>
    public void SetFromSquare(double x, double y)
    {
        _saturation = Math.Clamp(x / SquareWidth, 0, 1);
        _value = 1 - Math.Clamp(y / SquareHeight, 0, 1);
        SetHsv();
    }

    /// <summary>The hue from a point of the strip (in its units, held to it).</summary>
    public void SetHueAt(double x)
    {
        _hue = Math.Clamp(x / StripWidth, 0, 1) * 360 % 360;
        if (x >= StripWidth)
        {
            _hue = 360 * (StripWidth - 1) / StripWidth; // the strip's right end is red again: stay just short of it
        }

        SetHsv();
    }

    /// <summary>The ring moved by whole units: right is more saturated, up is brighter.</summary>
    public void MoveMarker(int right, int up) => SetFromSquare(MarkerX + right, MarkerY - up);

    /// <summary>The thumb moved by whole units along the strip.</summary>
    public void MoveHue(int right)
    {
        var x = HueX + right;
        _hue = ((x / StripWidth * 360) % 360 + 360) % 360;
        SetHsv();
    }

    /// <summary>The boxes show the colour again: what was typed and is no colour goes (the boxes are left).</summary>
    public void RefreshBoxes()
    {
        _hexText = Hex;
        _redText = Number(_rgb.R);
        _greenText = Number(_rgb.G);
        _blueText = Number(_rgb.B);
        foreach (var name in new[] { nameof(HexText), nameof(RedText), nameof(GreenText), nameof(BlueText), nameof(IsHexTextValid), nameof(IsRedTextValid), nameof(IsGreenTextValid), nameof(IsBlueTextValid) })
        {
            OnPropertyChanged(name);
        }
    }

    private void SetChannel(ref string field, string? value, string name, Func<Rgb, byte, Rgb> with)
    {
        field = value ?? string.Empty;
        OnPropertyChanged(name);
        OnPropertyChanged("Is" + name + "Valid");
        if (ColourMath.TryParseChannel(field, out var channel))
        {
            SetRgb(with(_rgb, channel), except: name);
        }
    }

    /// <summary>The colour from the square or the strip: its red, green and blue follow.</summary>
    private void SetHsv()
    {
        _rgb = ColourMath.ToRgb(new Hsv(_hue, _saturation, _value));
        Changed(except: null);
    }

    /// <summary>
    /// The colour from a box or a swatch: hue, saturation and value follow, but a grey keeps the hue and black keeps the
    /// saturation, which it has none of. The box it came from keeps what was typed.
    /// </summary>
    private void SetRgb(Rgb rgb, string? except)
    {
        _rgb = rgb;
        var hsv = ColourMath.ToHsv(rgb);
        if (hsv.V > 0)
        {
            if (hsv.S > 0)
            {
                _hue = hsv.H;
            }

            _saturation = hsv.S;
        }

        _value = hsv.V;
        Changed(except);
    }

    private void Changed(string? except)
    {
        foreach (var (name, text) in new[] { (nameof(HexText), Hex), (nameof(RedText), Number(_rgb.R)), (nameof(GreenText), Number(_rgb.G)), (nameof(BlueText), Number(_rgb.B)) })
        {
            if (name == except)
            {
                continue;
            }

            switch (name)
            {
                case nameof(HexText):
                    _hexText = text;
                    break;
                case nameof(RedText):
                    _redText = text;
                    break;
                case nameof(GreenText):
                    _greenText = text;
                    break;
                default:
                    _blueText = text;
                    break;
            }

            OnPropertyChanged(name);
            OnPropertyChanged("Is" + name + "Valid");
        }

        foreach (var name in new[] { nameof(Hex), nameof(NewBrush), nameof(HueBrush), nameof(Hue), nameof(Saturation), nameof(Value), nameof(MarkerX), nameof(MarkerY), nameof(HueX) })
        {
            OnPropertyChanged(name);
        }

        ColourChanged?.Invoke(Hex);
    }

    private static string Number(byte channel) => channel.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static SolidColorBrush Frozen(Rgb rgb)
    {
        var brush = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
        brush.Freeze();
        return brush;
    }
}
