using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows.Threading;
using DesktopIconGrid.Core;
using DesktopIconGrid.Core.Health;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Services;
using DesktopIconGrid.SpecialIcons;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// A health check that came with the bar contacts nothing until it is approved on this PC (review 21 H1). The checks
/// here point at a listener of the test, which counts every connection.
/// </summary>
[Collection("Shared icon worker")]
public sealed class HealthApprovalTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-health-").FullName;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private int _connections;

    public HealthApprovalTests()
    {
        _listener.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    Interlocked.Increment(ref _connections);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // Stopped.
            }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        Directory.Delete(_directory, recursive: true);
    }

    private string Check => $"tcp 127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    private (LauncherViewModel ViewModel, RecordingDialogs Dialogs, CommandTrust Trust) Create(params LauncherItem[] items)
    {
        var model = new LauncherModel();
        foreach (var item in items)
        {
            model.Add(item);
        }

        var dialogs = new RecordingDialogs();
        var trust = new CommandTrust(null);
        var viewModel = new LauncherViewModel(
            model,
            new LayoutStorage(Path.Combine(_directory, "layout.json")),
            new NoIcons(),
            dialogs,
            new RecordingLauncher(),
            new ProcessTracker(Dispatcher.CurrentDispatcher, Path.Combine(_directory, "logs")),
            new SpecialIconRegistry([]),
            action => action(),
            isMissing: _ => false,
            trust: trust);
        return (viewModel, dialogs, trust);
    }

    private async Task<bool> ConnectedAsync(TimeSpan within)
    {
        var until = DateTime.UtcNow + within;
        while (DateTime.UtcNow < until)
        {
            if (Volatile.Read(ref _connections) > 0)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return Volatile.Read(ref _connections) > 0;
    }

    [Fact]
    public async Task A_check_that_came_with_the_bar_contacts_nothing_until_it_is_approved_here()
    {
        var shortcut = new ShortcutItem { Name = "NAS", Location = @"C:\nas.exe", HealthCheck = Check };
        var (viewModel, dialogs, trust) = Create(shortcut);
        var item = viewModel.GetOrCreateViewModel(shortcut);
        try
        {
            Assert.True(item.IsHealthPaused);
            Assert.False(await ConnectedAsync(TimeSpan.FromSeconds(1.5)));

            dialogs.ApprovalAnswer = false; // Cancel
            viewModel.ApproveHealthCheck(item);
            Assert.Contains(Check, Assert.Single(dialogs.ApprovalsShown)); // the whole check, not the shortened one
            Assert.True(item.IsHealthPaused);
            Assert.False(await ConnectedAsync(TimeSpan.FromSeconds(0.5)));

            dialogs.ApprovalAnswer = true;
            viewModel.ApproveHealthCheck(item);

            Assert.False(item.IsHealthPaused);
            Assert.True(trust.IsApproved(HealthCheck.HashOf(shortcut)!));
            Assert.True(await ConnectedAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            item.Detach();
        }
    }

    [Fact]
    public void An_edit_in_Properties_never_approves_an_arrived_check_but_keeps_an_approved_one_approved()
    {
        var shortcut = new ShortcutItem { Name = "NAS", Location = @"C:\nas.exe", HealthCheck = Check };
        var (viewModel, dialogs, trust) = Create(shortcut);
        var item = viewModel.GetOrCreateViewModel(shortcut);
        try
        {
            dialogs.PropertiesEdit = edited => edited.Name = "Renamed";
            viewModel.EditItem(item);
            Assert.Equal("Renamed", shortcut.Name);
            Assert.True(item.IsHealthPaused);
            Assert.False(trust.IsApproved(HealthCheck.HashOf(shortcut)!));

            // Changing only the interval is still not typing the check (review 22 A1): it stays paused.
            dialogs.PropertiesEdit = edited => ((ShortcutItem)edited).HealthInterval = 120;
            viewModel.EditItem(item);
            Assert.True(item.IsHealthPaused);
            Assert.False(trust.IsApproved(HealthCheck.HashOf(shortcut)!));

            dialogs.ApprovalAnswer = true; // the menu, which shows all of it
            viewModel.ApproveHealthCheck(item);
            Assert.False(item.IsHealthPaused);

            // Once approved here, an edit here carries the approval over to the edited check.
            dialogs.PropertiesEdit = edited => ((ShortcutItem)edited).HealthInterval = 300;
            viewModel.EditItem(item);
            Assert.True(trust.IsApproved(HealthCheck.HashOf(shortcut)!));
            Assert.False(item.IsHealthPaused);
        }
        finally
        {
            item.Detach();
        }
    }

    [Fact]
    public void A_check_typed_in_a_new_shortcut_is_approved_and_the_interval_is_part_of_the_approval()
    {
        var (viewModel, dialogs, trust) = Create();
        dialogs.PropertiesEdit = edited =>
        {
            var shortcut = (ShortcutItem)edited;
            shortcut.Name = "NAS";
            shortcut.Location = @"C:\nas.exe";
            shortcut.HealthCheck = Check;
        };

        viewModel.NewShortcut(viewModel.RootItems, int.MaxValue);

        var added = Assert.IsType<ShortcutItem>(Assert.Single(viewModel.Model.Items));
        var item = viewModel.GetOrCreateViewModel(added);
        try
        {
            Assert.False(item.IsHealthPaused);
            Assert.True(trust.IsApproved(HealthCheck.HashOf(added)!));
            // The same check every 30 s instead of every minute is another approval (a synced edit cannot speed it up).
            Assert.NotEqual(HealthCheck.HashOf(added), HealthCheck.Parse(Check, out _)!.Hash(30));
        }
        finally
        {
            item.Detach();
        }
    }
}
