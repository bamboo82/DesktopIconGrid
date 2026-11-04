using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Services;

/// <summary>Stores the Google token encrypted with DPAPI for the current Windows user.</summary>
public sealed class DpapiTokenStore(string filePath) : ITokenStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DesktopIconGrid.GoogleToken.v1");

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
