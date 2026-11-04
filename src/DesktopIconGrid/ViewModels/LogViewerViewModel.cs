using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>Backs the "Show log" window for one shortcut: the latest captured run and where its file lives.</summary>
public sealed class LogViewerViewModel : ObservableObject
{
    private string _header = string.Empty;

    public LogViewerViewModel(string itemName, ProcessLogSession? session, string logDirectory)
    {
        ItemName = itemName;
        Session = session;
        LogDirectory = logDirectory;
        OpenFolderCommand = new RelayCommand(() => Open(LogDirectory), () => Directory.Exists(LogDirectory));
        OpenFileCommand = new RelayCommand(() => Open(Session!.LogPath), () => Session is not null && File.Exists(Session.LogPath));
        UpdateHeader();
        if (session is not null)
        {
            session.Finished += (_, _) => UpdateHeader();
        }
    }

    public string ItemName { get; }

    public ProcessLogSession? Session { get; }

    public string LogDirectory { get; }

    public string Title => $"Log — {ItemName}";

    public bool HasSession => Session is not null;

    public string Header
    {
        get => _header;
        private set => SetProperty(ref _header, value);
    }

    public string EmptyText =>
        "No run has been captured yet. Click the icon to start the program; its output appears here and is written to the log folder.";

    public ICommand OpenFolderCommand { get; }

    public ICommand OpenFileCommand { get; }

    private void UpdateHeader()
    {
        if (Session is null)
        {
            Header = string.Empty;
            return;
        }

        var started = Session.StartedAt.ToString("g", CultureInfo.CurrentCulture);
        Header = Session.IsFinished
            ? $"Started {started} — exited with code {(Session.ExitCode.HasValue ? Session.ExitCode.Value.ToString(CultureInfo.InvariantCulture) : "?")}"
            : $"Started {started} — running";
    }

    private static void Open(string path)
    {
        var result = ProcessLauncher.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        result.Process?.Dispose();
    }
}
