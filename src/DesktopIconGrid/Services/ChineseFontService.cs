using System.IO;
using System.Net.Http;
using System.Security;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Fonts;
using DesktopIconGrid.Resources;
using Microsoft.Win32;

namespace DesktopIconGrid.Services;

/// <summary>What the Settings row of the Chinese font needs (0.12.0 WP5): whether this PC has it, and the install.</summary>
public interface IChineseFontInstall
{
    /// <summary>The font file this PC has (<see cref="FontLocator"/>), or <see langword="null"/>.</summary>
    string? Find();

    /// <summary>The download's length in bytes, for the progress line.</summary>
    long Size { get; }

    /// <summary>Downloads and installs the font for this user; only ever on the button's click.</summary>
    Task<FontInstallResult> InstallAsync(IProgress<long> progress, CancellationToken cancel);
}

/// <summary>Taipei Sans TC Light on this PC: found as <see cref="FontLocator"/> says, installed by <see cref="FontInstaller"/>.</summary>
/// <param name="http">The app's HTTP client.</param>
/// <param name="registry">The registry's font lists (tests: a fake).</param>
/// <param name="dataDirectory">Where the download is kept while it runs; the app's data folder by default.</param>
/// <param name="userFontsDirectory">The user's font folder (<see cref="UserFontsDirectory"/> by default).</param>
/// <param name="windowsFontsDirectory">Windows' font folder by default.</param>
/// <param name="programDirectory">The program's folder, where a portable copy may carry the font.</param>
/// <param name="source">The file to install; Taipei Sans TC Light by default.</param>
/// <param name="announce">Tells the running programs about the new font; <see cref="FontAnnouncement.Announce"/> by default.</param>
/// <param name="downloadLimit">How long a download may take in all; <see cref="DownloadLimit"/> by default.</param>
public sealed class ChineseFontService(
    HttpClient http,
    IFontRegistry? registry = null,
    string? dataDirectory = null,
    string? userFontsDirectory = null,
    string? windowsFontsDirectory = null,
    string? programDirectory = null,
    FontSource? source = null,
    Action<string>? announce = null,
    TimeSpan? downloadLimit = null) : IChineseFontInstall
{
    /// <summary>How long a download may take in all before it is given up (the app's client has no limit of its own).</summary>
    public static readonly TimeSpan DownloadLimit = TimeSpan.FromMinutes(10);

    private readonly IFontRegistry _registry = registry ?? new WindowsFontRegistry();

    /// <summary>The files of that name found to hold no font, each said once per run (review 29 N1).</summary>
    private readonly HashSet<string> _saidNoFont = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _userFonts = userFontsDirectory ?? UserFontsDirectory;
    private readonly FontSource _source = source ?? TaipeiSans.Light;

    /// <summary><c>%LOCALAPPDATA%\Microsoft\Windows\Fonts</c>: where Windows itself puts a font installed for one user.</summary>
    public static string UserFontsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");

    /// <summary>
    /// Raised, on the install's thread, once the font is installed: the bar takes it at once, whatever Save or Cancel in
    /// Settings does then (0.12.1, review 28 F4: Cancel kept the Windows font for the run, and the next start used it).
    /// </summary>
    public event EventHandler<string>? Installed;

    public long Size => _source.Size;

    /// <summary>
    /// The font file: the first place that has a file of its name that is a font (review 28 F3). A damaged or empty file
    /// of that name is passed over for the next place, beside the program, then this user's fonts, then every user's,
    /// and said once per run in the log, not at each look (each start, each opening of Settings; 0.12.2, review 29 N1).
    /// </summary>
    public string? Find()
    {
        var locator = new FontLocator(_registry, File.Exists, programDirectory ?? AppContext.BaseDirectory, _userFonts, windowsFontsDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Fonts));
        foreach (var file in locator.Candidates(_source.FileName))
        {
            if (FontNames.IsFont(file))
            {
                return file;
            }

            bool first;
            lock (_saidNoFont)
            {
                first = _saidNoFont.Add(file);
            }

            if (first)
            {
                AppLog.Default.Info("Fonts", $"{file} has the name of {_source.FileName} but holds no font that can be read; it is passed over");
            }
        }

        return null;
    }

    public async Task<FontInstallResult> InstallAsync(IProgress<long> progress, CancellationToken cancel)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(downloadLimit ?? DownloadLimit);
        var installer = new FontInstaller(http, _registry, dataDirectory ?? AppPaths.DataDirectory, _userFonts, announce ?? (file => FontAnnouncement.Announce(file)));
        var result = await installer.InstallAsync(_source, progress, limit.Token).ConfigureAwait(false);
        if (result.Outcome == FontInstallOutcome.Cancelled && !cancel.IsCancellationRequested)
        {
            return new FontInstallResult(FontInstallOutcome.DownloadFailed, Message: Strings.FontInstall_TimedOut); // the limit, not the user
        }

        if (result is { Outcome: FontInstallOutcome.Installed, Path: { } installed })
        {
            Installed?.Invoke(this, installed);
        }

        return result;
    }
}

/// <summary>The fonts in <c>…\Windows NT\CurrentVersion\Fonts</c> of the current user and of the machine.</summary>
/// <param name="fontsKey">The key under HKCU and HKLM; tests give one of their own.</param>
internal sealed class WindowsFontRegistry(string fontsKey = WindowsFontRegistry.FontsKey) : IFontRegistry
{
    public const string FontsKey = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";

    public IEnumerable<KeyValuePair<string, string>> UserFonts() => Values(Registry.CurrentUser);

    public IEnumerable<KeyValuePair<string, string>> MachineFonts() => Values(Registry.LocalMachine);

    public void AddUserFont(string name, string path)
    {
        using var key = Registry.CurrentUser.CreateSubKey(fontsKey, writable: true);
        key.SetValue(name, path, RegistryValueKind.String);
    }

    private List<KeyValuePair<string, string>> Values(RegistryKey root)
    {
        try
        {
            using var key = root.OpenSubKey(fontsKey);
            return key is null
                ? []
                : key.GetValueNames().Select(name => (name, key.GetValue(name) as string)).Where(value => value.Item2 is not null)
                    .Select(value => new KeyValuePair<string, string>(value.name, value.Item2!)).ToList();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            AppLog.Default.Debug("Fonts", $"The installed fonts could not be read: {ex.Message}");
            return [];
        }
    }
}

/// <summary>Tells this session's programs about a font just installed, without waiting on one that hangs.</summary>
internal static class FontAnnouncement
{
    /// <summary>Adds the font for this session and tells the programs; false when Windows would not add it now.</summary>
    public static bool Announce(string fontFile)
    {
        var added = NativeMethods.AddFontResourceW(fontFile) != 0;
        if (!added)
        {
            AppLog.Default.Warning("Fonts", $"{fontFile} could not be added for this session; it is there from the next sign-in");
        }

        NativeMethods.SendMessageTimeoutW(NativeMethods.HWND_BROADCAST, NativeMethods.WM_FONTCHANGE, IntPtr.Zero, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 1000, out _);
        return added;
    }
}
