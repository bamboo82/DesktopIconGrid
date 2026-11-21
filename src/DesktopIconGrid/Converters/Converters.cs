using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DesktopIconGrid.Converters;

/// <summary>Visible when the value is non-null, collapsed otherwise.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the value is <see langword="false"/>, collapsed when <see langword="true"/>.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>
/// The text layout for a gauge's text (0.15.0 WP9): its font size in the gauge's box of <c>ConverterParameter</c> units
/// (100), drawn at the icon size; <see cref="Views.TextSharpness.FormattingFor"/> decides.
/// </summary>
public sealed class ScaledTextFormattingConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        var box = parameter is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var units) && units > 0 ? units : 100;
        return values.Length >= 2 && Number(values[0]) is { } fontSize && Number(values[1]) is { } iconSize
            ? Views.TextSharpness.FormattingFor(fontSize * iconSize / box)
            : System.Windows.Media.TextFormattingMode.Display;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static double? Number(object? value) => value switch
    {
        double number when double.IsFinite(number) => number,
        int number => number,
        _ => null,
    };
}
