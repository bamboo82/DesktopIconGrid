using System.IO;
using System.Text;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>Where the data lives (0.13.0 WP1): a new place chosen in Settings, and this PC's bar brought into a portable copy.</summary>
public sealed partial class LauncherViewModel
{
    /// <summary>Raised to restart the program: a new data folder takes effect, or this PC's bar is brought in.</summary>
    public event EventHandler? RestartRequested;

    /// <summary>Writes what waits to be written in the data folder (the usage counts), before it is copied; set by the host.</summary>
    public Func<Task>? FlushData { get; set; }

    /// <summary>Where the program and its data are; tests give their own.</summary>
    internal Func<DataFolders> DataFoldersNow { get; set; } = () => DataFolders.Current;

    /// <summary>The time a file kept aside is named after; tests give their own.</summary>
    internal Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.Now;

    /// <summary>
    /// Carries out a new place for the data chosen and checked in Settings: what waits is written, the data is copied when
    /// that was asked (to the new folder, which the running bar does not use: until it restarts it keeps its old one),
    /// <c>portable.txt</c> is written or taken away, and a restart is offered. When <c>portable.txt</c> cannot be changed
    /// beside the program (under Program Files) or the copy fails, that is said and the place is not changed.
    /// </summary>
    internal async Task MoveDataAsync(DataMove move)
    {
        ArgumentNullException.ThrowIfNull(move);
        var folders = DataFoldersNow();
        if (!DataLocation.CanWriteTo(folders.ProgramDirectory))
        {
            _dialogs.ShowError(Strings.Data_Title, string.Format(Strings.Data_MarkerFailed, folders.ProgramDirectory, Strings.Settings_DataProgramReadOnly));
            return;
        }

        if (move.Copy)
        {
            await WriteWhatWaitsAsync();
            var appDrive = move.Portable ? folders.AppDrive : null;
            var deviceId = Core.Models.ThisDevice.Id;
            var copied = await Task.Run(() => DataImport.Run(folders.Directory, move.Folder, appDrive, Now(), deviceId));
            AppLog.Default.Info("App", $"The bar's data copied from {folders.Directory} to {move.Folder}: {string.Join(", ", copied.Copied)}{(copied.KeptAside.Count > 0 ? $"; kept aside: {string.Join(", ", copied.KeptAside)}" : string.Empty)}");
            if (copied.Problem is not null)
            {
                AppLog.Default.Error("App", $"The bar's data could not be copied to {move.Folder}: {copied.Problem}");
                _dialogs.ShowError(Strings.Data_Title, string.Format(Strings.Data_CopyFailed, move.Folder, copied.Problem));
                return;
            }
        }

        if (!move.Copy && move.Portable && !DataImport.HasBar(move.Folder))
        {
            // "Don't copy": the bar starts with what the folder has, which is nothing, so it gets an empty bar now. Without
            // one, a start would take the folder for a note's and stay in the program's folder when that has a bar (R1).
            try
            {
                new LayoutStorage(Path.Combine(move.Folder, LayoutStorage.FileName)).Save(new Core.Models.LauncherLayout());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _dialogs.ShowError(Strings.Data_Title, string.Format(Strings.Data_CopyFailed, move.Folder, ex.Message));
                return;
            }
        }

        try
        {
            if (move.Portable)
            {
                File.WriteAllText(folders.MarkerFile, move.MarkerText + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            else
            {
                File.Delete(folders.MarkerFile);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Error("App", $"{DataLocation.PortableMarker} could not be changed: {ex.Message}");
            _dialogs.ShowError(Strings.Data_Title, string.Format(Strings.Data_MarkerFailed, folders.ProgramDirectory, ex.Message));
            return;
        }

        AppLog.Default.Info("App", move.Portable
            ? $"{DataLocation.PortableMarker} now says \"{move.MarkerText}\": data in {move.Folder} after a restart"
            : $"{DataLocation.PortableMarker} taken away: data in {move.Folder} after a restart");
        var restart = _dialogs.Choose(
            Strings.Data_Title,
            string.Format(Strings.Data_RestartQuestion, move.Folder, folders.Directory),
            [Strings.Data_RestartNow, Strings.Data_Later],
            defaultButton: 0,
            cancelButton: 1);
        if (restart == 0)
        {
            RestartRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// This PC's bar into the portable copy, asked for in Settings: a request file is left in the data folder and the bar
    /// restarts; the start brings the bar in before it reads it, so this bar's last writes (its place on the screen, the
    /// usage counts) do not go over it.
    /// </summary>
    internal void BringThisPcBar()
    {
        var folders = DataFoldersNow();
        try
        {
            File.WriteAllText(Path.Combine(folders.Directory, DataImport.RequestFile), string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError(Strings.Data_Title, string.Format(Strings.Data_CopyFailed, folders.Directory, ex.Message));
            return;
        }

        AppLog.Default.Info("App", $"This PC's bar is to be brought from {folders.ThisPcDirectory} at the restart");
        RestartRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The bar and the usage counts written now, so a copy has them; a flush that hangs is given up after 5 s.</summary>
    private async Task WriteWhatWaitsAsync()
    {
        Save(contentChanged: false);
        if (FlushData is { } flush)
        {
            try
            {
                await Task.WhenAny(flush(), Task.Delay(TimeSpan.FromSeconds(5)));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                AppLog.Default.Warning("App", $"The usage counts could not be written before the copy: {ex.Message}");
            }
        }
    }
}
