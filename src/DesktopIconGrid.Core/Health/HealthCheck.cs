using System.Globalization;

namespace DesktopIconGrid.Core.Health;

/// <summary>How a shortcut's health is checked.</summary>
public enum HealthCheckKind
{
    /// <summary><c>ping host</c>: an ICMP echo (often blocked by firewalls; <see cref="Tcp"/> is the safer choice).</summary>
    Ping,

    /// <summary><c>tcp host:port</c>: a TCP connection that is accepted.</summary>
    Tcp,

    /// <summary><c>http(s)://…</c>: an answer of 2xx or 3xx (redirects are not followed).</summary>
    Http,
}

/// <summary>Up, down, or not known yet.</summary>
public enum HealthStatus
{
    Unknown,
    Up,
    Down,
}

/// <summary>
/// A shortcut's <see cref="Models.ShortcutItem.HealthCheck"/>, read: <c>ping host</c>, <c>tcp host:port</c> or an
/// <c>http://</c> or <c>https://</c> address. A host is a DNS name or an IP address (IPv6 in brackets with a port).
/// </summary>
public sealed record HealthCheck(HealthCheckKind Kind, string Host, int Port, Uri? Url)
{
    public const int MinIntervalSeconds = 30;

    public const int MaxIntervalSeconds = 10 * 60;

    public const int DefaultIntervalSeconds = 60;

    public const int MaxLength = 2048;

    /// <summary>Between <see cref="MinIntervalSeconds"/> and <see cref="MaxIntervalSeconds"/>; <see cref="DefaultIntervalSeconds"/> when not set.</summary>
    public static int NormalizeInterval(int? seconds) =>
        seconds is { } value ? Math.Clamp(value, MinIntervalSeconds, MaxIntervalSeconds) : DefaultIntervalSeconds;

    /// <summary>The check in <paramref name="text"/>, or <see langword="null"/> with why not (null error for an empty text: no check).</summary>
    public static HealthCheck? Parse(string? text, out string? error)
    {
        error = null;
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > MaxLength || trimmed.Any(char.IsControl))
        {
            error = "too long, or holds a line break";
            return null;
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && url.Host.Length > 0)
            {
                return new HealthCheck(HealthCheckKind.Http, url.Host, url.Port, url);
            }

            error = "not a web address";
            return null;
        }

        var space = trimmed.IndexOf(' ');
        var verb = space < 0 ? trimmed : trimmed[..space];
        var target = space < 0 ? string.Empty : trimmed[(space + 1)..].Trim();
        switch (verb.ToLowerInvariant())
        {
            case "ping":
                if (IsHost(target))
                {
                    return new HealthCheck(HealthCheckKind.Ping, target.Trim('[', ']'), 0, null);
                }

                error = "ping needs a host name or address";
                return null;
            case "tcp":
                if (SplitHostPort(target) is { } endpoint)
                {
                    return new HealthCheck(HealthCheckKind.Tcp, endpoint.Host, endpoint.Port, null);
                }

                error = "tcp needs host:port";
                return null;
            default:
                error = "use ping <host>, tcp <host>:<port>, or an http:// or https:// address";
                return null;
        }
    }

    /// <summary>
    /// The check as the log, notifications and tooltips show it: an address by its scheme, host and port only, since
    /// health addresses often carry a token or a user name and password.
    /// </summary>
    public string Shown => Kind == HealthCheckKind.Http
        ? $"{Url!.Scheme}://{Url.Authority[(Url.Authority.LastIndexOf('@') + 1)..]}/…"
        : ToString();

    /// <summary>The check as the user writes it.</summary>
    public override string ToString() => Kind switch
    {
        HealthCheckKind.Http => Url!.ToString(),
        HealthCheckKind.Tcp => $"tcp {(Host.Contains(':') ? $"[{Host}]" : Host)}:{Port.ToString(CultureInfo.InvariantCulture)}",
        _ => $"ping {Host}",
    };

    private static (string Host, int Port)? SplitHostPort(string text)
    {
        string host, port;
        if (text.StartsWith('['))
        {
            var close = text.IndexOf("]:", StringComparison.Ordinal);
            if (close < 0)
            {
                return null;
            }

            host = text[1..close];
            port = text[(close + 2)..];
        }
        else
        {
            var colon = text.LastIndexOf(':');
            if (colon <= 0 || text.IndexOf(':') != colon)
            {
                return null;
            }

            host = text[..colon];
            port = text[(colon + 1)..];
        }

        return IsHost(host) && int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is > 0 and <= 65535
            ? (host, number)
            : null;
    }

    private static bool IsHost(string text)
    {
        var host = text.Trim('[', ']');
        return host.Length > 0 && !host.StartsWith('-') && Uri.CheckHostName(host) is not UriHostNameType.Unknown and not UriHostNameType.Basic;
    }
}
