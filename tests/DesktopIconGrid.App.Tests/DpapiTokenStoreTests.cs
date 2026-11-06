using System.IO;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

/// <summary>The Google sign-in's file per PC in portable mode, and the shared file of 0.8.0.</summary>
public sealed class DpapiTokenStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-token-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static OAuthToken Token() => new("at", "rt", DateTimeOffset.UtcNow.AddHours(1), null);

    [Fact]
    public void The_shared_file_becomes_this_PCs_own_once_and_a_disconnect_stays_done()
    {
        // Review 15 U4: the shared file was copied whenever this PC's own was missing, so after a Disconnect (which
        // deletes it) the next start signed this PC in again with the old token.
        var shared = Path.Combine(_directory, "google-token.bin");
        new DpapiTokenStore(shared).Save(Token()); // written on this PC by 0.8.0

        var own = DpapiTokenStore.FileForThisPc(_directory, "pc1", portable: true);

        Assert.Equal(Path.Combine(_directory, "google-token-pc1.bin"), own);
        Assert.False(File.Exists(shared)); // moved: no other PC could read it
        Assert.Equal("rt", new DpapiTokenStore(own).Load()?.RefreshToken);

        new DpapiTokenStore(own).Save(null); // Disconnect
        Assert.Equal(own, DpapiTokenStore.FileForThisPc(_directory, "pc1", portable: true));
        Assert.Null(new DpapiTokenStore(own).Load()); // still signed out at the next start
    }

    [Fact]
    public void Another_PCs_shared_file_is_left_for_that_PC()
    {
        var shared = Path.Combine(_directory, "google-token.bin");
        File.WriteAllBytes(shared, [1, 2, 3]); // what this PC sees of another PC's DPAPI file: it does not decrypt

        var own = DpapiTokenStore.FileForThisPc(_directory, "pc2", portable: true);

        Assert.False(File.Exists(own));
        Assert.True(File.Exists(shared));
        Assert.Equal(shared, DpapiTokenStore.FileForThisPc(_directory, "pc2", portable: false)); // installed: the one file
    }

    [Fact]
    public void The_folder_picker_opens_on_a_local_folder_never_on_a_share()
    {
        // Review 15 U2: with a stored folder on a share, the dialog was left to open where it was last used: that share.
        Assert.Equal(_directory, DialogService.LocalFolderToOpen(_directory));
        foreach (var away in new[] { @"\\nas\copies", Path.Combine(_directory, "gone"), null })
        {
            var opened = DialogService.LocalFolderToOpen(away);
            Assert.False(IconWorker.IsOnSlowStorage(opened), opened);
            Assert.True(Directory.Exists(opened), opened);
        }
    }
}
