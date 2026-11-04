using System.IO;
using System.Windows.Media;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Services;

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
        _nameIsAutomatic = !IsGroup && isNew && (_name.Length == 0 || _name == ShortcutItem.SuggestName(_location));

        UpdatePreview();
        UpdateWarning();
    }

    public bool IsGroup { get; }

    public bool IsShortcut => !IsGroup;

    public bool IsNew { get; }

    public string Title => IsGroup ? "Group Properties" : "Shortcut Properties";

    public string LocationHint => "The location may be a file, a folder or a URL.";

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
                OnPropertyChanged(nameof(HasCustomIcon));
                UpdatePreview();
                UpdateWarning();
            }
        }
    }

    public bool HasCustomIcon => _iconPath is not null;

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

    public void UseDefaultIcon() => IconPath = null;

    /// <summary>Copies the edited values into the item. Returns false when the form is not valid.</summary>
    public bool Apply()
    {
        if (!CanSave)
        {
            return false;
        }

        _target.Name = _name.Trim();
        _target.IconPath = _iconPath;
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
