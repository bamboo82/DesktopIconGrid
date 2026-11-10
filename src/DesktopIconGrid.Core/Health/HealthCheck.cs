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

    /// <summary>
    /// The check in <paramref name="text"/>, or <see langword="null"/> with why not (null error for an empty text: no check).
    /// A check that would be written back with an invisible or reordering character (an address can carry one escaped:
    /// <c>Uri.ToString()</c> unescapes <c>%E2%80%AE</c>) is refused, so the approval window reads as what is contacted.
    /// </summary>
    public static HealthCheck? Parse(string? text, out string? error)
    {
        var check = ParseCore(text, out error);
        if (check is not null && !SpecialIcons.CommandTileSettings.IsShowable(check.ToString() + check.Host))
        {
            error = "holds an invisible or reordering character";
            return null;
        }

        return check;
    }

    /// <summary>
    /// The host that is contacted, as DNS sees it: international names in their <c>xn--</c> form, so a look-alike shows
    /// as what it is; user info in an address (<c>http://good.example:pw@evil.example/</c>) is not part of it.
    /// </summary>
    public string ContactedHost
    {
        get
        {
            string host;
            if (Kind == HealthCheckKind.Http)
            {
                host = Url!.IdnHost;
            }
            else
            {
                try
                {
                    host = Host.Contains(':') ? Host : new IdnMapping().GetAscii(Host);
                }
                catch (ArgumentException)
                {
                    host = Host;
                }
            }

            var port = Kind == HealthCheckKind.Ping ? string.Empty : ":" + Port.ToString(CultureInfo.InvariantCulture);
            return host.Contains(':') ? $"[{host}]{port}" : host + port;
        }
    }

    private static HealthCheck? ParseCore(string? text, out string? error)
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

    /// <summary>
    /// What a PC approves (review 21 H1): the check as written back (what is contacted and how) and how often. A change
    /// of either, on any PC, needs approving again; the notification setting is left out, it contacts nothing.
    /// </summary>
    public string Hash(int? intervalSeconds)
    {
        var text = string.Join('\n', "health-check/1", ToString(), NormalizeInterval(intervalSeconds).ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>The <see cref="Hash"/> of a shortcut's check, or <see langword="null"/> when it has none (or none that reads).</summary>
    public static string? HashOf(Models.ShortcutItem shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        return Parse(shortcut.HealthCheck, out _) is { } check ? check.Hash(shortcut.HealthInterval) : null;
    }

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
