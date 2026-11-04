using System.IO;
using System.Windows.Media;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Services;
using System.Windows;

namespace DesktopIconGrid.ViewModels;

/// <summary>Backs the Shortcut Properties / Group Properties dialog. Writes to the item only on <see cref="Apply"/>.</summary>
public sealed class ItemPropertiesViewModel : ObservableObject
{
    private const int PreviewSize = 32;

    private readonly LauncherItem _target;
    private readonly IIconProvider _icons;
    private string _name;
    private string _location;
    private string _parameters;
    private string? _iconPath;
    private TextIcon? _textIcon;
    private bool _nameIsAutomatic;
    private bool _updatingAutomaticName;
    private ImageSource? _preview;
    private string? _warning;

    public ItemPropertiesViewModel(LauncherItem target, bool isNew, IIconProvider icons)
    {
        _target = target;
        _icons = icons;
        IsGroup = target is GroupItem;
        IsNew = isNew;

        _name = target.Name;
        _location = (target as ShortcutItem)?.Location ?? string.Empty;
        _parameters = (target as ShortcutItem)?.Parameters ?? string.Empty;
        _iconPath = target.IconPath;
        _textIcon = target.TextIcon;
        _nameIsAutomatic = !IsGroup && isNew && (_name.Length == 0 || _name == ShortcutItem.SuggestName(_location));

        UpdatePreview();
        UpdateWarning();
    }

    public bool IsGroup { get; }

    public bool IsShortcut => !IsGroup;

    public bool IsNew { get; }

    public string Title => IsGroup ? "Group Properties" : "Shortcut Properties";

    public string LocationHint => "The location may be a file, a folder or a URL.";

    public string ParametersHint =>
        "Files dropped onto the icon replace {file} (one launch per file) or {files} (all at once); " +
        "without a placeholder they are appended.";

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                if (!_updatingAutomaticName)
                {
                    _nameIsAutomatic = _name.Length == 0;
                }

                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public string Location
    {
        get => _location;
        set
        {
            if (!SetProperty(ref _location, value ?? string.Empty))
            {
                return;
            }

            if (_nameIsAutomatic)
            {
                _updatingAutomaticName = true;
                Name = ShortcutItem.SuggestName(_location);
                _updatingAutomaticName = false;
            }

            OnPropertyChanged(nameof(CanSave));
            UpdatePreview();
            UpdateWarning();
        }
    }

    public string Parameters
    {
        get => _parameters;
        set => SetProperty(ref _parameters, value ?? string.Empty);
    }

    public string? IconPath
    {
        get => _iconPath;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (SetProperty(ref _iconPath, normalized))
            {
                if (normalized is not null)
                {
                    _textIcon = null; // an image replaces a text icon
                }

                NotifyIconChanged();
            }
        }
    }

    /// <summary>Text drawn instead of an image. Setting it clears a custom image.</summary>
    public TextIcon? TextIcon
    {
        get => _textIcon;
        set
        {
            var normalized = value?.Normalized();
            if (!Equals(_textIcon, normalized))
            {
                _textIcon = normalized;
                if (normalized is not null)
                {
                    _iconPath = null;
                }

                NotifyIconChanged();
            }
        }
    }

    /// <summary>True when either an image or a text icon overrides the default.</summary>
    public bool HasCustomIcon => _iconPath is not null || _textIcon is not null;

    public bool HasTextIcon => _iconPath is null && _textIcon is not null;

    public bool ShowsImagePreview => !HasTextIcon;

    public string PreviewText => _textIcon?.Text ?? string.Empty;

    public FontFamily PreviewFontFamily => TextIconFonts.Resolve(_textIcon?.FontFamily);

    public double PreviewFontSize => Math.Min(_textIcon?.FontSize ?? TextIcon.DefaultFontSize, 28);

    public FontWeight PreviewFontWeight => _textIcon?.Bold == true ? FontWeights.Bold : FontWeights.Normal;

    public Brush PreviewTextBrush => _textIcon?.Color is { } color
        ? ThemeManager.ToBrush(color)
        : Application.Current?.TryFindResource(ThemeManager.ForegroundKey) as Brush ?? Brushes.White;

    public ImageSource? Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    /// <summary>Non-blocking notice, e.g. a location that does not exist yet.</summary>
    public string? Warning
    {
        get => _warning;
        private set
        {
            if (SetProperty(ref _warning, value))
            {
                OnPropertyChanged(nameof(HasWarning));
            }
        }
    }

    public bool HasWarning => _warning is not null;

    public bool CanSave => _name.Trim().Length > 0 && (IsGroup || _location.Trim().Length > 0);

    public void UseDefaultIcon()
    {
        _iconPath = null;
        _textIcon = null;
        NotifyIconChanged();
    }

    private void NotifyIconChanged()
    {
        OnPropertyChanged(nameof(IconPath));
        OnPropertyChanged(nameof(TextIcon));
        OnPropertyChanged(nameof(HasCustomIcon));
        OnPropertyChanged(nameof(HasTextIcon));
        OnPropertyChanged(nameof(ShowsImagePreview));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(PreviewFontFamily));
        OnPropertyChanged(nameof(PreviewFontSize));
        OnPropertyChanged(nameof(PreviewFontWeight));
        OnPropertyChanged(nameof(PreviewTextBrush));
        UpdatePreview();
        UpdateWarning();
    }

    /// <summary>Copies the edited values into the item. Returns false when the form is not valid.</summary>
    public bool Apply()
    {
        if (!CanSave)
        {
            return false;
        }

        _target.Name = _name.Trim();
        _target.IconPath = _iconPath;
        _target.TextIcon = _textIcon;
        if (_target is ShortcutItem shortcut)
        {
            shortcut.Location = _location.Trim();
            shortcut.Parameters = string.IsNullOrWhiteSpace(_parameters) ? null : _parameters.Trim();
        }

        return true;
    }

    private void UpdatePreview()
    {
        ImageSource? image = null;
        if (_iconPath is not null && _icons.CanResolve(_iconPath))
        {
            image = _icons.GetIcon(_iconPath, PreviewSize * 2);
        }

        if (image is null)
        {
            if (IsGroup)
            {
                image = _icons.GetFolderIcon(PreviewSize * 2);
            }
            else if (_icons.CanResolve(_location))
            {
                image = _icons.GetIcon(_location, PreviewSize * 2);
            }
        }

        Preview = image;
    }

    private void UpdateWarning()
    {
        var location = Environment.ExpandEnvironmentVariables(_location.Trim());
        if (IsShortcut
            && location.Length > 0
            && Path.IsPathRooted(location)
            && !LaunchPlanner.IsUri(location)
            && !File.Exists(location)
            && !Directory.Exists(location))
        {
            Warning = "The location was not found. You can still save it (for example for a drive that is not connected right now).";
            return;
        }

        if (_iconPath is not null && !_icons.CanResolve(_iconPath))
        {
            Warning = "The icon file was not found; the default icon will be used until it is.";
            return;
        }

        Warning = null;
    }
}
