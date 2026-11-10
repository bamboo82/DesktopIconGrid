using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DesktopIconGrid.Core.Health;

namespace DesktopIconGrid.Services;

/// <summary>The answer to one health check.</summary>
/// <param name="Detail">Why it is down (an error, a status code), or <see langword="null"/>.</param>
public sealed record HealthResult(HealthStatus Status, TimeSpan? Latency, string? Detail);

/// <summary>
/// Runs a <see cref="HealthCheck"/>: a ping, a TCP connection, or a web request whose answer must be 2xx or 3xx. None of
/// them needs administrator rights. Each is limited to <see cref="Timeout"/>.
/// </summary>
public static class HealthProbe
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false, // each check stands alone
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = Timeout,
    })
    {
        Timeout = Timeout + Timeout,
        DefaultRequestHeaders = { { "User-Agent", "DesktopIconGrid-HealthCheck" } },
    };

    /// <summary>The longest reason kept (a server's reason phrase or an exception message can be anything).</summary>
    public const int MaxDetailLength = 200;

    public static async Task<HealthResult> CheckAsync(HealthCheck check, CancellationToken stop)
    {
        var result = await CheckCoreAsync(check, stop).ConfigureAwait(false);
        return result.Detail is { Length: > MaxDetailLength } detail ? result with { Detail = detail[..(MaxDetailLength - 1)] + "…" } : result;
    }

    private static async Task<HealthResult> CheckCoreAsync(HealthCheck check, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(check);
        var watch = Stopwatch.StartNew();
        try
        {
            switch (check.Kind)
            {
                case HealthCheckKind.Ping:
                    using (var ping = new Ping())
                    {
                        var reply = await ping.SendPingAsync(check.Host, Timeout, cancellationToken: stop).ConfigureAwait(false);
                        return reply.Status == IPStatus.Success
                            ? new HealthResult(HealthStatus.Up, TimeSpan.FromMilliseconds(reply.RoundtripTime), null)
                            : new HealthResult(HealthStatus.Down, null, reply.Status.ToString());
                    }

                case HealthCheckKind.Tcp:
                    using (var client = new TcpClient())
                    using (var limit = CancellationTokenSource.CreateLinkedTokenSource(stop))
                    {
                        limit.CancelAfter(Timeout);
                        await client.ConnectAsync(check.Host, check.Port, limit.Token).ConfigureAwait(false);
                        return new HealthResult(HealthStatus.Up, watch.Elapsed, null);
                    }

                default:
                    using (var request = new HttpRequestMessage(HttpMethod.Get, check.Url))
                    using (var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop).ConfigureAwait(false))
                    {
                        var code = (int)response.StatusCode;
                        return code is >= 200 and < 400
                            ? new HealthResult(HealthStatus.Up, watch.Elapsed, null)
                            : new HealthResult(HealthStatus.Down, null, $"HTTP {code} {response.ReasonPhrase}".Trim());
                    }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new HealthResult(HealthStatus.Down, null, "no answer in time");
        }
        catch (Exception ex) when (ex is PingException or SocketException or HttpRequestException or InvalidOperationException or ArgumentException)
        {
            return new HealthResult(HealthStatus.Down, null, (ex.InnerException ?? ex).Message);
        }
    }
}
