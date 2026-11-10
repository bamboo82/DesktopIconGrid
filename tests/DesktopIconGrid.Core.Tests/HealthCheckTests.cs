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
}
