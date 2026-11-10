using System.Net;
using System.Net.Sockets;
using System.Text;
using DesktopIconGrid.Core.Health;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

/// <summary>The health light's checks, against listeners on this machine.</summary>
public class HealthProbeTests
{
    [Fact]
    public async Task Tcp_is_up_while_something_listens_and_down_after()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var check = HealthCheck.Parse($"tcp 127.0.0.1:{port}", out _)!;

        var up = await HealthProbe.CheckAsync(check, CancellationToken.None);
        listener.Stop();
        var down = await HealthProbe.CheckAsync(check, CancellationToken.None);

        Assert.Equal(HealthStatus.Up, up.Status);
        Assert.NotNull(up.Latency);
        Assert.Equal(HealthStatus.Down, down.Status);
        Assert.NotNull(down.Detail);
    }

    [Theory]
    [InlineData("200 OK", HealthStatus.Up)]
    [InlineData("302 Found", HealthStatus.Up)] // not followed: an answer is enough
    [InlineData("503 Service Unavailable", HealthStatus.Down)]
    public async Task Http_is_up_on_2xx_and_3xx(string statusLine, HealthStatus expected)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            _ = await stream.ReadAsync(buffer);
            var answer = $"HTTP/1.1 {statusLine}\r\nLocation: http://127.0.0.1:1/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(answer));
        });

        try
        {
            var result = await HealthProbe.CheckAsync(HealthCheck.Parse($"http://127.0.0.1:{port}/health", out _)!, CancellationToken.None);

            Assert.Equal(expected, result.Status);
            if (expected == HealthStatus.Down)
            {
                Assert.Contains("503", result.Detail);
            }
        }
        finally
        {
            await server.WaitAsync(TimeSpan.FromSeconds(10));
            listener.Stop();
        }
    }
}
