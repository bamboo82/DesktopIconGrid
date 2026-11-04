using System.Security.Cryptography;
using System.Text;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Services;

/// <summary>Protects the Google client secret with DPAPI for the current Windows user (Base64 of the ciphertext).</summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DesktopIconGrid.ClientSecret.v1");

    public string Protect(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser));
    }

    public string? Unprotect(string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
