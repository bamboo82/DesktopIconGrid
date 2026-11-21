using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>
/// Settings ▸ Appearance: the custom theme's background picture (0.15.0 WP5). The picture is stored as soon as it is
/// chosen (a picture no setting names is deleted at the next start); the setting changes at Save, as the others do.
/// </summary>
public sealed partial class SettingsViewModel
{
    private BackgroundImage? _picture;
    private ImageSource? _thumbnail;
    private bool _thumbnailRead;

    /// <summary>Where stored pictures are; tests stand in.</summary>
    internal string PicturesFolder { get; set; } = AppPaths.BackgroundsDirectory;

    private void ReadPicture(LauncherSettings current) => _picture = current.BackgroundImage;

    private void WritePicture(LauncherSettings settings) => settings.BackgroundImage = _picture;

    /// <summary>The picture as chosen now, with its position, zoom and strength; none for the colour alone.</summary>
    public BackgroundImage? Picture
    {
        get => _picture;
        set
        {
            if (_picture == value)
            {
                return;
            }

            var other = !string.Equals(_picture?.File, value?.File, StringComparison.OrdinalIgnoreCase);
            _picture = value;
            if (other)
            {
                _thumbnail = null;
                _thumbnailRead = false;
                OnPropertyChanged(nameof(PictureThumbnail));
                OnPropertyChanged(nameof(PictureThumbnailBrush));
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPicture));
            OnPropertyChanged(nameof(PictureNote));
            OnPropertyChanged(nameof(PictureStrength));
            OnPropertyChanged(nameof(PictureStrengthText));
            OnThemeColoursChanged();
        }
    }

    public bool HasPicture => _picture is not null;

    /// <summary>
    /// What the thumbnail's place says when it shows no thumbnail (0.15.0 WP5b): 無 without a picture; for a picture this PC
    /// lacks, 下載中… while it comes from the sync place and 這台電腦沒有這張圖片 otherwise. Empty with a thumbnail.
    /// </summary>
    public string PictureNote => _picture is not { } picture ? Strings.Settings_PictureNone
        : PictureShown ? string.Empty
        : PictureTransfers.IsFetching(picture.File) ? Strings.Settings_PictureDownloading
        : Strings.Settings_PictureMissingHere;

    /// <summary>A fetch of the picture began or ended (0.15.0 WP5b): the row reads it again.</summary>
    public void RefreshPicture()
    {
        _thumbnail = null;
        _thumbnailRead = false;
        OnPropertyChanged(nameof(PictureThumbnail));
        OnPropertyChanged(nameof(PictureThumbnailBrush));
        OnPropertyChanged(nameof(PictureNote));
        OnThemeColoursChanged();
    }

    /// <summary>Whether this PC has the chosen picture's file: only then is it drawn, and only then does it change the text.</summary>
    private bool PictureShown => _picture is { } picture && File.Exists(Path.Combine(PicturesFolder, picture.File));

    /// <summary>A small copy of the chosen picture for the row; none when there is none or this PC lacks it.</summary>
    public ImageSource? PictureThumbnail
    {
        get
        {
            if (!_thumbnailRead)
            {
                _thumbnailRead = true;
                _thumbnail = ReadThumbnail();
            }

            return _thumbnail;
        }
    }

    /// <summary>The thumbnail as the row's fill, cut to it; none when there is no thumbnail.</summary>
    public Brush? PictureThumbnailBrush
    {
        get
        {
            if (PictureThumbnail is not { } thumbnail)
            {
                return null;
            }

            var brush = new ImageBrush(thumbnail) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>How strongly the picture lies over the colour, 20 to 100 %.</summary>
    public int PictureStrength
    {
        get => _picture?.Strength ?? BackgroundImage.DefaultStrength;
        set
        {
            if (_picture is { } picture)
            {
                Picture = picture with { Strength = Math.Clamp(value, BackgroundImage.MinStrength, BackgroundImage.MaxStrength) };
            }
        }
    }

    public string PictureStrengthText => string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{PictureStrength} %");

    public int MinPictureStrength => BackgroundImage.MinStrength;

    public int MaxPictureStrength => BackgroundImage.MaxStrength;

    /// <summary>A picture just stored (<see cref="BackgroundPictures.Store"/>): it starts centred, at zoom 100 and full strength.</summary>
    public void UsePicture(string name, string average) => Picture = new BackgroundImage { File = name, Average = average };

    public void RemovePicture() => Picture = null;

    /// <summary>The strip of 調整位置… (0.15.0 WP6): the theme's colour and text as chosen now, and the background's opacity.</summary>
    internal Views.PicturePositionWindow.StripLook PictureStripLook()
    {
        var palette = PreviewPalette();
        return new Views.PicturePositionWindow.StripLook(palette.Background, palette.Foreground, LauncherSettings.MaxOpacity - Transparency);
    }

    /// <summary>What the text stands on with the picture shown: its average blended into the colour by its strength.</summary>
    private string? PictureBasis() => PictureShown && _picture is { Average: { } average } picture && IsColorValid
        ? ThemeCatalog.Blend(_backgroundColor, average, picture.Strength)
        : null;

    private ImageSource? ReadThumbnail()
    {
        if (_picture is not { } picture || !BackgroundImage.IsStoredName(picture.File))
        {
            return null;
        }

        var path = Path.Combine(PicturesFolder, picture.File);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var thumbnail = new BitmapImage();
            thumbnail.BeginInit();
            thumbnail.UriSource = new Uri(path);
            thumbnail.DecodePixelWidth = 128;
            thumbnail.CacheOption = BitmapCacheOption.OnLoad;
            thumbnail.EndInit();
            thumbnail.Freeze();
            return thumbnail;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
