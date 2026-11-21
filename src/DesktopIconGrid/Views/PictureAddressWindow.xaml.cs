using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.Views;

/// <summary>
/// The background picture from a web address (0.15.0 WP5). Nothing is fetched until OK is pressed, and then once, by the
/// download rules the web icon has (<see cref="IconDownloader.FetchAsync"/>), here for https only, also after redirects;
/// the bytes are decoded by Windows (<see cref="BackgroundPictures.Decode"/>). A failure is said in the window, which
/// stays open. The address is not kept, and the picture is never fetched again by itself.
/// </summary>
public partial class PictureAddressWindow : Window
{
    private readonly HttpClient _http;
    private CancellationTokenSource? _fetching;

    public PictureAddressWindow(HttpClient http)
    {
        _http = http;
        InitializeComponent();
        DialogTheme.Apply(this);
    }

    /// <summary>The picture fetched and decoded, after OK.</summary>
    public BitmapSource? Picture { get; private set; }

    /// <summary>Shows the window and returns the picture, or <see langword="null"/> for Cancel.</summary>
    public static BitmapSource? Show(Window? owner, HttpClient http)
    {
        var window = new PictureAddressWindow(http) { Owner = owner };
        return window.ShowDialog() == true ? window.Picture : null;
    }

    /// <summary>The message for a failure, in the interface's language.</summary>
    internal static string Reason(DownloadFailure failure) => failure switch
    {
        DownloadFailure.NotHttps => Strings.Picture_NotHttps,
        DownloadFailure.TooLarge => string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Picture_TooLarge, IconDownloader.MaxBytes / (1024 * 1024)),
        DownloadFailure.NotAPicture => Strings.Picture_NotAPicture,
        _ => Strings.Picture_NoAnswer,
    };

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(AddressBox.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            Say(Strings.Picture_NotHttps);
            return;
        }

        using var fetching = new CancellationTokenSource();
        _fetching = fetching;
        OkButton.IsEnabled = false;
        AddressBox.IsEnabled = false;
        Say(Strings.Picture_Fetching, warning: false);
        try
        {
            var bytes = await IconDownloader.FetchAsync(_http, uri, httpsOnly: true, fetching.Token);
            Picture = BackgroundPictures.Decode(bytes);
            DialogResult = true;
        }
        catch (DownloadFailedException ex)
        {
            AppLog.Default.Info("Theme", $"Background picture from the web: {ex.Message}");
            Say(Reason(ex.Failure));
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Default.Info("Theme", $"Background picture from the web: {ex.Message}");
            Say(Strings.Picture_NotAPicture);
        }
        catch (OperationCanceledException) when (fetching.IsCancellationRequested)
        {
            // Cancel was pressed while it was fetching; the window is closing.
        }
        finally
        {
            _fetching = null;
            if (IsLoaded && DialogResult is null)
            {
                OkButton.IsEnabled = true;
                AddressBox.IsEnabled = true;
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _fetching?.Cancel();

    private void Say(string text, bool warning = true)
    {
        StatusLine.Text = text;
        StatusLine.Visibility = Visibility.Visible;
        if (warning)
        {
            StatusLine.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, DialogTheme.WarningBrushKey);
        }
        else
        {
            StatusLine.ClearValue(System.Windows.Controls.TextBlock.ForegroundProperty);
        }
    }
}
