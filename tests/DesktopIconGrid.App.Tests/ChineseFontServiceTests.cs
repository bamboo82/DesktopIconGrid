using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Fonts;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// The parts of the font install that nothing tested in 0.12.0 (review 28): the service as the app wires it, the time
/// limit, the registry and the announcement to the running programs.
/// </summary>
public sealed class ChineseFontServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dig-font-service-").FullName;

    private static string TestFont => Path.Combine(AppContext.BaseDirectory, "Assets", "DigTestCjk-Light.ttf");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Windows may hold the announced font's file a moment after it was taken away again.
        }
    }

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static FontSource SourceOf(byte[] bytes) =>
        new(new Uri("https://example.test/DigTestCjk-Light.ttf"), "DigTestCjk-Light.ttf", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));

    private sealed class FakeRegistry : IFontRegistry
    {
        public Dictionary<string, string> User { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<KeyValuePair<string, string>> UserFonts() => User;

        public IEnumerable<KeyValuePair<string, string>> MachineFonts() => [];

        public void AddUserFont(string name, string path) => User[name] = path;
    }

    /// <summary>Serves <paramref name="body"/>, or holds the answer back until the request is cancelled.</summary>
    private sealed class Server(byte[] body, bool hang = false) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            var content = new ByteArrayContent(body);
            content.Headers.ContentLength = body.Length;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }

    private ChineseFontService Service(HttpMessageHandler server, FontSource? source = null, TimeSpan? limit = null, FakeRegistry? registry = null, List<string>? announced = null) =>
        new(new HttpClient(server), registry ?? new FakeRegistry(), Folder("data"), Folder("user-fonts"), Folder("windows-fonts"), Folder("program"), source, file => announced?.Add(file), limit);

    [Fact]
    public void A_file_of_that_name_that_holds_no_font_is_not_found()
    {
        // Review 28 F3: an empty or damaged file of that name showed the font chosen in Settings, and no Install button,
        // while the app drew Windows' font.
        var carried = Path.Combine(Folder(Path.Combine("program", "Fonts")), TaipeiSans.FileName);
        var service = Service(new Server([]));
        Assert.Null(service.Find());

        File.WriteAllBytes(carried, []);
        Assert.Null(service.Find());
        File.WriteAllBytes(carried, File.ReadAllBytes(TestFont)[..40]);
        Assert.Null(service.Find());

        File.Copy(TestFont, carried, overwrite: true);
        Assert.Equal(carried, service.Find());
    }

    [Fact]
    public void A_damaged_copy_beside_the_program_does_not_hide_the_font_installed_and_is_said_once()
    {
        // 0.12.2, review 29 N1: Find stopped at the first file of that name, and said so at every look.
        var carried = Path.Combine(Folder(Path.Combine("program", "Fonts")), TaipeiSans.FileName);
        File.WriteAllBytes(carried, File.ReadAllBytes(TestFont)[..40]);
        var installed = Path.Combine(Folder("user-fonts"), TaipeiSans.FileName);
        File.Copy(TestFont, installed);
        var registry = new FakeRegistry();
        registry.User["Taipei Sans TC Beta Light (TrueType)"] = installed;
        var service = Service(new Server([]), registry: registry);

        Assert.Equal(installed, service.Find());
        Assert.Equal(installed, service.Find()); // each start, each opening of Settings
        Assert.Single(AppLog.Default.Snapshot(), entry => entry.Message.Contains(carried, StringComparison.OrdinalIgnoreCase));

        File.Delete(installed);
        Assert.Null(service.Find()); // the damaged one is never taken
    }

    [Fact]
    public async Task The_download_is_given_up_after_its_limit_and_said_as_a_timeout_not_as_a_cancel()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), ChineseFontService.DownloadLimit);
        var source = SourceOf(File.ReadAllBytes(TestFont));

        var timedOut = await Service(new Server([], hang: true), source, TimeSpan.FromMilliseconds(200)).InstallAsync(new Progress<long>(), CancellationToken.None);
        Assert.Equal(new FontInstallResult(FontInstallOutcome.DownloadFailed, Message: Strings.FontInstall_TimedOut), timedOut);

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var cancelled = await Service(new Server([], hang: true), source, TimeSpan.FromMinutes(10)).InstallAsync(new Progress<long>(), cancel.Token);
        Assert.Equal(FontInstallOutcome.Cancelled, cancelled.Outcome);
        Assert.False(File.Exists(Path.Combine(_root, "user-fonts", source.FileName)));
    }

    [Fact]
    public async Task A_finished_install_is_listed_announced_and_raises_Installed_so_the_bar_takes_the_font_at_once()
    {
        // Review 28 F4: after an install, Cancel in Settings kept the Windows font for the run and the next start took
        // the font; the app now applies it when Installed is raised.
        var bytes = File.ReadAllBytes(TestFont);
        var source = SourceOf(bytes);
        var registry = new FakeRegistry();
        var announced = new List<string>();
        var service = Service(new Server(bytes), source, registry: registry, announced: announced);
        string? installed = null;
        service.Installed += (_, path) => installed = path;

        var result = await service.InstallAsync(new Progress<long>(), CancellationToken.None);

        var target = Path.Combine(_root, "user-fonts", source.FileName);
        Assert.Equal(new FontInstallResult(FontInstallOutcome.Installed, target), result);
        Assert.Equal(target, installed);
        Assert.Equal([target], announced);
        Assert.Equal([target], registry.User.Values);
        Assert.Equal(bytes, File.ReadAllBytes(target));
    }

    [Fact]
    public void The_registry_lists_a_font_added_for_this_user_under_its_key()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // A key of the test's own under HKCU: the real font lists are not touched.
        const string parent = @"Software\DesktopIconGrid.Tests";
        var key = $@"{parent}\Fonts-{Guid.NewGuid():N}";
        var registry = new WindowsFontRegistry(key);
        try
        {
            Assert.Empty(registry.UserFonts());
            registry.AddUserFont("Dig Test Light (TrueType)", TestFont);
            Assert.Equal([new KeyValuePair<string, string>("Dig Test Light (TrueType)", TestFont)], registry.UserFonts());
            Assert.Empty(registry.MachineFonts()); // that key is no machine's
            Assert.NotEmpty(new WindowsFontRegistry().MachineFonts()); // Windows' own fonts can be read
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
            using (var tests = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(parent))
            {
                if (tests is { SubKeyCount: 0, ValueCount: 0 })
                {
                    tests.Close();
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(parent, throwOnMissingSubKey: false);
                }
            }
        }
    }

    [Fact]
    public void A_font_is_added_for_this_session_and_announced_without_waiting_on_a_window_that_hangs()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // A copy of one of Windows' own fonts: GDI refuses the two-glyph test font, which WPF reads (run 197).
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var real = new[] { "arial.ttf", "segoeui.ttf", "consola.ttf" }.Select(name => Path.Combine(windows, name)).First(File.Exists);
        var font = Path.Combine(Folder("announce"), "DigAnnounceTest.ttf");
        File.Copy(real, font);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Assert.True(FontAnnouncement.Announce(font));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"the announcement took {watch.Elapsed}");
        }
        finally
        {
            NativeMethods.RemoveFontResourceW(font);
        }
    }
}
