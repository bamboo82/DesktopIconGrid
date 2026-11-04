using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DesktopIconGrid.Core.Sync;

/// <summary>Supplies bearer tokens to API clients.</summary>
public interface IAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);

    /// <summary>Forces the next <see cref="GetAccessTokenAsync"/> to refresh (after a 401).</summary>
    void InvalidateAccessToken();
}

public sealed class GoogleAuthException(string message) : Exception(message);

/// <summary>
/// Google's OAuth 2.0 flow for installed apps: the browser signs in and is redirected to a loopback
/// address the app listens on, the code is exchanged (with PKCE) for tokens, and the refresh token
/// keeps the session alive across runs.
/// </summary>
public sealed class GoogleAuthorization : IAccessTokenProvider
{
    public const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

    /// <summary>Files the app creates in Drive, nothing else.</summary>
    public static readonly IReadOnlyList<string> Scopes = ["https://www.googleapis.com/auth/drive.file"];

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly ITokenStore _store;
    private readonly Func<OAuthClientCredentials> _credentials;
    private readonly Func<string, Task> _openBrowser;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private OAuthToken? _token;

    public GoogleAuthorization(
        HttpClient http,
        ITokenStore store,
        Func<OAuthClientCredentials> credentials,
        Func<string, Task>? openBrowser = null,
        Func<DateTimeOffset>? clock = null)
    {
        _http = http;
        _store = store;
        _credentials = credentials;
        _openBrowser = openBrowser ?? OpenInDefaultBrowser;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _token = store.Load();
    }

    /// <summary>True when a refresh token is on file, i.e. the user has connected and not disconnected.</summary>
    public bool IsAuthorized => _token?.RefreshToken is not null;

    public static string BuildAuthorizationUrl(OAuthClientCredentials credentials, string redirectUri, string codeChallenge, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = credentials.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', Scopes),
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
        };

        var builder = new StringBuilder(AuthorizationEndpoint).Append('?');
        var first = true;
        foreach (var (key, value) in query)
        {
            if (!first)
            {
                builder.Append('&');
            }

            first = false;
            builder.Append(key).Append('=').Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }

    /// <summary>Runs the interactive sign-in. Throws <see cref="GoogleAuthException"/> when it fails or is cancelled.</summary>
    public async Task AuthorizeAsync(CancellationToken cancellationToken)
    {
        var credentials = _credentials();
        if (!credentials.IsComplete)
        {
            throw new GoogleAuthException("Enter the Google API client ID and client secret first.");
        }

        var verifier = Pkce.CreateVerifier();
        var challenge = Pkce.ComputeChallenge(verifier);
        var state = Pkce.CreateState();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SignInTimeout);

        using var listener = new LoopbackListener();
        var url = BuildAuthorizationUrl(credentials, listener.RedirectUri, challenge, state);
        await _openBrowser(url).ConfigureAwait(false);

        string code;
        try
        {
            code = await listener.WaitForCodeAsync(state, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GoogleAuthException("The sign-in timed out. Try again and finish signing in within five minutes.");
        }

        var token = await ExchangeCodeAsync(credentials, code, listener.RedirectUri, verifier, cancellationToken).ConfigureAwait(false);
        if (token.RefreshToken is null)
        {
            throw new GoogleAuthException("Google did not return a refresh token. Remove the app's access at myaccount.google.com/permissions and connect again.");
        }

        _token = token;
        _store.Save(token);
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var token = _token ?? throw new GoogleAuthException("Not connected to Google Drive.");
        if (!token.IsExpiringWithin(RefreshMargin, _clock()))
        {
            return token.AccessToken;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            token = _token ?? throw new GoogleAuthException("Not connected to Google Drive.");
            if (!token.IsExpiringWithin(RefreshMargin, _clock()))
            {
                return token.AccessToken;
            }

            if (token.RefreshToken is null)
            {
                throw new GoogleAuthException("The Google sign-in has expired. Connect again.");
            }

            var refreshed = await RefreshAsync(_credentials(), token.RefreshToken, cancellationToken).ConfigureAwait(false);
            _token = refreshed with { RefreshToken = refreshed.RefreshToken ?? token.RefreshToken };
            _store.Save(_token);
            return _token.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void InvalidateAccessToken()
    {
        if (_token is not null)
        {
            _token = _token with { ExpiresAt = DateTimeOffset.MinValue };
        }
    }

    /// <summary>Forgets the stored tokens and asks Google to revoke them (best effort).</summary>
    public async Task RevokeAsync(CancellationToken cancellationToken)
    {
        var token = _token;
        _token = null;
        _store.Save(null);

        if (token is null)
        {
            return;
        }

        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = token.RefreshToken ?? token.AccessToken,
            });
            using var response = await _http.PostAsync(RevokeEndpoint, content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Offline or cancelled: the local copy is gone, which is what matters here.
        }
    }

    internal Task<OAuthToken> ExchangeCodeAsync(OAuthClientCredentials credentials, string code, string redirectUri, string verifier, CancellationToken cancellationToken) =>
        PostTokenRequestAsync(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = credentials.ClientId,
            ["client_secret"] = credentials.ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier,
        }, cancellationToken);

    internal Task<OAuthToken> RefreshAsync(OAuthClientCredentials credentials, string refreshToken, CancellationToken cancellationToken) =>
        PostTokenRequestAsync(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = credentials.ClientId,
            ["client_secret"] = credentials.ClientSecret,
            ["grant_type"] = "refresh_token",
        }, cancellationToken);

    internal static OAuthToken ParseTokenResponse(string json, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("access_token", out var accessToken) || accessToken.ValueKind != JsonValueKind.String)
        {
            throw new GoogleAuthException("Google's response did not contain an access token.");
        }

        var expiresIn = root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds) ? seconds : 3600;
        var refreshToken = root.TryGetProperty("refresh_token", out var refresh) && refresh.ValueKind == JsonValueKind.String ? refresh.GetString() : null;
        var scope = root.TryGetProperty("scope", out var scopeElement) && scopeElement.ValueKind == JsonValueKind.String ? scopeElement.GetString() : null;
        return new OAuthToken(accessToken.GetString()!, refreshToken, now.AddSeconds(expiresIn), scope);
    }

    private async Task<OAuthToken> PostTokenRequestAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new GoogleAuthException(DescribeTokenError(body, response.StatusCode));
        }

        try
        {
            return ParseTokenResponse(body, _clock());
        }
        catch (JsonException)
        {
            throw new GoogleAuthException("Google's response could not be read.");
        }
    }

    private static string DescribeTokenError(string body, HttpStatusCode status)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            var description = root.TryGetProperty("error_description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            if (error is not null)
            {
                return error switch
                {
                    "invalid_grant" => "The Google sign-in is no longer valid. Connect again.",
                    "invalid_client" => "Google rejected the client ID or client secret.",
                    _ => description is null ? $"Google sign-in failed ({error})." : $"{description} ({error})",
                };
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return $"Google sign-in failed (HTTP {(int)status}).";
    }

    private static Task OpenInDefaultBrowser(string url)
    {
        using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    /// <summary>Listens on a random loopback port for the browser redirect that carries the authorization code.</summary>
    private sealed class LoopbackListener : IDisposable
    {
        private const string SuccessPage =
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>Desktop Icon Grid</title></head>" +
            "<body style=\"font-family:Segoe UI,sans-serif;margin:48px\"><h2>Connected to Google Drive</h2>" +
            "<p>You can close this window and return to Desktop Icon Grid.</p></body></html>";

        private const string FailurePage =
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>Desktop Icon Grid</title></head>" +
            "<body style=\"font-family:Segoe UI,sans-serif;margin:48px\"><h2>Sign-in did not complete</h2>" +
            "<p>Return to Desktop Icon Grid and try again.</p></body></html>";

        private readonly HttpListener _listener;

        public LoopbackListener()
        {
            var port = GetFreePort();
            RedirectUri = $"http://127.0.0.1:{port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(RedirectUri);
            _listener.Start();
        }

        public string RedirectUri { get; }

        public async Task<string> WaitForCodeAsync(string expectedState, CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() => _listener.Stop());
            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new GoogleAuthException("The sign-in was interrupted before it finished.");
                }

                var query = context.Request.QueryString;
                var code = query["code"];
                var state = query["state"];
                var error = query["error"];

                if (code is null && error is null)
                {
                    await RespondAsync(context, 404, string.Empty).ConfigureAwait(false);
                    continue; // favicon requests and the like
                }

                var ok = error is null && code is not null && state == expectedState;
                await RespondAsync(context, 200, ok ? SuccessPage : FailurePage).ConfigureAwait(false);

                if (error is not null)
                {
                    throw new GoogleAuthException(error == "access_denied"
                        ? "Access was denied in the browser."
                        : $"Google reported an error: {error}.");
                }

                if (state != expectedState)
                {
                    throw new GoogleAuthException("The sign-in response did not match this request. Try again.");
                }

                return code!;
            }
        }

        public void Dispose()
        {
            try
            {
                _listener.Close();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static async Task RespondAsync(HttpListenerContext context, int status, string html)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(html);
                context.Response.StatusCode = status;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
                context.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
            {
                // The browser went away; the code (if any) was still captured.
            }
        }

        private static int GetFreePort()
        {
            var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            try
            {
                return ((IPEndPoint)socket.LocalEndpoint).Port;
            }
            finally
            {
                socket.Stop();
            }
        }
    }
}
