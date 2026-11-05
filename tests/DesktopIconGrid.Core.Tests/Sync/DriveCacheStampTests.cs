using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests.Sync;

public class DriveCacheStampTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static DriveEntry File(long? size = 10, DateTimeOffset? modified = null, string? md5 = null) =>
        new("id", "a.txt", "text/plain", size, modified ?? Noon, md5);

    [Fact]
    public void The_checksum_decides_when_both_sides_have_one()
    {
        var stamp = DriveCacheStamp.Of(File(md5: "abc"));

        Assert.True(stamp.IsFreshFor(File(modified: Noon.AddHours(1), md5: "ABC"))); // a touch that changed nothing
        Assert.False(stamp.IsFreshFor(File(md5: "def")));
        Assert.False(stamp.IsFreshFor(File(size: 11, md5: "abc"))); // a checksum collision with another size is not trusted
    }

    [Fact]
    public void Without_checksums_the_size_and_the_modification_time_must_both_match()
    {
        var stamp = DriveCacheStamp.Of(File());

        Assert.True(stamp.IsFreshFor(File()));
        Assert.False(stamp.IsFreshFor(File(modified: Noon.AddSeconds(1))));
        Assert.False(stamp.IsFreshFor(File(size: 11)));
        Assert.False(stamp.IsFreshFor(File(size: null)));
        var undated = new DriveEntry("id", "a.txt", "text/plain", 10, null);
        Assert.False(DriveCacheStamp.Of(undated).IsFreshFor(undated));
    }

    [Fact]
    public void A_stamp_from_before_checksums_still_compares_by_size_and_time()
    {
        var stamp = new DriveCacheStamp(10, Noon, null);

        Assert.True(stamp.IsFreshFor(File(md5: "abc")));
        Assert.False(stamp.IsFreshFor(File(modified: Noon.AddMinutes(1), md5: "abc")));
    }

    [Fact]
    public void Folders_and_Google_formats_are_never_fresh()
    {
        var stamp = new DriveCacheStamp(null, Noon, null);

        Assert.False(stamp.IsFreshFor(new DriveEntry("f", "Docs", DriveEntry.FolderMimeType, null, Noon)));
        Assert.False(stamp.IsFreshFor(new DriveEntry("d", "Plan", "application/vnd.google-apps.document", null, Noon)));
    }

    [Fact]
    public void Stamps_round_trip_through_json_and_garbage_is_ignored()
    {
        var stamp = DriveCacheStamp.Of(File(md5: "abc"));

        Assert.Equal(stamp, DriveCacheStamp.TryParse(stamp.Serialize()));
        Assert.Null(DriveCacheStamp.TryParse("not json"));
        Assert.Null(DriveCacheStamp.TryParse(string.Empty));
        Assert.Null(DriveCacheStamp.TryParse(null));
    }
}
