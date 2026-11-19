using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopIconGrid.Core.Fonts;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.12.0 WP5 (T1, T3, T4): finding Taipei Sans TC Light, installing it, and the setting.</summary>
public sealed class ChineseFontTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dig-fonts-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder(string name) => Path.Combine(_root, name);

    /// <summary>The registry's font lists, in memory.</summary>
    private sealed class FakeRegistry : IFontRegistry
    {
        public Dictionary<string, string> User { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Machine { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Exception? RefuseWith { get; set; }

        public IEnumerable<KeyValuePair<string, string>> UserFonts() => User;

        public IEnumerable<KeyValuePair<string, string>> MachineFonts() => Machine;

        public void AddUserFont(string name, string path)
        {
            if (RefuseWith is { } refusal)
            {
                throw refusal;
            }

            User[name] = path;
        }
    }

    // ----- T1: finding it ----------------------------------------------------------------------------

    [Fact]
    public void The_font_is_looked_for_next_to_the_program_then_for_this_user_then_for_every_user()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var registry = new FakeRegistry();
        var locator = new FontLocator(registry, files.Contains, Folder("program"), Folder("user-fonts"), Folder("windows-fonts"));
        var name = TaipeiSans.FileName;

        Assert.Null(locator.Find(name));

        // For every user, by a bare file name: in Windows' font folder.
        registry.Machine["Taipei Sans TC Beta Light (TrueType)"] = name;
        files.Add(Path.Combine(Folder("windows-fonts"), name));
        Assert.Equal(Path.Combine(Folder("windows-fonts"), name), locator.Find(name));

        // For this user, by a full path: before every user's.
        var mine = Path.Combine(Folder("elsewhere"), name);
        registry.User["Taipei Sans TC Beta Light (TrueType)"] = $"\"{mine}\"";
        files.Add(mine);
        Assert.Equal(mine, locator.Find(name));

        // Carried next to the program: before both.
        var carried = Path.Combine(Folder("program"), "Fonts", name);
        files.Add(carried);
        Assert.Equal(carried, locator.Find(name));
    }

    [Fact]
    public void A_value_naming_a_missing_file_or_another_file_is_passed_over()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var registry = new FakeRegistry();
        var locator = new FontLocator(registry, files.Contains, Folder("program"), Folder("user-fonts"), Folder("windows-fonts"));
        var name = TaipeiSans.FileName;

        registry.User["Taipei Sans TC Beta Light (TrueType)"] = name; // listed, but its file is gone
        registry.User["Taipei Sans TC Beta Regular (TrueType)"] = "TaipeiSansTCBeta-Regular.ttf";
        files.Add(Path.Combine(Folder("user-fonts"), "TaipeiSansTCBeta-Regular.ttf"));
        registry.User["Other (TrueType)"] = "My" + name; // ends in the name, but is another file
        files.Add(Path.Combine(Folder("user-fonts"), "My" + name));
        Assert.Null(locator.Find(name));

        // A bare name for this user is in the user's font folder.
        files.Add(Path.Combine(Folder("user-fonts"), name));
        Assert.Equal(Path.Combine(Folder("user-fonts"), name), locator.Find(name));
    }

    // ----- the font's own name ------------------------------------------------------------------------

    /// <summary>A minimal TrueType file: a table directory and a name table with a full name (name 4) in US English.</summary>
    internal static byte[] MinimalFont(string fullName)
    {
        var text = Encoding.BigEndianUnicode.GetBytes(fullName);
        var name = new List<byte>();
        name.AddRange(U16(0)); // format
        name.AddRange(U16(2)); // two records
        name.AddRange(U16(6 + (2 * 12))); // where the strings begin
        name.AddRange([.. U16(1), .. U16(0), .. U16(0), .. U16(4), .. U16(4), .. U16(0)]); // a Macintosh name: not read
        name.AddRange([.. U16(3), .. U16(1), .. U16(0x0409), .. U16(4), .. U16(text.Length), .. U16(4)]);
        name.AddRange("Mac!"u8.ToArray());
        name.AddRange(text);

        var font = new List<byte>();
        font.AddRange([0, 1, 0, 0]); // TrueType
        font.AddRange([.. U16(1), .. U16(16), .. U16(0), .. U16(0)]);
        font.AddRange("name"u8.ToArray());
        font.AddRange([.. U32(0), .. U32(12 + 16), .. U32(name.Count)]);
        font.AddRange(name);
        return [.. font];

        static byte[] U16(int value) => [(byte)(value >> 8), (byte)value];
        static byte[] U32(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
    }

    [Fact]
    public void The_full_name_is_read_from_the_file_itself()
    {
        Assert.Equal("Taipei Sans TC Beta Light", FontNames.FullName(new MemoryStream(MinimalFont("Taipei Sans TC Beta Light"))));
        Assert.Null(FontNames.FullName(new MemoryStream("not a font at all"u8.ToArray())));
        Assert.Null(FontNames.FullName(new MemoryStream([])));
    }

    // ----- T3: installing it --------------------------------------------------------------------------

    /// <summary>Answers every request with a status, the bytes, and a length header unless told otherwise.</summary>
    private sealed class FakeServer(HttpStatusCode status, byte[] body, long? contentLength) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public Exception? FailWith { get; init; }

        /// <summary>Holds the body back until the request is cancelled.</summary>
        public bool Hang { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (FailWith is { } failure)
            {
                throw failure;
            }

            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            var content = new ByteArrayContent(body);
            content.Headers.ContentLength = contentLength;
            return new HttpResponseMessage(status) { Content = content };
        }
    }

    private static readonly byte[] Font = MinimalFont("Test Sans Light");

    private static FontSource SourceOf(byte[] bytes) =>
        new(new Uri("https://example.test/TestSans-Light.ttf"), "TestSans-Light.ttf", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));

    private (FontInstaller Installer, FakeRegistry Registry, List<string> Announced) Installer(HttpMessageHandler server)
    {
        var registry = new FakeRegistry();
        var announced = new List<string>();
        return (new FontInstaller(new HttpClient(server), registry, Folder("data"), Folder("user-fonts"), announced.Add), registry, announced);
    }

    private void AssertNothingLeft(FakeRegistry registry, string fileName)
    {
        Assert.False(File.Exists(Path.Combine(Folder("user-fonts"), fileName)));
        Assert.Empty(registry.User);
        Assert.False(File.Exists(Path.Combine(Folder("data"), fileName + ".part")));
    }

    [Fact]
    public async Task The_right_bytes_are_installed_for_this_user_listed_under_their_full_name_and_announced()
    {
        var (installer, registry, announced) = Installer(new FakeServer(HttpStatusCode.OK, Font, Font.Length));
        var progress = new List<long>();

        var result = await installer.InstallAsync(SourceOf(Font), new Progress(progress), CancellationToken.None);

        var target = Path.Combine(Folder("user-fonts"), "TestSans-Light.ttf");
        Assert.Equal(new FontInstallResult(FontInstallOutcome.Installed, target), result);
        Assert.Equal(Font, File.ReadAllBytes(target));
        Assert.Equal(target, registry.User["Test Sans Light (TrueType)"]);
        Assert.Equal([target], announced);
        Assert.False(File.Exists(Path.Combine(Folder("data"), "TestSans-Light.ttf.part")));
        Assert.Equal(Font.Length, progress[^1]);
    }

    [Fact]
    public async Task Another_length_one_byte_too_many_or_another_digest_install_nothing()
    {
        var source = SourceOf(Font);
        foreach (var server in new[]
        {
            new FakeServer(HttpStatusCode.OK, Font, Font.Length + 1), // the server says another length
            new FakeServer(HttpStatusCode.OK, [.. Font, 0], null), // no length given, and one byte too many arrives
            new FakeServer(HttpStatusCode.OK, Font.Select(b => (byte)~b).ToArray(), Font.Length), // the same length, another digest
        })
        {
            var (installer, registry, announced) = Installer(server);
            Assert.Equal(FontInstallOutcome.Mismatch, (await installer.InstallAsync(source, null, CancellationToken.None)).Outcome);
            AssertNothingLeft(registry, source.FileName);
            Assert.Empty(announced);
        }
    }

    [Fact]
    public async Task A_refusal_no_connection_and_a_cancel_install_nothing()
    {
        var source = SourceOf(Font);

        var (refused, registry, _) = Installer(new FakeServer(HttpStatusCode.NotFound, [], 0));
        var result = await refused.InstallAsync(source, null, CancellationToken.None);
        Assert.Equal(FontInstallOutcome.DownloadFailed, result.Outcome);
        Assert.Contains("404", result.Message, StringComparison.Ordinal);
        AssertNothingLeft(registry, source.FileName);

        var (offline, offlineRegistry, _) = Installer(new FakeServer(HttpStatusCode.OK, Font, Font.Length) { FailWith = new HttpRequestException("No such host is known.") });
        Assert.Equal(new FontInstallResult(FontInstallOutcome.DownloadFailed, Message: "No such host is known."), await offline.InstallAsync(source, null, CancellationToken.None));
        AssertNothingLeft(offlineRegistry, source.FileName);

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var (hanging, hangingRegistry, _) = Installer(new FakeServer(HttpStatusCode.OK, Font, Font.Length) { Hang = true });
        Assert.Equal(FontInstallOutcome.Cancelled, (await hanging.InstallAsync(source, null, cancel.Token)).Outcome);
        AssertNothingLeft(hangingRegistry, source.FileName);
    }

    [Fact]
    public async Task A_file_of_that_name_that_cannot_be_read_is_a_failure_said_and_a_cancel_while_it_is_hashed_is_a_cancel()
    {
        // 0.12.1, review 28 F1: hashing a file already there let IOException, UnauthorizedAccessException and
        // OperationCanceledException out, and the Settings row stayed on "Downloading… 0.0 / 20.3 MB".
        var source = SourceOf(Font);
        var target = Path.Combine(Folder("user-fonts"), source.FileName);
        Directory.CreateDirectory(Folder("user-fonts"));
        File.WriteAllBytes(target, Font);
        var server = new FakeServer(HttpStatusCode.OK, Font, Font.Length);
        var (installer, registry, announced) = Installer(server);

        FontInstallResult held;
        using (new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            held = await installer.InstallAsync(source, null, CancellationToken.None);
        }

        Assert.Equal(FontInstallOutcome.InstallFailed, held.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(held.Message));
        Assert.Equal(Font, File.ReadAllBytes(target)); // left as it was
        Assert.Empty(registry.User);
        Assert.Empty(announced);
        Assert.Equal(0, server.Requests);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.Equal(FontInstallOutcome.Cancelled, (await installer.InstallAsync(source, null, cancelled.Token)).Outcome);
        Assert.Empty(registry.User);
    }

    /// <summary>Sends the first part of a font, then holds the rest back until the request is cancelled.</summary>
    private sealed class TrickleServer(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StreamContent(new Trickle(body[..(body.Length / 2)]));
            content.Headers.ContentLength = body.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }

        private sealed class Trickle(byte[] first) : Stream
        {
            private bool _sent;

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (!_sent)
                {
                    _sent = true;
                    first.CopyTo(buffer);
                    return first.Length;
                }

                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    /// <summary>Hears the first bytes of a download on the reading thread, while the part file is open.</summary>
    private sealed class FirstBytes(string part) : IProgress<long>
    {
        public TaskCompletionSource<bool> Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Report(long value) => Arrived.TrySetResult(File.Exists(part));
    }

    [Fact]
    public async Task A_cancel_in_the_middle_of_the_body_leaves_no_part_file()
    {
        // Review 28: the test of a cancel cancelled before the response headers, so no part file was ever at risk.
        var source = SourceOf(Font);
        var part = Path.Combine(Folder("data"), source.FileName + ".part");
        var (installer, registry, _) = Installer(new TrickleServer(Font));
        var progress = new FirstBytes(part);
        using var cancel = new CancellationTokenSource();

        var install = installer.InstallAsync(source, progress, cancel.Token);
        Assert.True(await progress.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10))); // half of it is in the part file
        await cancel.CancelAsync();

        Assert.Equal(FontInstallOutcome.Cancelled, (await install).Outcome);
        Assert.False(File.Exists(part));
        AssertNothingLeft(registry, source.FileName);
    }

    [Fact]
    public async Task A_name_listed_for_another_file_is_not_written_over_and_nothing_is_installed()
    {
        // Review 28 F6: AddUserFont wrote over a value of that name that pointed at another file.
        var source = SourceOf(Font);
        var other = Path.Combine(Folder("elsewhere"), "TestSans-Light-1.0.ttf");
        var (installer, registry, announced) = Installer(new FakeServer(HttpStatusCode.OK, Font, Font.Length));
        registry.User["Test Sans Light (TrueType)"] = other;

        var result = await installer.InstallAsync(source, null, CancellationToken.None);

        Assert.Equal(new FontInstallResult(FontInstallOutcome.NameTaken, other), result);
        Assert.Equal(other, registry.User["Test Sans Light (TrueType)"]);
        Assert.False(File.Exists(Path.Combine(Folder("user-fonts"), source.FileName))); // the moved file is taken back
        Assert.False(File.Exists(Path.Combine(Folder("data"), source.FileName + ".part")));
        Assert.Empty(announced);

        // The same file under that name, as a bare file name or its full path, is no other file.
        foreach (var same in new[] { source.FileName, Path.Combine(Folder("user-fonts"), source.FileName) })
        {
            var (again, sameRegistry, _) = Installer(new FakeServer(HttpStatusCode.OK, Font, Font.Length));
            sameRegistry.User["Test Sans Light (TrueType)"] = same;
            Assert.Equal(FontInstallOutcome.Installed, (await again.InstallAsync(source, null, CancellationToken.None)).Outcome);
            File.Delete(Path.Combine(Folder("user-fonts"), source.FileName));
        }
    }

    [Fact]
    public void A_file_is_a_font_when_its_name_table_gives_a_name()
    {
        // Review 28 F3: a file found by its name but empty or damaged is not "found".
        Directory.CreateDirectory(Folder("files"));
        var font = Path.Combine(Folder("files"), "font.ttf");
        var empty = Path.Combine(Folder("files"), "empty.ttf");
        var damaged = Path.Combine(Folder("files"), "damaged.ttf");
        File.WriteAllBytes(font, Font);
        File.WriteAllBytes(empty, []);
        File.WriteAllBytes(damaged, Font[..20]);

        Assert.True(FontNames.IsFont(font));
        Assert.False(FontNames.IsFont(empty));
        Assert.False(FontNames.IsFont(damaged));
        Assert.False(FontNames.IsFont(Path.Combine(Folder("files"), "missing.ttf")));
    }

    [Fact]
    public async Task A_folder_or_registry_that_cannot_be_written_leaves_no_font_value_or_part_file()
    {
        var source = SourceOf(Font);

        var (refusing, registry, _) = Installer(new FakeServer(HttpStatusCode.OK, Font, Font.Length));
        registry.RefuseWith = new UnauthorizedAccessException("Access to the registry key is denied.");
        Assert.Equal(new FontInstallResult(FontInstallOutcome.InstallFailed, Message: "Access to the registry key is denied."), await refusing.InstallAsync(source, null, CancellationToken.None));
        AssertNothingLeft(registry, source.FileName);

        // The user's font folder cannot be made: a file has its name. (The case above left it empty.)
        Directory.Delete(Folder("user-fonts"));
        File.WriteAllText(Folder("user-fonts"), "in the way");
        try
        {
            var (blocked, blockedRegistry, _) = Installer(new FakeServer(HttpStatusCode.OK, Font, Font.Length));
            Assert.Equal(FontInstallOutcome.InstallFailed, (await blocked.InstallAsync(source, null, CancellationToken.None)).Outcome);
            Assert.Empty(blockedRegistry.User);
            Assert.False(File.Exists(Path.Combine(Folder("data"), source.FileName + ".part")));
        }
        finally
        {
            File.Delete(Folder("user-fonts"));
        }
    }

    [Fact]
    public async Task A_file_of_that_name_already_there_is_used_when_it_is_the_same_and_never_overwritten_when_not()
    {
        var source = SourceOf(Font);
        var target = Path.Combine(Folder("user-fonts"), source.FileName);
        Directory.CreateDirectory(Folder("user-fonts"));

        File.WriteAllBytes(target, [1, 2, 3]);
        var server = new FakeServer(HttpStatusCode.OK, Font, Font.Length);
        var (installer, registry, _) = Installer(server);
        Assert.Equal(new FontInstallResult(FontInstallOutcome.OtherFileThere, target), await installer.InstallAsync(source, null, CancellationToken.None));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(target));
        Assert.Empty(registry.User);
        Assert.Equal(0, server.Requests); // not even downloaded

        File.WriteAllBytes(target, Font); // the same font, put there by hand
        Assert.Equal(new FontInstallResult(FontInstallOutcome.Installed, target), await installer.InstallAsync(source, null, CancellationToken.None));
        Assert.Equal(target, registry.User["Test Sans Light (TrueType)"]);
        Assert.Equal(0, server.Requests);
    }

    [Fact]
    public void The_built_in_source_is_the_file_checked_for_this_release()
    {
        Assert.Equal("TaipeiSansTCBeta-Light.ttf", TaipeiSans.Light.FileName);
        Assert.Equal(20_279_264, TaipeiSans.Light.Size);
        Assert.Equal("d69a9ea6a77f694c3a1a1fb766c764aa74cb3e2c4c69f1b532edf98b3f2bb662", TaipeiSans.Light.Sha256);
        Assert.Equal("https", TaipeiSans.Light.Url.Scheme);
        Assert.EndsWith("/24ca88e96d1e842558df3ad6fad71993c07754c4/packages/core/src/TaipeiSansTCBeta-Light.ttf", TaipeiSans.Light.Url.AbsoluteUri, StringComparison.Ordinal);
    }

    // ----- the setting --------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, "zh-TW", true, true)]
    [InlineData(null, "en-US", true, true)]
    [InlineData(null, "zh-CN", true, false)] // the font draws Taiwan's glyph forms
    [InlineData("taipei-sans-light", "zh-CN", true, true)]
    [InlineData("system", "zh-TW", true, false)]
    [InlineData("taipei-sans-light", "zh-TW", false, false)] // without the font, as before
    [InlineData(null, "zh-TW", false, false)]
    public void Chinese_is_drawn_in_Taipei_Sans_by_default_except_for_Simplified_Chinese_and_only_with_the_font(string? setting, string culture, bool found, bool uses)
    {
        Assert.Equal(uses, ChineseFonts.UsesTaipei(setting, CultureInfo.GetCultureInfo(culture), found));
    }

    /// <summary>The settings of 0.11.0, as that version reads and writes them.</summary>
    private sealed class OlderSettings
    {
        public bool ShowLabels { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    [Fact]
    public void The_setting_is_written_only_when_chosen_reads_back_and_survives_an_older_version()
    {
        var plain = JsonSerializer.Serialize(new LauncherSettings(), LayoutStorage.JsonOptions);
        Assert.DoesNotContain("chineseFont", plain, StringComparison.Ordinal);

        var chosen = new LauncherSettings { ChineseFont = " System " };
        chosen.Normalize();
        Assert.Equal(LauncherSettings.ChineseFontSystem, chosen.ChineseFont);
        var json = JsonSerializer.Serialize(chosen, LayoutStorage.JsonOptions);
        Assert.Equal(LauncherSettings.ChineseFontSystem, JsonSerializer.Deserialize<LauncherSettings>(json, LayoutStorage.JsonOptions)!.ChineseFont);

        var older = JsonSerializer.Deserialize<OlderSettings>(json, LayoutStorage.JsonOptions)!;
        Assert.Equal(LauncherSettings.ChineseFontSystem, older.ExtensionData!["chineseFont"].GetString());
        Assert.Equal(LauncherSettings.ChineseFontSystem, JsonSerializer.Deserialize<LauncherSettings>(JsonSerializer.Serialize(older, LayoutStorage.JsonOptions), LayoutStorage.JsonOptions)!.ChineseFont);

        var unknown = new LauncherSettings { ChineseFont = "comic-sans" };
        Assert.True(unknown.Normalize());
        Assert.Null(unknown.ChineseFont); // no such choice: the default

        var copy = new LauncherSettings();
        copy.CopyFrom(new LauncherSettings { ChineseFont = LauncherSettings.ChineseFontTaipei });
        Assert.Equal(LauncherSettings.ChineseFontTaipei, copy.ChineseFont);
    }

    /// <summary>Collects progress reports as they are made, not later on a thread pool thread.</summary>
    private sealed class Progress(List<long> reports) : IProgress<long>
    {
        public void Report(long value) => reports.Add(value);
    }
}
