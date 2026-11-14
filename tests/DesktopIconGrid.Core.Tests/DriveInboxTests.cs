using System.Net;
using System.Text;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests;

/// <summary>Sending a file to another PC through the app's folder on Google Drive, against a Drive kept in memory.</summary>
public sealed class DriveInboxTests : IDisposable
{
    private const string Laptop = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Desktop = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private readonly string _folder = Directory.CreateTempSubdirectory("dig-driveinbox-").FullName;
    private readonly MemoryDrive _drive = new();

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string MakeFile(string name, string text = "hello")
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>Files and folders by id, with a parent each; the app folder is "app".</summary>
    private sealed class MemoryDrive : IDriveFiles
    {
        private readonly Dictionary<string, (string Parent, string Name, bool IsFolder, byte[] Content, DateTimeOffset Modified)> _entries = [];
        private int _next;

        public bool FailDeletes { get; set; }

        public int Count => _entries.Count;

        public int Uploads { get; private set; }

        /// <summary>Called before a folder is made: another PC may make one at the same moment.</summary>
        public Action<string, string>? BeforeCreate { get; set; }

        public Task<string> AppFolderIdAsync(CancellationToken cancellationToken) => Task.FromResult("app");

        public Task<DriveEntry?> GetEntryAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(_entries.TryGetValue(id, out var e) ? Entry(id, e) : null);

        public Task<IReadOnlyList<DriveEntry>> ListChildrenAsync(string folderId, CancellationToken cancellationToken)
        {
            Exists(folderId);
            return Task.FromResult<IReadOnlyList<DriveEntry>>(_entries.Where(pair => pair.Value.Parent == folderId).Select(pair => Entry(pair.Key, pair.Value)!).ToList());
        }

        /// <summary>As Drive answers for a folder that was deleted.</summary>
        private void Exists(string folderId)
        {
            if (folderId != "app" && !_entries.ContainsKey(folderId))
            {
                throw new DriveException("File not found.", HttpStatusCode.NotFound);
            }
        }

        public Task<DriveEntry> CreateFolderAsync(string parentId, string name, CancellationToken cancellationToken)
        {
            BeforeCreate?.Invoke(parentId, name);
            Exists(parentId);
            var id = $"id{_next++}";
            _entries[id] = (parentId, name, true, [], DateTimeOffset.UnixEpoch.AddSeconds(_next));
            return Task.FromResult(Entry(id, _entries[id])!);
        }

        public Task<DriveEntry> UploadAsync(string parentId, string name, string mimeType, Stream content, long length, string? existingFileId, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            Exists(parentId);
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            Uploads++;
            var id = existingFileId ?? $"id{_next++}";
            _entries[id] = (parentId, name, false, buffer.ToArray(), DateTimeOffset.UnixEpoch.AddSeconds(_next++));
            return Task.FromResult(Entry(id, _entries[id])!);
        }

        public Task DownloadAsync(string fileId, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            destination.Write(_entries[fileId].Content);
            return Task.CompletedTask;
        }

        public Task DeleteEntryAsync(string id, CancellationToken cancellationToken)
        {
            if (FailDeletes)
            {
                throw new DriveException("no");
            }

            _entries.Remove(id);
            return Task.CompletedTask;
        }

        public Task ExportAsync(string fileId, string exportMimeType, Stream destination, CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>Puts a file somewhere as someone else with access to the account might.</summary>
        public void Plant(string parentId, string name, string text) =>
            _entries[$"id{_next++}"] = (parentId, name, false, Encoding.UTF8.GetBytes(text), DateTimeOffset.UnixEpoch.AddSeconds(_next));

        /// <summary>A folder made at <paramref name="made"/>, as another PC might have made it.</summary>
        public string PlantFolder(string parentId, string name, DateTimeOffset made)
        {
            var id = $"id{_next++}";
            _entries[id] = (parentId, name, true, [], made);
            return id;
        }

        /// <summary>Deletes on Drive, as the user might in a browser (the folder's content goes with it).</summary>
        public void Remove(string id)
        {
            foreach (var child in _entries.Where(pair => pair.Value.Parent == id).Select(pair => pair.Key).ToList())
            {
                Remove(child);
            }

            _entries.Remove(id);
        }

        public string TextOf(string id) => Encoding.UTF8.GetString(_entries[id].Content);

        public IReadOnlyList<string> FoldersNamed(string parentId, string name) =>
            _entries.Where(pair => pair.Value.Parent == parentId && pair.Value.Name == name && pair.Value.IsFolder).Select(pair => pair.Key).ToList();

        public string IdOf(string parentId, string name) => _entries.Single(pair => pair.Value.Parent == parentId && pair.Value.Name == name).Key;

        private static DriveEntry? Entry(string id, (string Parent, string Name, bool IsFolder, byte[] Content, DateTimeOffset Modified) e) =>
            new(id, e.Name, e.IsFolder ? DriveEntry.FolderMimeType : "application/octet-stream", e.IsFolder ? null : e.Content.Length, e.Modified, CreatedTime: e.Modified);
    }

    [Fact]
    public async Task A_file_sent_through_Drive_arrives_once_under_a_free_name_and_leaves_the_inbox()
    {
        var laptop = new DriveInbox(_drive, Laptop, "Laptop");
        var desktop = new DriveInbox(_drive, Desktop, "Desktop‮ PC");
        await laptop.AnnounceAsync(default);
        await desktop.AnnounceAsync(default);
        await desktop.AnnounceAsync(default); // said already: nothing uploaded again
        var afterAnnouncing = _drive.Count;

        Assert.Equal([new InboxDevice(Desktop, "Desktop PC")], await laptop.DevicesAsync(default));
        Assert.Equal("report.pdf", await laptop.SendAsync(Desktop, MakeFile("report.pdf"), default));
        Assert.Equal(afterAnnouncing + 1, _drive.Count);
        Assert.Empty(await laptop.PendingAsync(null, default));

        var waiting = Assert.Single(await desktop.PendingAsync(null, default));
        Assert.Equal(("report.pdf", Laptop, "Laptop", 5L), (waiting.Name, waiting.SenderId, waiting.SenderName, waiting.Size));

        var downloads = Path.Combine(_folder, "Downloads");
        Directory.CreateDirectory(downloads);
        File.WriteAllText(Path.Combine(downloads, "report.pdf"), "older");
        var seen = new List<string>();
        var (path, marked, cleared) = await desktop.ReceiveAsync(waiting, downloads, arriving =>
        {
            seen.Add(arriving);
            return true;
        }, default);

        Assert.Equal(Path.Combine(downloads, "report (2).pdf"), path);
        Assert.Equal("hello", File.ReadAllText(path));
        Assert.True(marked && cleared);
        Assert.EndsWith(".part", Assert.Single(seen)); // marked before it had its name
        Assert.Empty(await desktop.PendingAsync(null, default));
        Assert.Equal(afterAnnouncing, _drive.Count);
    }

    [Fact]
    public async Task Only_what_the_app_put_there_is_taken_and_link_files_arrive_inert()
    {
        var desktop = new DriveInbox(_drive, Desktop, "Desktop");
        await desktop.AnnounceAsync(default);
        var inbox = _drive.IdOf("app", FolderInbox.FolderName);
        var mine = _drive.IdOf(inbox, Desktop);
        _drive.Plant(mine, "stray.txt", "x");
        _drive.Plant(mine, "not-an-id~x.txt", "x");
        _drive.Plant(mine, $"{Laptop}~site.url", "[InternetShortcut]");
        _drive.Plant(mine, $"{Laptop}~..\\..\\evil.exe", "x");

        var waiting = await desktop.PendingAsync(null, default);

        Assert.Equal(["site.url.received", ".._.._evil.exe"], waiting.Select(file => file.Name));
        Assert.All(waiting, file => Assert.Equal(Laptop[..8], file.SenderName)); // the sender has no inbox: the id's start
        var downloads = Path.Combine(_folder, "Downloads");
        foreach (var file in waiting)
        {
            Assert.Equal(downloads, Path.GetDirectoryName((await desktop.ReceiveAsync(file, downloads, null, default)).Path));
        }
    }

    [Fact]
    public async Task Sending_needs_a_PC_that_takes_part_and_a_file_and_a_failed_delete_is_reported()
    {
        var laptop = new DriveInbox(_drive, Laptop, "Laptop");
        var file = MakeFile("a.txt");

        Assert.Equal(SendProblem.NoInbox, (await Assert.ThrowsAsync<InboxSendException>(() => laptop.SendAsync(Desktop, file, default))).Problem);
        Assert.Equal(SendProblem.NotAnotherPc, (await Assert.ThrowsAsync<InboxSendException>(() => laptop.SendAsync(Laptop, file, default))).Problem);
        Assert.Empty(await laptop.DevicesAsync(default)); // nothing was created by looking

        var desktop = new DriveInbox(_drive, Desktop, "Desktop");
        await desktop.AnnounceAsync(default);
        Assert.Equal(SendProblem.NotAFile, (await Assert.ThrowsAsync<InboxSendException>(() => laptop.SendAsync(Desktop, _folder, default))).Problem);

        await laptop.SendAsync(Desktop, file, default);
        var waiting = Assert.Single(await desktop.PendingAsync(null, default));
        _drive.FailDeletes = true;
        var (_, _, cleared) = await desktop.ReceiveAsync(waiting, Path.Combine(_folder, "Downloads"), null, default);

        Assert.False(cleared); // the caller remembers it
        Assert.Empty(await desktop.PendingAsync(id => id == waiting.Path, default));
    }

    [Fact]
    public async Task A_PC_that_takes_its_name_back_is_no_longer_offered_or_sent_to_and_its_waiting_files_stay()
    {
        // Review 27 S1.
        var laptop = new DriveInbox(_drive, Laptop, "Laptop");
        var desktop = new DriveInbox(_drive, Desktop, "Desktop");
        await desktop.AnnounceAsync(default);
        await laptop.SendAsync(Desktop, MakeFile("a.txt"), default);

        Assert.True(await desktop.WithdrawAsync(default));
        Assert.False(await desktop.WithdrawAsync(default));

        Assert.Empty(await laptop.DevicesAsync(default));
        Assert.Equal(SendProblem.NoInbox, (await Assert.ThrowsAsync<InboxSendException>(() => laptop.SendAsync(Desktop, MakeFile("b.txt"), default))).Problem);
        Assert.Equal("a.txt", Assert.Single(await desktop.PendingAsync(null, default)).Name);
        Assert.False(await new DriveInbox(new MemoryDrive(), Laptop, "Laptop").WithdrawAsync(default)); // no inbox at all

        await desktop.AnnounceAsync(default); // said again after it was taken back, by the same run
        Assert.Equal([new InboxDevice(Desktop, "Desktop")], await laptop.DevicesAsync(default));
    }

    [Fact]
    public async Task The_name_file_is_read_once_per_run_and_written_only_when_it_says_something_else()
    {
        // Review 27 S4: a new name of the same length was not written.
        await new DriveInbox(_drive, Laptop, "Laptop").AnnounceAsync(default);
        var uploads = _drive.Uploads;

        var again = new DriveInbox(_drive, Laptop, "Laptop"); // the next run, the same name
        await again.AnnounceAsync(default);
        await again.AnnounceAsync(default);
        Assert.Equal(uploads, _drive.Uploads);

        var renamed = new DriveInbox(_drive, Laptop, "Lapdog"); // as long as before
        await renamed.AnnounceAsync(default);
        Assert.Equal(uploads + 1, _drive.Uploads);
        var mine = _drive.IdOf(_drive.IdOf("app", FolderInbox.FolderName), Laptop);
        Assert.Contains("Lapdog", _drive.TextOf(_drive.IdOf(mine, FolderInbox.DeviceFile)));
        Assert.Equal([new InboxDevice(Laptop, "Lapdog")], await new DriveInbox(_drive, Desktop, "Desktop").DevicesAsync(default));
    }

    [Fact]
    public async Task Of_two_inbox_folders_every_PC_uses_the_older_and_one_just_made_in_vain_goes()
    {
        // Two PCs announcing at the same moment could make two "inbox" folders (review 27 S4).
        var older = _drive.PlantFolder("app", FolderInbox.FolderName, DateTimeOffset.UnixEpoch);
        var desktopFolder = _drive.PlantFolder(older, Desktop, DateTimeOffset.UnixEpoch);
        _drive.Plant(desktopFolder, FolderInbox.DeviceFile, """{"name":"Desktop"}""");
        _drive.PlantFolder("app", FolderInbox.FolderName, DateTimeOffset.UnixEpoch.AddDays(1)); // a newer one, empty

        Assert.Equal([new InboxDevice(Desktop, "Desktop")], await new DriveInbox(_drive, Laptop, "Laptop").DevicesAsync(default));

        // A PC that finds none makes one; another PC made one a moment earlier: the older is used, the new one deleted.
        var drive = new MemoryDrive();
        drive.BeforeCreate = (parent, name) =>
        {
            if (parent == "app" && name == FolderInbox.FolderName)
            {
                drive.BeforeCreate = null;
                drive.PlantFolder("app", FolderInbox.FolderName, DateTimeOffset.UnixEpoch);
            }
        };
        await new DriveInbox(drive, Laptop, "Laptop").AnnounceAsync(default);
        var inbox = Assert.Single(drive.FoldersNamed("app", FolderInbox.FolderName));
        Assert.Single(drive.FoldersNamed(inbox, Laptop));
    }

    [Fact]
    public async Task A_folder_deleted_on_Drive_is_looked_up_again_and_made_anew()
    {
        // Review 27 S4: the remembered ids went stale.
        var desktop = new DriveInbox(_drive, Desktop, "Desktop");
        await desktop.AnnounceAsync(default);
        _drive.Remove(_drive.IdOf("app", FolderInbox.FolderName));

        await Assert.ThrowsAsync<DriveException>(() => desktop.PendingAsync(null, default)); // that look fails

        await desktop.AnnounceAsync(default); // the next one finds nothing and makes the folders again
        Assert.Empty(await desktop.PendingAsync(null, default));
        Assert.Equal([new InboxDevice(Desktop, "Desktop")], await new DriveInbox(_drive, Laptop, "Laptop").DevicesAsync(default));
    }

    [Fact]
    public void A_failed_look_waits_one_two_five_then_fifteen_minutes()
    {
        // Review 27 S4.
        var backOff = new InboxBackOff();
        var start = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(backOff.IsDue(start));

        Assert.True(backOff.Failed(start)); // the first: a warning in the log
        Assert.False(backOff.IsDue(start.AddSeconds(30)));
        Assert.True(backOff.IsDue(start.AddSeconds(57))); // the minute timer may come a little early
        Assert.False(backOff.Failed(start.AddMinutes(1))); // the repeats: debug lines
        Assert.False(backOff.IsDue(start.AddMinutes(2.5)));
        Assert.True(backOff.IsDue(start.AddMinutes(3)));
        backOff.Failed(start.AddMinutes(3));
        Assert.False(backOff.IsDue(start.AddMinutes(7.5)));
        Assert.True(backOff.IsDue(start.AddMinutes(8)));
        backOff.Failed(start.AddMinutes(8));
        Assert.False(backOff.IsDue(start.AddMinutes(22.5)));
        backOff.Failed(start.AddMinutes(23));
        Assert.False(backOff.IsDue(start.AddMinutes(37.5))); // stays at 15
        Assert.True(backOff.IsDue(start.AddMinutes(38)));
        Assert.Equal(5, backOff.Failures);

        backOff.Reset(); // a look that worked, or the properties opened
        Assert.True(backOff.IsDue(start.AddMinutes(24)));
        Assert.True(backOff.Failed(start.AddMinutes(24)));
    }
}
