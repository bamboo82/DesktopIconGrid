using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>Backs the "Text icon" dialog: the characters, font, size, weight and colour drawn instead of an image.</summary>
public sealed class TextIconViewModel : ObservableObject
{
    private string _text;
    private FontChoice? _selectedFont;
    private string _fontSizeText;
    private bool _bold;
    private string _color;

    public TextIconViewModel(TextIcon? current, string suggestedText)
    {
        _text = current?.Text ?? Suggest(suggestedText);
        _selectedFont = TextIconFonts.Find(current?.FontFamily) ?? TextIconFonts.DefaultChoice();
        _fontSizeText = (current?.FontSize ?? TextIcon.DefaultFontSize).ToString(CultureInfo.CurrentCulture);
        _bold = current?.Bold ?? false;
        _color = current?.Color ?? string.Empty;
    }

    public IReadOnlyList<FontChoice> Fonts => TextIconFonts.Installed;

    public IReadOnlyList<double> FontSizes => TextIcon.SuggestedFontSizes;

    public int MaxTextLength => TextIcon.MaxTextLength;

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(PreviewText));
            }
        }
    }

    public FontChoice? SelectedFont
    {
        get => _selectedFont;
        set
        {
            if (SetProperty(ref _selectedFont, value))
            {
                OnPropertyChanged(nameof(PreviewFontFamily));
            }
        }
    }

    public string FontSizeText
    {
        get => _fontSizeText;
        set
        {
            if (SetProperty(ref _fontSizeText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsFontSizeValid));
                OnPropertyChanged(nameof(PreviewFontSize));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool Bold
    {
        get => _bold;
        set
        {
            if (SetProperty(ref _bold, value))
            {
                OnPropertyChanged(nameof(PreviewFontWeight));
            }
        }
    }

    /// <summary><c>#RRGGBB</c>, or empty for the theme's text colour.</summary>
    public string Color
    {
        get => _color;
        set
        {
            if (SetProperty(ref _color, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsColorValid));
                OnPropertyChanged(nameof(PreviewBrush));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool IsColorValid => _color.Length == 0 || LauncherSettings.IsValidColor(_color);

    public bool IsFontSizeValid => TryParseFontSize(out _);

    public bool CanSave => _text.Trim().Length > 0 && IsColorValid && IsFontSizeValid;

    public string PreviewText => _text.Trim().Length == 0 ? "Aa" : _text.Trim();

    public FontFamily PreviewFontFamily => TextIconFonts.Resolve(_selectedFont?.Source);

    public double PreviewFontSize => TryParseFontSize(out var size) ? size : TextIcon.DefaultFontSize;

    public FontWeight PreviewFontWeight => _bold ? FontWeights.Bold : FontWeights.Normal;

    public Brush PreviewBrush => LauncherSettings.IsValidColor(_color)
        ? ThemeManager.ToBrush(_color)
        : Application.Current?.TryFindResource(ThemeManager.ForegroundKey) as Brush ?? Brushes.White;

    public TextIcon? ToTextIcon()
    {
        TryParseFontSize(out var size);
        return new TextIcon(_text, _selectedFont?.Source, size, _bold, _color.Length == 0 ? null : _color).Normalized();
    }

    private bool TryParseFontSize(out double size)
    {
        if (double.TryParse(_fontSizeText, NumberStyles.Float, CultureInfo.CurrentCulture, out size)
            || double.TryParse(_fontSizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out size))
        {
            return size >= TextIcon.MinFontSize && size <= TextIcon.MaxFontSize;
        }

        size = TextIcon.DefaultFontSize;
        return false;
    }

    private static string Suggest(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        // Short names fit as they are; long Latin names shrink to their initials.
        if (trimmed.Length <= 4 || trimmed.Any(c => c > 0x2E7F))
        {
            return trimmed.Length <= TextIcon.MaxTextLength ? trimmed : trimmed[..TextIcon.MaxTextLength];
        }

        var initials = string.Concat(trimmed.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])));
        return initials.Length is >= 2 and <= 4 ? initials : trimmed[..Math.Min(4, trimmed.Length)];
    }
}
