using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Services;

/// <summary>Stores the Google token encrypted with DPAPI for the current Windows user.</summary>
public sealed class DpapiTokenStore(string filePath) : ITokenStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DesktopIconGrid.GoogleToken.v1");

    /// <summary>
    /// The sign-in's file in <paramref name="directory"/>. DPAPI ties it to one PC and user, so a portable copy keeps one
    /// per PC (<c>google-token-&lt;machine&gt;.bin</c>): PCs that share the drive no longer sign each other out. The shared
    /// file of 0.8.0 becomes this PC's own when, and only when, it decrypts here: it was written here and is of no use
    /// to any other PC. It is moved, not copied, so a Disconnect, or a refresh token Google no longer takes (both
    /// delete this PC's file), is not undone at the next start.
    /// </summary>
    /// <param name="readableHere">Whether a file decrypts on this PC; by default a <see cref="Load"/> of it.</param>
    internal static string FileForThisPc(string directory, string? machine, bool portable, Func<string, bool>? readableHere = null)
    {
        var shared = Path.Combine(directory, "google-token.bin");
        if (!portable || machine is null)
        {
            return shared;
        }

        var own = Path.Combine(directory, $"google-token-{machine}.bin");
        readableHere ??= path => new DpapiTokenStore(path).Load() is not null;
        if (!File.Exists(own) && File.Exists(shared) && readableHere(shared))
        {
            try
            {
                File.Move(shared, own);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Default.Warning("Sync", $"The Google sign-in could not be kept for this PC: {ex.Message}");
            }
        }

        return own;
    }

    public OAuthToken? Load()
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(filePath), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<OAuthToken>(bytes, LayoutStorage.JsonOptions);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(OAuthToken? token)
    {
        if (token is null)
        {
            try
            {
                File.Delete(filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nothing sensible to do; the in-memory token is already gone.
            }

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(token, LayoutStorage.JsonOptions), Entropy, DataProtectionScope.CurrentUser);
        var tempPath = filePath + ".tmp";
        File.WriteAllBytes(tempPath, bytes);
        File.Move(tempPath, filePath, overwrite: true);
    }
}
