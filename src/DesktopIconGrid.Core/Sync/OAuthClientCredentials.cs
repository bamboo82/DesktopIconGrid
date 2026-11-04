using System.Text.Json;

namespace DesktopIconGrid.Core.Sync;

/// <summary>
/// The OAuth client Google issues for a desktop app. Google treats the secret of an installed
/// app as public, so it may be stored with the other local settings.
/// </summary>
public sealed record OAuthClientCredentials(string ClientId, string ClientSecret)
{
    public static readonly OAuthClientCredentials Empty = new(string.Empty, string.Empty);

    public bool IsComplete => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>
    /// Reads the <c>client_secret_*.json</c> file that Google Cloud Console offers for download
    /// (<c>{"installed":{"client_id":…,"client_secret":…}}</c>; <c>"web"</c> is accepted as well).
    /// </summary>
    public static bool TryParseClientSecretJson(string json, out OAuthClientCredentials credentials)
    {
        credentials = Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var sectionName in new[] { "installed", "web" })
            {
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty(sectionName, out var section)
                    && section.ValueKind == JsonValueKind.Object
                    && section.TryGetProperty("client_id", out var id)
                    && section.TryGetProperty("client_secret", out var secret)
                    && id.ValueKind == JsonValueKind.String
                    && secret.ValueKind == JsonValueKind.String)
                {
                    credentials = new OAuthClientCredentials(id.GetString()!.Trim(), secret.GetString()!.Trim());
                    return credentials.IsComplete;
                }
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return false;
    }
}
