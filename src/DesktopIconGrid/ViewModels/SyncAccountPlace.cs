namespace DesktopIconGrid.ViewModels;

/// <summary>Where the sync window shows the Google account's part (0.16.1 WP2).</summary>
public enum SyncAccountPlace
{
    /// <summary>Google Drive keeps the copies: the account's group, directly under the choice of place.</summary>
    Group,

    /// <summary>A folder keeps them, and buckets may need the account: the last line of the window, closed at first.</summary>
    Line,

    /// <summary>A folder keeps them and nothing needs the account: not shown.</summary>
    Hidden,
}
