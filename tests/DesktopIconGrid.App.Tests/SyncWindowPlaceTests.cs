using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconGrid.Core.Models;
using GroupItem = DesktopIconGrid.Core.Models.GroupItem;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.App.Tests;

/// <summary>The sync window in each state of the place of copies (0.16.1 WP2), and what each state needs.</summary>
internal static class SyncWindowStates
{
    public const string Google = "Google Drive";
    public const string Folder = "a folder, nothing of Google's";
    public const string FolderWithAccount = "a folder, an account connected";
    public const string FolderWithBucket = "a folder, a bucket icon in a group";

    public static readonly string[] All = [Google, Folder, FolderWithAccount, FolderWithBucket];

    public sealed record Dialog(SyncViewModel ViewModel, SyncSettings Settings, RecordingDialogs Dialogs);

    /// <summary>The dialog in <paramref name="state"/>; made on the WPF thread, whose timers the coordinator takes.</summary>
    public static Dialog Make(string state, string folder, Func<string, Task>? openBrowser = null) =>
        Make(state == Google ? null : folder, account: state == FolderWithAccount, bucket: state == FolderWithBucket, openBrowser);

    public static Dialog Make(string? folder, bool account, bool bucket, Func<string, Task>? openBrowser = null)
    {
        var settings = new SyncSettings
        {
            DeviceName = "PC-1",
            SyncFolder = folder is null ? null : Path.GetFullPath(folder),
            ClientId = "client.apps.googleusercontent.com",
            ClientSecret = "secret",
            AccountEmail = account ? "someone@example.com" : null,
        };
        var store = new InMemoryTokenStore
        {
            Token = account ? new OAuthToken("access", "refresh", DateTimeOffset.UtcNow.AddHours(1), GoogleAuthorization.Scopes[0]) : null,
        };
        var auth = new GoogleAuthorization(new HttpClient(), store, () => settings.Credentials, openBrowser ?? (_ => throw new GoogleAuthException("No browser in tests.")));
        var service = new SyncService(auth, new EmptyDrive(), settings, () => { });
        var layout = new LauncherLayout();
        layout.Items.Add(new ShortcutItem { Name = "Editor", Location = @"C:\Tools\editor.exe" });
        if (bucket)
        {
            layout.Items.Add(new GroupItem { Name = "Inbox", Items = [new SpecialIconItem { Kind = SpecialIconKinds.GoogleDriveBucket, Name = "Drop" }] });
        }

        var dialogs = new RecordingDialogs { ConfirmAnswer = true };
        return new Dialog(new SyncViewModel(new SyncCoordinator(service, new Host(layout), (_, _) => true), dialogs), settings, dialogs);
    }

    /// <summary>
    /// Lets the window take its last change and, unless <paramref name="until"/> says what to wait for, the dialog's
    /// operation end (a folder is listed on a worker).
    /// </summary>
    public static void Settle(Window window, Func<bool>? until = null)
    {
        until ??= () => window.DataContext is not SyncViewModel { IsBusy: true };
        for (var i = 0; i < 300; i++)
        {
            window.UpdateLayout();
            var frame = new DispatcherFrame();
            window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            window.UpdateLayout();
            if (until())
            {
                return;
            }

            Thread.Sleep(10);
        }

        Assert.Fail("The sync window did not settle.");
    }

    /// <summary>A Drive with no copies and one account.</summary>
    private sealed class EmptyDrive : IDriveClient
    {
        public Task<DriveUser> GetUserAsync(CancellationToken cancellationToken) => Task.FromResult(new DriveUser("someone@example.com", "Someone"));

        public Task<IReadOnlyList<DriveFile>> ListFilesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DriveFile>>([]);

        public Task<DriveFile> UploadJsonAsync(string fileName, string json, IReadOnlyDictionary<string, string> appProperties, string? existingFileId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<string> DownloadTextAsync(string fileId, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task DeleteAsync(string fileId, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class Host(LauncherLayout layout) : ISyncHost
    {
        public event EventHandler? LayoutChanged
        {
            add { }
            remove { }
        }

        public LauncherLayout ExportLayout() => layout;

        public void ReplaceLayout(LauncherLayout replacement)
        {
        }

        public void PictureArrived(string storedName)
        {
        }
    }
}

/// <summary>
/// 0.16.1 WP2: 跨電腦同步 shows only what belongs to the place chosen for the copies. With Google Drive the account's group
/// is under the choice and the folder's hint is gone; with a folder the account is a closed line at the end, there only
/// while an account, a sign-in or a bucket icon needs it.
/// </summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class SyncWindowPlaceTests(WpfThread wpf) : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("dig-sync-place-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(false, false, false, false, SyncAccountPlace.Hidden)]
    [InlineData(false, true, false, false, SyncAccountPlace.Line)]
    [InlineData(false, false, true, false, SyncAccountPlace.Line)]
    [InlineData(false, false, false, true, SyncAccountPlace.Line)]
    [InlineData(true, false, false, false, SyncAccountPlace.Group)]
    [InlineData(true, true, true, true, SyncAccountPlace.Group)]
    public void The_account_is_under_the_choice_with_Google_and_with_a_folder_a_line_only_when_needed(
        bool usesGoogle, bool connected, bool connecting, bool bucketIcon, SyncAccountPlace place) =>
        Assert.Equal(place, SyncViewModel.PlaceOfAccount(usesGoogle, connected, connecting, bucketIcon));

    [Theory]
    [InlineData(SyncWindowStates.Google)]
    [InlineData(SyncWindowStates.Folder)]
    [InlineData(SyncWindowStates.FolderWithAccount)]
    [InlineData(SyncWindowStates.FolderWithBucket)]
    public void Each_place_shows_only_what_belongs_to_it(string state)
    {
        wpf.Invoke(() =>
        {
            var dialog = SyncWindowStates.Make(state, _folder);
            var window = new SyncWindow { DataContext = dialog.ViewModel };
            try
            {
                window.Show();
                SyncWindowStates.Settle(window);
                var google = state == SyncWindowStates.Google;
                var line = state is SyncWindowStates.FolderWithAccount or SyncWindowStates.FolderWithBucket;

                Assert.True(window.GoogleChoice.IsVisible && window.FolderChoice.IsVisible, $"{state}: a choice is missing");
                Assert.Equal(google, window.GoogleChoice.IsChecked);
                Assert.Equal(!google, window.FolderChoice.IsChecked);
                Assert.Equal(Strings.Sync_UseGoogle, window.GoogleChoice.Content);
                Assert.Equal(!google, window.FolderRow.IsVisible);
                Assert.Equal(!google, window.FolderHint.IsVisible);

                Assert.Equal(google, window.AccountGroup.IsVisible);
                Assert.Equal(line, window.AccountLine.IsVisible);
                Assert.False(window.AccountLine.IsExpanded, $"{state}: the line opened by itself");
                Assert.Same(line ? window.AccountLine : window.AccountGroup, window.AccountPanel.Parent); // hidden: where it was
                Assert.Equal(google, window.AccountPanel.IsVisible); // in the closed line it is out of the tab order and of UI Automation

                // The choice and the account's group first, the line last of the sections; 此裝置 and 副本 stay.
                var sections = ((Panel)window.StorageGroup.Parent).Children.OfType<HeaderedContentControl>().ToList();
                Assert.Same(window.StorageGroup, sections[0]);
                Assert.Same(window.AccountGroup, sections[1]);
                Assert.Same(window.AccountLine, sections[^1]);
                Assert.All(sections.Where(section => section is GroupBox && !ReferenceEquals(section, window.AccountGroup)), section => Assert.True(section.IsVisible, $"{state}: {section.Header} is gone"));

                if (line)
                {
                    Assert.Equal(dialog.ViewModel.ConnectionText, window.AccountLineState.Text);
                    Assert.Equal(state == SyncWindowStates.FolderWithAccount ? string.Format(Strings.Sync_ConnectedAs, "someone@example.com") : Strings.Sync_NotConnected, window.AccountLineState.Text);
                    Assert.Equal(Strings.Sync_AccountForBucketsLine, window.AccountLineTitle.Text);
                    var right = window.AccountLineState.TranslatePoint(new Point(window.AccountLineState.ActualWidth, 0), window.AccountLine).X;
                    var titleRight = window.AccountLineTitle.TranslatePoint(new Point(window.AccountLineTitle.ActualWidth, 0), window.AccountLine).X;
                    var stateLeft = window.AccountLineState.TranslatePoint(default, window.AccountLine).X;
                    Assert.True(right <= window.AccountLine.ActualWidth + 0.5 && right >= window.AccountLine.ActualWidth * 0.75, $"{state}: the state ends at {right} of {window.AccountLine.ActualWidth}");
                    Assert.True(titleRight <= stateLeft + 0.5, $"{state}: the title ends at {titleRight}, the state starts at {stateLeft}");
                    Assert.True(right + 8 <= HeaderEnd(window) + 0.5, $"{state}: the state ends at {right}, against the chevron"); // review 45 W1

                    window.AccountLine.IsExpanded = true; // opened, it holds the group as it is under the choice
                    SyncWindowStates.Settle(window);
                    Assert.True(window.AccountHint.IsVisible, $"{state}: the opened line is empty");
                    Assert.Equal(Strings.Sync_AccountForBucketsHint, window.AccountHint.Text);
                    Assert.True(window.AccountPanel.IsVisible);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(true, SyncWindowStates.FolderWithBucket)]
    [InlineData(true, SyncWindowStates.FolderWithAccount)]
    [InlineData(false, SyncWindowStates.FolderWithBucket)]
    [InlineData(false, SyncWindowStates.FolderWithAccount)]
    public void The_account_lines_state_keeps_8_units_from_the_chevron_in_both_looks(bool fluent, string state)
    {
        // Review 45 W1: closed, the line read 「未連線∨」 in the Windows 11 look, the state against the chevron.
        wpf.Invoke(() =>
        {
            var was = DialogTheme.UseFluent;
            DialogTheme.Configure(fluent);
            var dialog = SyncWindowStates.Make(state, _folder);
            var window = new SyncWindow { DataContext = dialog.ViewModel };
            try
            {
                window.Show();
                SyncWindowStates.Settle(window);
                var what = $"{(fluent ? "Windows 11" : "classic")} look, {state}";
                Assert.True(window.AccountLine.IsVisible && !window.AccountLine.IsExpanded, what);
                var right = window.AccountLineState.TranslatePoint(new Point(window.AccountLineState.ActualWidth, 0), window.AccountLine).X;
                var end = HeaderEnd(window);
                Assert.True(right + 8 <= end + 0.5, $"{what}: the state ends at {right}, the header may reach {end}");
                Assert.True(right >= window.AccountLine.ActualWidth * 0.75, $"{what}: the state ends at {right} of {window.AccountLine.ActualWidth}");
            }
            finally
            {
                window.Close();
                DialogTheme.Configure(was);
            }
        });
    }

    /// <summary>
    /// How far right the account line's header may reach, in the line's units: the end of the slot the template gives the
    /// header, or the left edge of anything the template draws to the right of it (the Windows 11 look's chevron).
    /// </summary>
    private static double HeaderEnd(SyncWindow window)
    {
        var presenter = (FrameworkElement)VisualTreeHelper.GetParent(window.AccountLineHeader);
        var parent = (UIElement)VisualTreeHelper.GetParent(presenter);
        var end = parent.TranslatePoint(new Point(LayoutInformation.GetLayoutSlot(presenter).Right, 0), window.AccountLine).X;
        var start = presenter.TranslatePoint(default, window.AccountLine).X;
        DependencyObject? toggle = presenter;
        while (toggle is not null and not ToggleButton)
        {
            toggle = VisualTreeHelper.GetParent(toggle);
        }

        foreach (var drawn in Visuals(toggle ?? parent).OfType<FrameworkElement>())
        {
            if (drawn.IsVisible && drawn.ActualWidth > 0 && !drawn.IsAncestorOf(presenter) && !ReferenceEquals(drawn, presenter) && !presenter.IsAncestorOf(drawn))
            {
                var left = drawn.TranslatePoint(default, window.AccountLine).X;
                if (left > start + 1)
                {
                    end = Math.Min(end, left);
                }
            }
        }

        return end;
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var below in Visuals(child))
            {
                yield return below;
            }
        }
    }

    [Fact]
    public void Switching_the_place_moves_the_account_at_once_and_forgets_nothing()
    {
        wpf.Invoke(() =>
        {
            var dialog = SyncWindowStates.Make(folder: null, account: true, bucket: false);
            var viewModel = dialog.ViewModel;
            var window = new SyncWindow { DataContext = viewModel };
            try
            {
                window.Show();
                SyncWindowStates.Settle(window);
                window.SecretBox.Password = "typed, not yet saved";
                // The window sizes to its content up to the work area (WorkAreaFit), where the body scrolls: on CI's short screen
                // both places reach that cap, so the content's height is what shows the window following it.
                var contentWithGroup = window.Body.ExtentHeight;
                Assert.True(window.AccountGroup.IsVisible);

                dialog.Dialogs.Folder = _folder;
                var toFolder = viewModel.ChooseFolderAsync();
                SyncWindowStates.Settle(window, () => toFolder.IsCompleted);

                Assert.True(viewModel.UsesFolder);
                Assert.False(window.AccountGroup.IsVisible);
                Assert.True(window.AccountLine.IsVisible && !window.AccountLine.IsExpanded); // an account is connected: the line, closed
                Assert.Same(window.AccountLine, window.AccountPanel.Parent);
                Assert.True(window.FolderRow.IsVisible && window.FolderHint.IsVisible);
                Assert.True(Math.Abs(window.Body.ExtentHeight - contentWithGroup) > 1, $"the content kept its height: {window.Body.ExtentHeight}");
                Assert.Equal(SizeToContent.Height, window.SizeToContent); // and the window follows it, up to the work area
                Assert.Equal("typed, not yet saved", window.SecretBox.Password);
                Assert.Equal("client.apps.googleusercontent.com", viewModel.ClientId);
                Assert.True(viewModel.IsGoogleConnected);

                window.AccountLine.IsExpanded = true;
                var toGoogle = viewModel.UseGoogleDriveAsync();
                SyncWindowStates.Settle(window, () => toGoogle.IsCompleted);

                Assert.True(viewModel.UsesGoogle);
                Assert.True(window.AccountGroup.IsVisible && window.AccountPanel.IsVisible);
                Assert.Same(window.AccountGroup, window.AccountPanel.Parent);
                Assert.False(window.AccountLine.IsVisible);
                Assert.False(window.FolderRow.IsVisible || window.FolderHint.IsVisible);
                Assert.Equal("typed, not yet saved", window.SecretBox.Password);
                Assert.True(viewModel.IsGoogleConnected);

                dialog.Dialogs.Folder = null; // the picker cancelled: it opened at the folder used before Google Drive
                var cancelled = viewModel.ChooseFolderAsync();
                SyncWindowStates.Settle(window, () => cancelled.IsCompleted);
                Assert.Equal(Path.GetFullPath(_folder), dialog.Dialogs.LastPickStart);
                Assert.True(viewModel.UsesGoogle);

                dialog.Dialogs.Folder = _folder;
                var back = viewModel.ChooseFolderAsync();
                SyncWindowStates.Settle(window, () => back.IsCompleted);
                Assert.False(window.AccountLine.IsExpanded, "the line came back open"); // closed as it comes
                Assert.Equal(Path.GetFullPath(_folder), viewModel.SyncFolder);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void A_sign_in_opens_the_line_and_one_that_fails_opens_it_again()
    {
        wpf.Invoke(() =>
        {
            var browser = new TaskCompletionSource();
            var dialog = SyncWindowStates.Make(_folder, account: false, bucket: true, openBrowser: _ => browser.Task);
            var viewModel = dialog.ViewModel;
            var window = new SyncWindow { DataContext = viewModel };
            try
            {
                window.Show();
                SyncWindowStates.Settle(window);
                Assert.True(window.AccountLine.IsVisible && !window.AccountLine.IsExpanded);

                var signIn = viewModel.ConnectAsync(); // waits in the browser
                SyncWindowStates.Settle(window, () => viewModel.IsConnecting);
                Assert.True(window.AccountLine.IsExpanded, "the line stayed closed while the sign-in ran");
                Assert.True(window.AccountPanel.IsVisible);

                window.AccountLine.IsExpanded = false; // closed by hand while the browser is open
                browser.SetException(new GoogleAuthException("The sign-in did not finish."));
                SyncWindowStates.Settle(window, () => signIn.IsCompleted);

                Assert.False(viewModel.IsConnecting);
                Assert.True(viewModel.ConnectFailed);
                Assert.True(window.AccountLine.IsExpanded, "the line stayed closed after the sign-in failed");
                Assert.Equal("The sign-in did not finish.", viewModel.ShownStatusText);
                Assert.True(window.AccountLine.IsVisible); // the bucket icon still needs it
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Without_an_account_or_a_bucket_icon_a_folder_shows_no_account_at_all()
    {
        wpf.Invoke(() =>
        {
            var dialog = SyncWindowStates.Make(_folder, account: false, bucket: false);
            var window = new SyncWindow { DataContext = dialog.ViewModel };
            try
            {
                window.Show();
                SyncWindowStates.Settle(window);
                Assert.Equal(SyncAccountPlace.Hidden, dialog.ViewModel.AccountPlace);
                Assert.False(window.AccountGroup.IsVisible || window.AccountLine.IsVisible);
                Assert.False(window.SecretBox.IsVisible || window.AccountHint.IsVisible);
                Assert.Equal(Visibility.Collapsed, window.AccountGroup.Visibility); // collapsed, not disabled
                Assert.True(window.AccountGroup.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
