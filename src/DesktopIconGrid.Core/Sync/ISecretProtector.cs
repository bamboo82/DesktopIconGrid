namespace DesktopIconGrid.Core.Sync;

/// <summary>
/// Encrypts small secrets for storage on disk. The app supplies Windows DPAPI (current user); the core library
/// stays platform-neutral and tests use a reversible fake.
/// </summary>
public interface ISecretProtector
{
    /// <summary>An opaque string that only this user on this machine can turn back into the secret.</summary>
    string Protect(string secret);

    /// <summary>The secret, or <see langword="null"/> when the value cannot be decrypted (another user or machine, or corrupted).</summary>
    string? Unprotect(string protectedValue);
}
