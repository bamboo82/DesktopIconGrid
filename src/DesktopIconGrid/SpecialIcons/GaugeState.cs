using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Infrastructure;

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
    private static readonly SolidColorBrush UnknownBrush = Frozen("#FF9A9A9A");

    private readonly double _warningAt;
    private readonly double _criticalAt;
    private double? _percent;
    private string _detail;

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

    public double? Percent => _percent;

    public string Label => _percent is { } p ? $"{Math.Round(p).ToString(CultureInfo.CurrentCulture)}%" : "…";

    /// <summary>Tooltip line, e.g. "C:\ — 120 GB free of 500 GB".</summary>
    public string Detail => _detail;

    public Brush LevelBrush => _percent switch
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

    public void Detach() => IsDetached = true;

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
