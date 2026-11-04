using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace DesktopIconGrid.Core.Sync;

/// <summary>Proof Key for Code Exchange helpers (RFC 7636) and random state values.</summary>
public static class Pkce
{
    /// <summary>A 43-character, high-entropy code verifier.</summary>
    public static string CreateVerifier() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>The S256 challenge for a verifier.</summary>
    public static string ComputeChallenge(string verifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(verifier);
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    public static string CreateState() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
}
