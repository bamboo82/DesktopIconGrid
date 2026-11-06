using System.Security.Cryptography;
using System.Text;

namespace DesktopIconGrid.Core.Models;

/// <summary>
/// A key for this PC and Windows user, worked out the same way at every start and never written to the PC itself:
/// Windows' machine id (<c>HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid</c>) and the user's SID, hashed. Per-PC
/// locations are kept under it (<see cref="ThisDevice"/>), so they belong to the PC, not to the data folder: a portable
/// copy on a stick, a <c>sync.json</c> that could not be read, or data copied to another PC cannot carry them along.
/// </summary>
public static class MachineKey
{
    private const string Prefix = "pc-";

    /// <summary>The key, or <see langword="null"/> when the machine id or the SID is not known.</summary>
    public static string? From(string? machineGuid, string? userSid)
    {
        if (string.IsNullOrWhiteSpace(machineGuid) || string.IsNullOrWhiteSpace(userSid))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"DesktopIconGrid|{machineGuid.Trim().ToLowerInvariant()}|{userSid.Trim().ToUpperInvariant()}"));
        return Prefix + Convert.ToHexStringLower(hash, 0, 12);
    }
}
