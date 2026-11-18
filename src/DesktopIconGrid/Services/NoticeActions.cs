using System.IO;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Services;

/// <summary>
/// What a click on a notice's balloon does, by the notice's target (0.12.0 WP1 N3). The bar gives the actions; the
/// file checks can be replaced in tests.
/// </summary>
public sealed class NoticeActions(Action openLog, Action openSync, Action openSettings, Action showBar, Action<string> selectFile, Action<string> openFolder)
{
    internal Func<string, bool> FileExists { get; init; } = File.Exists;

    internal Func<string, bool> DirectoryExists { get; init; } = Directory.Exists;

    public void Act(Notice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        switch (notice.Target)
        {
            case NoticeTarget.Log:
                openLog();
                break;
            case NoticeTarget.Folder:
                var (select, open) = NoticeFolder.Resolve(notice.Path, FileExists, DirectoryExists);
                if (select is not null)
                {
                    selectFile(select);
                }
                else if (open is not null)
                {
                    openFolder(open);
                }

                break; // neither is there any more: nothing
            case NoticeTarget.Sync:
                openSync();
                break;
            case NoticeTarget.Settings:
                openSettings();
                break;
            case NoticeTarget.Bar:
                showBar();
                break;
        }
    }
}
