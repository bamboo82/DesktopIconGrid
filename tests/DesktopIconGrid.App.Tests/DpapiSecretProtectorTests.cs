using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

public class DpapiSecretProtectorTests
{
    [Fact]
    public void Secrets_round_trip_and_are_not_stored_in_clear()
    {
        var protector = new DpapiSecretProtector();

        var sealed_ = protector.Protect("GOCSPX-example-secret");

        Assert.DoesNotContain("example", sealed_);
        Assert.Equal("GOCSPX-example-secret", protector.Unprotect(sealed_));
    }

    [Fact]
    public void Garbage_cannot_be_unprotected()
    {
        var protector = new DpapiSecretProtector();

        Assert.Null(protector.Unprotect("not base64"));
        Assert.Null(protector.Unprotect(Convert.ToBase64String([1, 2, 3, 4])));
    }
}
