using System.Globalization;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using Anchor = DesktopIconGrid.Core.Models.PointerAnchor;

namespace DesktopIconGrid.ViewModels;

/// <summary>
/// 指標處視窗的位置 on the Keys tab (0.16.0 WP2, the owner's request): which point of the window at the pointer is put
/// under the pointer, in both its forms. A picture of the window with its nine named points, where a click or a drag puts
/// the point (snapped to a dot within <see cref="AnchorSnap"/> %) and the arrow keys move it; two boxes with the same
/// values in whole percent; and *Reset to default*, which leaves none chosen: the placement before 0.16.0, which the
/// picture shows as the middle of the top edge. Always enabled, whether or not a pointer key is set.
/// </summary>
public sealed partial class SettingsViewModel
{
    /// <summary>How near one of the nine dots, in percent each way, a click or a drag puts the point on it.</summary>
    public const int AnchorSnap = 6;

    /// <summary>
    /// The picture's canvas (inside its 1-unit border, 166 × 102 units): the dots' centres are 3 units in from each side,
    /// so the edge dots, 6 units wide, sit on the border's inside, and 0 to 100 % spans the 160 × 96 units between them.
    /// </summary>
    public const double AnchorInset = 3;

    public const double AnchorSpanX = 160;

    public const double AnchorSpanY = 96;

    /// <summary>Half the pointer mark, a ring 14 units across, drawn centred on the point.</summary>
    private const double AnchorMarkRadius = 7;

    /// <summary>What the picture and the boxes show while none is chosen: the middle of the top edge.</summary>
    public static Anchor DefaultShownAnchor { get; } = new(50, 0);

    private Anchor? _pointerAnchor;
    private string _anchorXText = string.Empty;
    private string _anchorYText = string.Empty;

    private void ReadPointerAnchor(LauncherSettings current)
    {
        _pointerAnchor = current.PointerAnchor?.Normalized();
        ShowAnchorTexts();
    }

    /// <summary>The chosen point, or none: the placement before 0.16.0.</summary>
    public Anchor? PointerAnchor
    {
        get => _pointerAnchor;
        set
        {
            value = value?.Normalized();
            if (value != _pointerAnchor || !IsPointerAnchorValid)
            {
                _pointerAnchor = value;
                ShowAnchorTexts(); // the boxes follow the picture, and a box left wrong is put right
                OnAnchorChanged();
            }
        }
    }

    /// <summary>The point the picture and the boxes show: the chosen one, else the middle of the top edge.</summary>
    public Anchor ShownAnchor => _pointerAnchor ?? DefaultShownAnchor;

    public bool IsPointerAnchorSet => _pointerAnchor is not null;

    /// <summary>水平: the box of the point across the window, in whole percent.</summary>
    public string PointerAnchorXText
    {
        get => _anchorXText;
        set => SetAnchorText(value, across: true);
    }

    /// <summary>垂直: the box of the point down the window, in whole percent.</summary>
    public string PointerAnchorYText
    {
        get => _anchorYText;
        set => SetAnchorText(value, across: false);
    }

    public bool IsPointerAnchorXValid => TryAnchorPercent(_anchorXText, out _);

    public bool IsPointerAnchorYValid => TryAnchorPercent(_anchorYText, out _);

    /// <summary>Both boxes hold a whole number from 0 to 100; otherwise Save waits (the Keys tab has a problem).</summary>
    public bool IsPointerAnchorValid => IsPointerAnchorXValid && IsPointerAnchorYValid;

    /// <summary>The picture's value for UI Automation: 水平 50 %，垂直 0 %.</summary>
    public string PointerAnchorValueText => string.Format(CultureInfo.CurrentCulture, Strings.Settings_PointerAnchorValue, ShownAnchor.X, ShownAnchor.Y);

    /// <summary>預設：上緣中央 while none is chosen; empty once one is.</summary>
    public string PointerAnchorStatus => IsPointerAnchorSet ? string.Empty : Strings.Settings_PointerAnchorDefault;

    /// <summary>Where the pointer mark is drawn on the picture's canvas: its top left corner.</summary>
    public double AnchorMarkLeft => AnchorInset + (AnchorSpanX * ShownAnchor.X / 100) - AnchorMarkRadius;

    public double AnchorMarkTop => AnchorInset + (AnchorSpanY * ShownAnchor.Y / 100) - AnchorMarkRadius;

    /// <summary>
    /// A click or a drag at (<paramref name="x"/>, <paramref name="y"/>) on the picture's canvas: the point under it in
    /// whole percent, held to the window, and on a dot when within <see cref="AnchorSnap"/> % of one.
    /// </summary>
    public void MoveAnchorTo(double x, double y)
    {
        static int Percent(double offset, double span) => (int)Math.Round(Math.Clamp(offset / span * 100, Anchor.Min, Anchor.Max), MidpointRounding.AwayFromZero);
        PointerAnchor = Snapped(new Anchor(Percent(x - AnchorInset, AnchorSpanX), Percent(y - AnchorInset, AnchorSpanY)));
    }

    /// <summary>The dot of the nine (0, 50 or 100 each way) within <see cref="AnchorSnap"/> % of <paramref name="point"/> each way, else the point.</summary>
    public static Anchor Snapped(Anchor point)
    {
        ArgumentNullException.ThrowIfNull(point);
        static int? Near(int value) => new[] { 0, 50, 100 }.Where(dot => Math.Abs(dot - value) <= AnchorSnap).Select(dot => (int?)dot).FirstOrDefault();
        return Near(point.X) is { } x && Near(point.Y) is { } y ? new Anchor(x, y) : point;
    }

    /// <summary>
    /// The arrow keys on the picture: <paramref name="across"/> and <paramref name="down"/> are −1, 0 or 1. By 1 %, or with
    /// <paramref name="dotToDot"/> (Ctrl) to the next dot that way, 50 % from a dot; held to 0–100. From none chosen the
    /// move starts at the middle of the top edge.
    /// </summary>
    public void NudgeAnchor(int across, int down, bool dotToDot)
    {
        static int Step(int value, int direction, bool dot) => direction == 0
            ? value
            : dot
                ? (direction > 0 ? Math.Min(Anchor.Max, ((value / 50) + 1) * 50) : Math.Max(Anchor.Min, (((value + 49) / 50) - 1) * 50))
                : Math.Clamp(value + Math.Sign(direction), Anchor.Min, Anchor.Max);
        var shown = ShownAnchor;
        PointerAnchor = new Anchor(Step(shown.X, across, dotToDot), Step(shown.Y, down, dotToDot));
    }

    /// <summary>Home on the picture: the window's centre.</summary>
    public void CentreAnchor() => PointerAnchor = Anchor.Centre;

    /// <summary>還原預設: none chosen, the placement before 0.16.0.</summary>
    public void ResetPointerAnchor() => PointerAnchor = null;

    private static bool TryAnchorPercent(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value is >= Anchor.Min and <= Anchor.Max;

    /// <summary>
    /// A box typed into: a whole number from 0 to 100 chooses the point with the other box's value (or the one shown, while
    /// the other box is wrong); anything else is shown as wrong and changes nothing.
    /// </summary>
    private void SetAnchorText(string? text, bool across)
    {
        text ??= string.Empty;
        if (text == (across ? _anchorXText : _anchorYText))
        {
            return;
        }

        if (across)
        {
            _anchorXText = text;
        }
        else
        {
            _anchorYText = text;
        }

        OnPropertyChanged(across ? nameof(PointerAnchorXText) : nameof(PointerAnchorYText));
        if (TryAnchorPercent(text, out var value))
        {
            var shown = ShownAnchor;
            var chosen = across ? new Anchor(value, shown.Y) : new Anchor(shown.X, value);
            if (chosen != _pointerAnchor)
            {
                _pointerAnchor = chosen;
                OnAnchorChanged();
                return;
            }
        }

        OnAnchorValidityChanged();
    }

    /// <summary>The boxes show the point shown, as whole numbers.</summary>
    private void ShowAnchorTexts()
    {
        _anchorXText = ShownAnchor.X.ToString(CultureInfo.InvariantCulture);
        _anchorYText = ShownAnchor.Y.ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(PointerAnchorXText));
        OnPropertyChanged(nameof(PointerAnchorYText));
    }

    private void OnAnchorChanged()
    {
        foreach (var name in new[]
        {
            nameof(PointerAnchor), nameof(ShownAnchor), nameof(IsPointerAnchorSet), nameof(PointerAnchorValueText), nameof(PointerAnchorStatus),
            nameof(AnchorMarkLeft), nameof(AnchorMarkTop),
        })
        {
            OnPropertyChanged(name);
        }

        OnAnchorValidityChanged();
    }

    private void OnAnchorValidityChanged()
    {
        OnPropertyChanged(nameof(IsPointerAnchorXValid));
        OnPropertyChanged(nameof(IsPointerAnchorYValid));
        OnPropertyChanged(nameof(IsPointerAnchorValid));
        OnProblemsChanged();
    }
}
