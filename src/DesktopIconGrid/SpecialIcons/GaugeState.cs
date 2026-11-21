using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.SpecialIcons;

/// <summary>
/// Live value of a gauge icon (0–100 %), plus everything the template needs to draw it in the chosen
/// style: the ring arc, the bar proportions, the level colour and the texts.
/// </summary>
public sealed class GaugeState : ObservableObject
{
    private const double RingRadius = 44;
    private const double Centre = 50;

    private static readonly SolidColorBrush NormalBrush = Frozen("#FF34C759");
    private static readonly SolidColorBrush WarningBrush = Frozen("#FFF5A623");
    private static readonly SolidColorBrush CriticalBrush = Frozen("#FFE5484D");
    private static readonly SolidColorBrush UnknownBrush = Frozen(NeutralColor);

    /// <summary>
    /// The grey of a value with no level (a clock without a timer, a sample not taken yet). On a see-through bar such a
    /// value takes an edge of its own (review 38 T2); the green, orange and red ones read without one.
    /// </summary>
    public const string NeutralColor = "#9A9A9A";

    private readonly double _warningAt;
    private readonly double _criticalAt;
    private double? _percent;
    private string _detail;
    private bool _isVisible = true;
    private string? _labelText;
    private CommandLevel? _level;
    private GaugeHistory? _history;
    private double? _historyMax;
    private GaugeLook? _look;

    /// <summary>How wide the ring's value may be with a size of its own (0.15.0 WP7b): inside the ring, 64 units across.</summary>
    public const double RingTextWidth = 60;

    /// <summary>How wide the bar's value may be with a size of its own: the tile less its sides.</summary>
    public const double BarTextWidth = 96;

    public GaugeState(string caption, GaugeStyle style, double warningAt, double criticalAt, string initialDetail)
    {
        Caption = caption;
        Style = style;
        _warningAt = warningAt;
        _criticalAt = criticalAt;
        _detail = initialDetail;
    }

    public string Caption { get; }

    public GaugeStyle Style { get; }

    public bool IsRing => Style == GaugeStyle.Ring;

    public bool IsBar => Style == GaugeStyle.Bar;

    public bool IsNumber => Style == GaugeStyle.Number;

    /// <summary>Set when the owning icon is removed so the monitor stops sampling it.</summary>
    public bool IsDetached { get; private set; }

    /// <summary>False hides the gauge and shows the icon image instead (e.g. a bucket that is not uploading).</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public double? Percent => _percent;

    /// <summary>Replaces the percentage text (e.g. "REC"); <see langword="null"/> shows the percentage.</summary>
    public string? LabelText
    {
        get => _labelText;
        set
        {
            if (SetProperty(ref _labelText, value))
            {
                OnPropertyChanged(nameof(Label));
                OnPropertyChanged(nameof(LabelFontSize));
                OnPropertyChanged(nameof(LabelMaxWidth));
                OnPropertyChanged(nameof(LookLabelSize));
            }
        }
    }

    public string Label => _labelText ?? (_percent is { } p ? $"{Math.Round(p).ToString(CultureInfo.CurrentCulture)}%" : "…");

    /// <summary>The number style's text size: smaller for a longer text (a command tile's first line), so more of it fits.</summary>
    public double LabelFontSize => Label.Length switch
    {
        <= 4 => 42,
        <= 6 => 32,
        <= 9 => 24,
        _ => 18,
    };

    /// <summary>
    /// How wide the number style's text may be before it is cut with "…", in the tile's 100 units. A value of up to nine
    /// characters may be up to 160 wide and is then made smaller to fit ("100%" is 109 wide at 42, release check N1); a
    /// longer text, already at the smallest size, is cut at 98 rather than shrunk until it cannot be read (N4).
    /// </summary>
    public double LabelMaxWidth => Label.Length <= 9 ? 160 : 98;

    /// <summary>
    /// The colour from something other than the percentage (a command tile's exit code); <see langword="null"/> uses the
    /// thresholds.
    /// </summary>
    public CommandLevel? Level
    {
        get => _level;
        set
        {
            if (SetProperty(ref _level, value))
            {
                OnPropertyChanged(nameof(LevelBrush));
                OnPropertyChanged(nameof(IsNeutral));
                OnPropertyChanged(nameof(IsLightLevel));
                OnPropertyChanged(nameof(IsAlarm));
            }
        }
    }

    /// <summary>
    /// The widget's text look (0.15.0 WP7b), or <see langword="null"/> for the text as before. The tile template's triggers
    /// read it; see <see cref="GaugeLook"/>.
    /// </summary>
    public GaugeLook? Look
    {
        get => _look;
        set
        {
            if (SetProperty(ref _look, value))
            {
                OnPropertyChanged(nameof(LookLabelSize));
            }
        }
    }

    /// <summary>
    /// The value's size with a look that sets one, in the tile's units: the ring's and the bar's made smaller where the
    /// value would be wider than they have room for (<see cref="RingTextWidth"/>, <see cref="BarTextWidth"/>); the number
    /// style's box makes its own smaller.
    /// </summary>
    public double LookLabelSize
    {
        get
        {
            if (_look is not { HasSize: true } look)
            {
                return 0;
            }

            var weight = look.HasBold ? look.Weight : IsRing ? FontWeights.SemiBold : IsNumber ? FontWeights.Bold : FontWeights.Normal;
            return Style switch
            {
                GaugeStyle.Ring => LookFonts.FitSize(Label, look.Size, RingTextWidth, look.Family, weight),
                GaugeStyle.Bar => LookFonts.FitSize(Label, look.Size, BarTextWidth, look.Family, weight),
                _ => look.Size,
            };
        }
    }

    /// <summary>
    /// The value's text as it is drawn without a look (0.15.0 WP7b), what the text icon window's Widget mode starts from:
    /// the ring's at 26 semi-bold, the bar's at 22, the number's at its size for the text shown now, bold; in
    /// <paramref name="family"/>, the bar's font. <paramref name="tileSize"/> is the icon size its preview is drawn at.
    /// </summary>
    public ViewModels.TextLookDefaults LookDefaults(FontFamily family, int tileSize) => Style switch
    {
        GaugeStyle.Ring => new(26, FontWeights.SemiBold, family, tileSize),
        GaugeStyle.Bar => new(22, FontWeights.Normal, family, tileSize),
        _ => new(LabelFontSize, FontWeights.Bold, family, tileSize),
    };

    /// <summary>The value is in the warning or the alarm colour, which win over a look's colour (0.15.0 WP7b).</summary>
    public bool IsAlarm => ReferenceEquals(LevelBrush, WarningBrush) || ReferenceEquals(LevelBrush, CriticalBrush);

    /// <summary>Raised once, when <see cref="Detach"/> is called.</summary>
    public event EventHandler? Detached;

    /// <summary>Tooltip text, e.g. "C:\ — 120 GB free of 500 GB", with a line of the last samples under it when they are kept.</summary>
    public string Detail => _history?.Render(_historyMax) is { Length: > 0 } line ? $"{_detail}\n{line}" : _detail;

    /// <summary>The tooltip text without the line of samples, for a screen reader.</summary>
    public string Summary => _detail;

    /// <summary>
    /// Keeps a sample for the tooltip's sparkline (the last <see cref="GaugeHistory.DefaultCapacity"/>): drawn against
    /// <paramref name="max"/> (100 for a percentage), or against the samples' own peak when it is <see langword="null"/>.
    /// Shown from the next <see cref="Update"/>.
    /// </summary>
    public void Record(double? value, double? max)
    {
        (_history ??= new GaugeHistory()).Add(value);
        _historyMax = max;
    }

    public Brush LevelBrush => _level switch
    {
        CommandLevel.Ok => NormalBrush,
        CommandLevel.Warning => WarningBrush,
        CommandLevel.Failed => CriticalBrush,
        CommandLevel.Unknown => UnknownBrush,
        _ => LevelFromPercent,
    };

    /// <summary>The value is drawn in <see cref="NeutralColor"/>.</summary>
    public bool IsNeutral => ReferenceEquals(LevelBrush, UnknownBrush);

    /// <summary>
    /// The value's level colour is light (green, orange; not red): over a background picture its edge is near-black, else
    /// white (review 41 U2), by the rule the see-through edges follow.
    /// </summary>
    public bool IsLightLevel => LevelBrush is SolidColorBrush { Color: var colour }
        && ThemeCatalog.Luminance($"#{colour.R:X2}{colour.G:X2}{colour.B:X2}") > 0.5;

    private Brush LevelFromPercent => _percent switch
    {
        null => UnknownBrush,
        var p when p >= _criticalAt => CriticalBrush,
        var p when p >= _warningAt => WarningBrush,
        _ => NormalBrush,
    };

    /// <summary>Clockwise arc from the top of a 100×100 box, for the ring style.</summary>
    public Geometry ArcGeometry => BuildArc(_percent ?? 0);

    /// <summary>Proportions for the bar style (star-sized grid columns).</summary>
    public GridLength FillStar => new(Math.Max(_percent ?? 0, 0.01), GridUnitType.Star);

    public GridLength RemainderStar => new(Math.Max(100 - (_percent ?? 0), 0.01), GridUnitType.Star);

    public void Update(double? percent, string detail)
    {
        _percent = percent is { } p && double.IsFinite(p) ? Math.Clamp(p, 0, 100) : null;
        _detail = detail;
        OnAllPropertiesChanged();
    }

    public void Detach()
    {
        if (IsDetached)
        {
            return;
        }

        IsDetached = true;
        Detached?.Invoke(this, EventArgs.Empty);
    }

    private static Geometry BuildArc(double percent)
    {
        Geometry geometry;
        if (percent <= 0.5)
        {
            geometry = Geometry.Empty;
        }
        else if (percent >= 99.5)
        {
            geometry = new EllipseGeometry(new Point(Centre, Centre), RingRadius, RingRadius);
        }
        else
        {
            var angle = 2 * Math.PI * percent / 100;
            var end = new Point(Centre + RingRadius * Math.Sin(angle), Centre - RingRadius * Math.Cos(angle));
            var figure = new PathFigure
            {
                StartPoint = new Point(Centre, Centre - RingRadius),
                IsClosed = false,
                IsFilled = false,
            };
            figure.Segments.Add(new ArcSegment(end, new Size(RingRadius, RingRadius), 0, percent > 50, SweepDirection.Clockwise, isStroked: true));
            var path = new PathGeometry();
            path.Figures.Add(figure);
            geometry = path;
        }

        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
