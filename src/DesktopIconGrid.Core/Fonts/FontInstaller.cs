using System.Security;
using System.Security.Cryptography;
using DesktopIconGrid.Core.Diagnostics;

namespace DesktopIconGrid.Core.Fonts;

/// <summary>How an install ended.</summary>
public enum FontInstallOutcome
{
    /// <summary>The font is installed for this user (or was already, with the same digest).</summary>
    Installed,

    /// <summary>Cancelled while downloading: nothing is left.</summary>
    Cancelled,

    /// <summary>No connection, or the server refused: nothing is left.</summary>
    DownloadFailed,

    /// <summary>The download was not the expected file (its length or digest): nothing is installed.</summary>
    Mismatch,

    /// <summary>The font folder or the registry could not be written: nothing is installed.</summary>
    InstallFailed,

    /// <summary>A file of that name with another digest is in the user's font folder: left alone.</summary>
    OtherFileThere,
}

/// <summary>An install's outcome, the installed file, and the error's own words.</summary>
public sealed record FontInstallResult(FontInstallOutcome Outcome, string? Path = null, string? Message = null);

/// <summary>
/// Downloads a font and installs it for this user, without an administrator (0.12.0 WP5, T3). The download goes to
/// <c>&lt;file&gt;.part</c> in the app's data folder; it stops when the server gives another length or sends more bytes
/// than the file has, and is kept only when its SHA-256 is the expected one. Then the file is moved into the user's
/// font folder, listed under the user's fonts in the registry as "&lt;its full name&gt; (TrueType)", and announced to
/// the running programs (<paramref name="announce"/>). Whatever fails, no part file, no font file and no registry value
/// are left behind; a file of that name already there with another digest is not overwritten. Nothing runs it but the
/// click on its button.
/// </summary>
/// <param name="http">The app's HTTP client.</param>
/// <param name="registry">The registry's font lists.</param>
/// <param name="partDirectory">The app's data folder, for the download.</param>
/// <param name="userFontsDirectory"><c>%LOCALAPPDATA%\Microsoft\Windows\Fonts</c>.</param>
/// <param name="announce">Tells the running programs about the new font (<c>AddFontResourceW</c>, <c>WM_FONTCHANGE</c>).</param>
public sealed class FontInstaller(HttpClient http, IFontRegistry registry, string partDirectory, string userFontsDirectory, Action<string>? announce = null)
{
    /// <summary>Downloads and installs <paramref name="source"/>; <paramref name="progress"/> hears the bytes received so far.</summary>
    public async Task<FontInstallResult> InstallAsync(FontSource source, IProgress<long>? progress, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(source);
        var target = Path.Combine(userFontsDirectory, source.FileName);
        if (File.Exists(target))
        {
            return await DigestOfAsync(target, cancel).ConfigureAwait(false) == source.Sha256
                ? Register(source, target, movedHere: false)
                : new FontInstallResult(FontInstallOutcome.OtherFileThere, target);
        }

        var part = Path.Combine(partDirectory, source.FileName + ".part");
        var downloaded = await DownloadAsync(source, part, progress, cancel).ConfigureAwait(false);
        if (downloaded is not null)
        {
            Delete(part);
            return downloaded;
        }

        try
        {
            Directory.CreateDirectory(userFontsDirectory);
            if (File.Exists(target))
            {
                // Put there meanwhile, by hand or by another copy of the app.
                Delete(part);
                return await DigestOfAsync(target, cancel).ConfigureAwait(false) == source.Sha256
                    ? Register(source, target, movedHere: false)
                    : new FontInstallResult(FontInstallOutcome.OtherFileThere, target);
            }

            File.Move(part, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Delete(part);
            return new FontInstallResult(FontInstallOutcome.InstallFailed, Message: ex.Message);
        }

        return Register(source, target, movedHere: true);
    }

    /// <summary>The download into <paramref name="part"/>; <see langword="null"/> when it is the expected file, else why not.</summary>
    private async Task<FontInstallResult?> DownloadAsync(FontSource source, string part, IProgress<long>? progress, CancellationToken cancel)
    {
        try
        {
            using var response = await http.GetAsync(source.Url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new FontInstallResult(FontInstallOutcome.DownloadFailed, Message: $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim());
            }

            if (response.Content.Headers.ContentLength is { } length && length != source.Size)
            {
                AppLog.Default.Warning("Fonts", $"{source.FileName}: the server gives {length} bytes, not {source.Size}; not downloaded");
                return new FontInstallResult(FontInstallOutcome.Mismatch);
            }

            FileStream output;
            try
            {
                Directory.CreateDirectory(partDirectory);
                output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new FontInstallResult(FontInstallOutcome.InstallFailed, Message: ex.Message);
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            await using (output.ConfigureAwait(false))
            {
                var input = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
                await using (input.ConfigureAwait(false))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
                    {
                        received += read;
                        if (received > source.Size)
                        {
                            AppLog.Default.Warning("Fonts", $"{source.FileName}: more than {source.Size} bytes arrived; not installed");
                            return new FontInstallResult(FontInstallOutcome.Mismatch);
                        }

                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                        progress?.Report(received);
                    }
                }
            }

            var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (received != source.Size || digest != source.Sha256)
            {
                AppLog.Default.Warning("Fonts", $"{source.FileName}: {received} bytes with SHA-256 {digest}, not the expected file; not installed");
                return new FontInstallResult(FontInstallOutcome.Mismatch);
            }

            return null;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return new FontInstallResult(FontInstallOutcome.Cancelled);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            // No connection, a connection that broke, or the client's own timeout.
            return new FontInstallResult(FontInstallOutcome.DownloadFailed, Message: ex.Message);
        }
    }

    /// <summary>Lists the installed file under the user's fonts and announces it; undoes the move when the registry refuses.</summary>
    private FontInstallResult Register(FontSource source, string target, bool movedHere)
    {
        string? fullName;
        try
        {
            using var file = File.OpenRead(target);
            fullName = FontNames.FullName(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            fullName = null;
        }

        var name = $"{fullName ?? Path.GetFileNameWithoutExtension(source.FileName)} (TrueType)";
        try
        {
            registry.AddUserFont(name, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            if (movedHere)
            {
                Delete(target);
            }

            return new FontInstallResult(FontInstallOutcome.InstallFailed, Message: ex.Message);
        }

        try
        {
            announce?.Invoke(target);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Installed all the same: Windows loads it at the next sign-in.
            AppLog.Default.Warning("Fonts", $"{source.FileName} is installed, but running programs were not told: {ex.Message}");
        }

        AppLog.Default.Info("Fonts", $"{source.FileName} installed for this user as \"{name}\"");
        return new FontInstallResult(FontInstallOutcome.Installed, target);
    }

    private static async Task<string> DigestOfAsync(string path, CancellationToken cancel)
    {
        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancel).ConfigureAwait(false));
        }
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Warning("Fonts", $"{path} could not be deleted: {ex.Message}");
        }
    }
}
