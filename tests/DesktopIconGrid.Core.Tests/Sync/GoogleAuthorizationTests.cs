using System.Net;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests.Sync;

public class GoogleAuthorizationTests
{
    private static readonly OAuthClientCredentials Credentials = new("client-id.apps.googleusercontent.com", "secret");
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Pkce_challenge_matches_rfc7636_test_vector()
    {
        var challenge = Pkce.ComputeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk");

        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", challenge);
    }

    [Fact]
    public void Pkce_verifier_is_url_safe_and_long_enough()
    {
        var verifier = Pkce.CreateVerifier();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", verifier);
        Assert.NotEqual(verifier, Pkce.CreateVerifier());
    }

    [Fact]
    public void Authorization_url_contains_required_parameters()
    {
        var url = GoogleAuthorization.BuildAuthorizationUrl(Credentials, "http://127.0.0.1:5000/", "challenge", "state123");

        Assert.StartsWith(GoogleAuthorization.AuthorizationEndpoint + "?", url);
        Assert.Contains("client_id=client-id.apps.googleusercontent.com", url);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A5000%2F", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fdrive.file", url);
        Assert.Contains("code_challenge=challenge", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("state=state123", url);
        Assert.Contains("access_type=offline", url);
        Assert.Contains("prompt=consent", url);
    }

    [Fact]
    public void Extra_scopes_are_added_to_the_url_and_granted_scopes_are_remembered()
    {
        var url = GoogleAuthorization.BuildAuthorizationUrl(Credentials, "http://127.0.0.1:5000/", "c", "s", [GoogleAuthorization.Scopes[0], GoogleAuthorization.FullDriveScope, GoogleAuthorization.Scopes[0]]);
        Assert.Contains("scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fdrive.file%20https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fdrive&", url);
        Assert.Contains("include_granted_scopes=true", url);

        var store = new InMemoryTokenStore { Token = new OAuthToken("at", "rt", Now.AddHours(1), "https://www.googleapis.com/auth/drive.file https://www.googleapis.com/auth/drive") };
        var auth = new GoogleAuthorization(new HttpClient(new FakeHttpHandler()), store, () => Credentials, clock: () => Now);
        Assert.True(auth.HasScope(GoogleAuthorization.FullDriveScope));
        Assert.Equal(2, auth.GrantedScopes.Count);

        var limited = new GoogleAuthorization(new HttpClient(new FakeHttpHandler()), new InMemoryTokenStore { Token = new OAuthToken("at", "rt", Now.AddHours(1), null) }, () => Credentials, clock: () => Now);
        Assert.False(limited.HasScope(GoogleAuthorization.FullDriveScope));
    }

    [Fact]
    public async Task Refresh_keeps_the_granted_scopes_when_the_response_omits_them()
    {
        var handler = new FakeHttpHandler().Enqueue(HttpStatusCode.OK, """{"access_token":"new","expires_in":3600}""");
        var store = new InMemoryTokenStore { Token = new OAuthToken("old", "rt", Now, "https://www.googleapis.com/auth/drive") };
        var auth = new GoogleAuthorization(new HttpClient(handler), store, () => Credentials, clock: () => Now);

        await auth.GetAccessTokenAsync(CancellationToken.None);

        Assert.True(auth.HasScope("https://www.googleapis.com/auth/drive"));
    }

    [Fact]
    public void Token_response_is_parsed()
    {
        var token = GoogleAuthorization.ParseTokenResponse(
            """{"access_token":"at","expires_in":3599,"refresh_token":"rt","scope":"https://www.googleapis.com/auth/drive.file","token_type":"Bearer"}""",
            Now);

        Assert.Equal("at", token.AccessToken);
        Assert.Equal("rt", token.RefreshToken);
        Assert.Equal(Now.AddSeconds(3599), token.ExpiresAt);
        Assert.False(token.IsExpiringWithin(TimeSpan.FromMinutes(1), Now));
        Assert.True(token.IsExpiringWithin(TimeSpan.FromMinutes(1), Now.AddSeconds(3560)));
    }

    [Fact]
    public void Token_response_without_access_token_is_rejected()
    {
        Assert.Throws<GoogleAuthException>(() => GoogleAuthorization.ParseTokenResponse("""{"token_type":"Bearer"}""", Now));
    }

    [Fact]
    public async Task Fresh_access_token_is_returned_without_network()
    {
        var handler = new FakeHttpHandler();
        var store = new InMemoryTokenStore { Token = new OAuthToken("at", "rt", Now.AddHours(1), null) };
        var auth = new GoogleAuthorization(new HttpClient(handler), store, () => Credentials, clock: () => Now);

        Assert.True(auth.IsAuthorized);
        Assert.Equal("at", await auth.GetAccessTokenAsync(CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Expiring_access_token_is_refreshed_and_persisted()
    {
        var handler = new FakeHttpHandler().Enqueue(HttpStatusCode.OK, """{"access_token":"new","expires_in":3600}""");
        var store = new InMemoryTokenStore { Token = new OAuthToken("old", "rt", Now.AddSeconds(30), null) };
        var auth = new GoogleAuthorization(new HttpClient(handler), store, () => Credentials, clock: () => Now);

        var accessToken = await auth.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("new", accessToken);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(GoogleAuthorization.TokenEndpoint, request.Url);
        Assert.Contains("grant_type=refresh_token", request.Body);
        Assert.Contains("refresh_token=rt", request.Body);
        Assert.Contains("client_secret=secret", request.Body);
        Assert.Equal("new", store.Token!.AccessToken);
        Assert.Equal("rt", store.Token.RefreshToken); // kept when the refresh response omits it
    }

    [Fact]
    public async Task Invalidated_token_is_refreshed_on_next_use()
    {
        var handler = new FakeHttpHandler().Enqueue(HttpStatusCode.OK, """{"access_token":"new","expires_in":3600}""");
        var store = new InMemoryTokenStore { Token = new OAuthToken("old", "rt", Now.AddHours(1), null) };
        var auth = new GoogleAuthorization(new HttpClient(handler), store, () => Credentials, clock: () => Now);

        auth.InvalidateAccessToken();

        Assert.Equal("new", await auth.GetAccessTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Revoked_refresh_token_surfaces_a_friendly_error()
    {
        var handler = new FakeHttpHandler().Enqueue(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""");
        var store = new InMemoryTokenStore { Token = new OAuthToken("old", "rt", Now, null) };
        var auth = new GoogleAuthorization(new HttpClient(handler), store, () => Credentials, clock: () => Now);

        var ex = await Assert.ThrowsAsync<GoogleAuthException>(() => auth.GetAccessTokenAsync(CancellationToken.None));

        Assert.Contains("Connect again", ex.Message);
    }

    [Fact]
    public async Task Not_connected_is_reported()
    {
        var auth = new GoogleAuthorization(new HttpClient(new FakeHttpHandler()), new InMemoryTokenStore(), () => Credentials, clock: () => Now);

        Assert.False(auth.IsAuthorized);
        await Assert.ThrowsAsync<GoogleAuthException>(() => auth.GetAccessTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Authorize_requires_credentials()
    {
        var auth = new GoogleAuthorization(new HttpClient(new FakeHttpHandler()), new InMemoryTokenStore(), () => OAuthClientCredentials.Empty);

        await Assert.ThrowsAsync<GoogleAuthException>(() => auth.AuthorizeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Revoke_clears_the_store_even_when_google_is_unreachable()
    {
        var handler = new FakeHttpHandler().Enqueue(_ => throw new HttpRequestException("offline"));
        var store = new InMemoryTokenStore { Token = new OAuthToken("at", "rt", Now.AddHours(1), null) };
        var auth = new GoogleAuthorization(new HttpClient(handler), store, () => Credentials, clock: () => Now);

        await auth.RevokeAsync(CancellationToken.None);

        Assert.Null(store.Token);
        Assert.False(auth.IsAuthorized);
        Assert.Contains("token=rt", Assert.Single(handler.Requests).Body);
    }

    [Theory]
    [InlineData("""{"installed":{"client_id":" id ","client_secret":"s","redirect_uris":["http://localhost"]}}""", true, "id")]
    [InlineData("""{"web":{"client_id":"id","client_secret":"s"}}""", true, "id")]
    [InlineData("""{"installed":{"client_id":"id"}}""", false, "")]
    [InlineData("not json", false, "")]
    [InlineData("", false, "")]
    public void Client_secret_json_is_parsed(string json, bool expected, string expectedId)
    {
        var ok = OAuthClientCredentials.TryParseClientSecretJson(json, out var credentials);

        Assert.Equal(expected, ok);
        if (expected)
        {
            Assert.Equal(expectedId, credentials.ClientId);
            Assert.True(credentials.IsComplete);
        }
    }
}
