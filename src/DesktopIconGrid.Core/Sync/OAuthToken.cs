namespace DesktopIconGrid.Core.Sync;

/// <summary>Tokens returned by Google's token endpoint. Persisted through an <see cref="ITokenStore"/>.</summary>
public sealed record OAuthToken(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt, string? Scope)
{
    public bool IsExpiringWithin(TimeSpan margin, DateTimeOffset now) => ExpiresAt - now <= margin;
}

/// <summary>Persists the OAuth token somewhere appropriate for the platform (encrypted on Windows).</summary>
public interface ITokenStore
{
    OAuthToken? Load();

    /// <summary>Saves the token, or removes the stored token when <paramref name="token"/> is <see langword="null"/>.</summary>
    void Save(OAuthToken? token);
}

/// <summary>Keeps the token in memory only; used by tests.</summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    public OAuthToken? Token { get; set; }

    public OAuthToken? Load() => Token;

    public void Save(OAuthToken? token) => Token = token;
}
