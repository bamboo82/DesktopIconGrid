using DesktopIconGrid.Core.Health;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Reading a shortcut's health check.</summary>
public class HealthCheckTests
{
    [Theory]
    [InlineData("ping nas.local", HealthCheckKind.Ping, "nas.local", 0, "ping nas.local")]
    [InlineData("  PING 192.168.1.1 ", HealthCheckKind.Ping, "192.168.1.1", 0, "ping 192.168.1.1")]
    [InlineData("tcp nas.local:445", HealthCheckKind.Tcp, "nas.local", 445, "tcp nas.local:445")]
    [InlineData("tcp [::1]:8080", HealthCheckKind.Tcp, "::1", 8080, "tcp [::1]:8080")]
    [InlineData("https://example.com/health", HealthCheckKind.Http, "example.com", 443, "https://example.com/health")]
    [InlineData("http://10.0.0.5:8080/", HealthCheckKind.Http, "10.0.0.5", 8080, "http://10.0.0.5:8080/")]
    public void A_check_is_read_and_written_back_the_same_way(string text, HealthCheckKind kind, string host, int port, string written)
    {
        var check = HealthCheck.Parse(text, out var error);

        Assert.Null(error);
        Assert.NotNull(check);
        Assert.Equal((kind, host, port), (check.Kind, check.Host, check.Port));
        Assert.Equal(written, check.ToString());
        Assert.Equal(check, HealthCheck.Parse(check.ToString(), out _));
    }

    [Theory]
    [InlineData("ping")]
    [InlineData("ping -t host")] // an option, not a host
    [InlineData("ping a b")]
    [InlineData("tcp host")]
    [InlineData("tcp host:0")]
    [InlineData("tcp host:70000")]
    [InlineData("tcp ::1:80")] // IPv6 needs brackets
    [InlineData("ftp://example.com")]
    [InlineData("https://")]
    [InlineData("curl example.com")]
    [InlineData("ping host\nrm -rf")]
    public void A_check_that_is_not_understood_says_why(string text)
    {
        Assert.Null(HealthCheck.Parse(text, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void No_text_is_no_check_and_the_interval_stays_in_range()
    {
        Assert.Null(HealthCheck.Parse("  ", out var error));
        Assert.Null(error);
        Assert.Equal(HealthCheck.DefaultIntervalSeconds, HealthCheck.NormalizeInterval(null));
        Assert.Equal(HealthCheck.MinIntervalSeconds, HealthCheck.NormalizeInterval(1));
        Assert.Equal(HealthCheck.MaxIntervalSeconds, HealthCheck.NormalizeInterval(99999));
    }

    [Fact]
    public void An_address_is_shown_without_its_path_query_or_user()
    {
        var check = HealthCheck.Parse("https://admin:secret@status.example.com:8443/health?token=abc", out _)!;

        Assert.Equal("https://status.example.com:8443/…", check.Shown);
        Assert.DoesNotContain("secret", check.Shown);
        Assert.Equal("tcp nas:445", HealthCheck.Parse("tcp nas:445", out _)!.Shown);
    }

    [Fact]
    public void The_approval_covers_what_is_contacted_and_how_often_however_it_is_spelled()
    {
        var check = HealthCheck.Parse("tcp nas:445", out _)!;

        Assert.Equal(check.Hash(null), HealthCheck.Parse("  TCP nas:445 ", out _)!.Hash(60)); // the same check, the default interval
        Assert.NotEqual(check.Hash(60), check.Hash(30));
        Assert.NotEqual(check.Hash(60), HealthCheck.Parse("tcp nas:446", out _)!.Hash(60));
        Assert.NotEqual(check.Hash(60), HealthCheck.Parse("ping nas", out _)!.Hash(60));
        Assert.Null(HealthCheck.HashOf(new Models.ShortcutItem { HealthCheck = "nonsense" }));
        Assert.Equal(check.Hash(90), HealthCheck.HashOf(new Models.ShortcutItem { HealthCheck = "tcp nas:445", HealthInterval = 90 }));
    }

    [Theory]
    [InlineData("http://h/%E2%80%AEgpj.exe")] // unescaped by Uri.ToString() into a real right-to-left override
    [InlineData("http://h/a%E2%80%8Bb")] // a zero-width space
    public void An_address_that_would_read_differently_is_refused(string text)
    {
        Assert.Null(HealthCheck.Parse(text, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void The_contacted_host_is_shown_without_user_info_and_in_its_DNS_form()
    {
        Assert.Equal("evil.example:80", HealthCheck.Parse("http://good.example:pw@evil.example/", out _)!.ContactedHost);
        Assert.Equal("xn--bcher-kva.example:443", HealthCheck.Parse("https://bücher.example/", out _)!.ContactedHost);
        Assert.Equal("xn--bcher-kva.example:445", HealthCheck.Parse("tcp bücher.example:445", out _)!.ContactedHost);
        Assert.Equal("[::1]:8080", HealthCheck.Parse("tcp [::1]:8080", out _)!.ContactedHost);
        Assert.Equal("nas", HealthCheck.Parse("ping nas", out _)!.ContactedHost);
    }
}
