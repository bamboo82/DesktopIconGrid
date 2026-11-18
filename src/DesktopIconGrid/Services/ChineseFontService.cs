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
public sealed class ChineseFontService(HttpClient http) : IChineseFontInstall
{
    /// <summary>How long a download may take in all before it is given up (the app's client has no limit of its own).</summary>
    public static readonly TimeSpan DownloadLimit = TimeSpan.FromMinutes(10);

    private readonly WindowsFontRegistry _registry = new();

    /// <summary><c>%LOCALAPPDATA%\Microsoft\Windows\Fonts</c>: where Windows itself puts a font installed for one user.</summary>
    public static string UserFontsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");

    public long Size => TaipeiSans.Light.Size;

    public string? Find() => new FontLocator(_registry, File.Exists, AppContext.BaseDirectory, UserFontsDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Fonts))
        .Find(TaipeiSans.FileName);

    public async Task<FontInstallResult> InstallAsync(IProgress<long> progress, CancellationToken cancel)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(DownloadLimit);
        var installer = new FontInstaller(http, _registry, AppPaths.DataDirectory, UserFontsDirectory, FontAnnouncement.Announce);
        var result = await installer.InstallAsync(TaipeiSans.Light, progress, limit.Token).ConfigureAwait(false);
        return result.Outcome == FontInstallOutcome.Cancelled && !cancel.IsCancellationRequested
            ? new FontInstallResult(FontInstallOutcome.DownloadFailed, Message: Strings.FontInstall_TimedOut) // the limit, not the user
            : result;
    }
}

/// <summary>The fonts in <c>…\Windows NT\CurrentVersion\Fonts</c> of the current user and of the machine.</summary>
internal sealed class WindowsFontRegistry : IFontRegistry
{
    private const string FontsKey = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";

    public IEnumerable<KeyValuePair<string, string>> UserFonts() => Values(Registry.CurrentUser);

    public IEnumerable<KeyValuePair<string, string>> MachineFonts() => Values(Registry.LocalMachine);

    public void AddUserFont(string name, string path)
    {
        using var key = Registry.CurrentUser.CreateSubKey(FontsKey, writable: true);
        key.SetValue(name, path, RegistryValueKind.String);
    }

    public void RemoveUserFont(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(FontsKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    private static List<KeyValuePair<string, string>> Values(RegistryKey root)
    {
        try
        {
            using var key = root.OpenSubKey(FontsKey);
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
    public static void Announce(string fontFile)
    {
        if (NativeMethods.AddFontResourceW(fontFile) == 0)
        {
            AppLog.Default.Warning("Fonts", $"{fontFile} could not be added for this session; it is there from the next sign-in");
        }

        NativeMethods.SendMessageTimeoutW(NativeMethods.HWND_BROADCAST, NativeMethods.WM_FONTCHANGE, IntPtr.Zero, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 1000, out _);
    }
}
