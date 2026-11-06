using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests.Sync;

/// <summary>
/// "This PC" is the PC and user, not the data folder: per-PC paths follow the machine key, and sync settings made on
/// another PC (a portable stick, copied data, a cloned VM) get an identity of their own here.
/// </summary>
public sealed class DeviceIdentityTests : IDisposable
{
    private const string MachineGuid = "3f2a9c1e-7b44-4d0a-9e51-0c6d8f2b7a19";
    private const string UserSid = "S-1-5-21-1004336348-1177238915-682003330-1001";

    private readonly string _folder = Directory.CreateTempSubdirectory("dig-device-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void The_machine_key_is_the_same_every_time_and_differs_per_PC_and_per_user()
    {
        var key = MachineKey.From(MachineGuid, UserSid);

        Assert.NotNull(key);
        Assert.StartsWith("pc-", key, StringComparison.Ordinal);
        Assert.Equal(27, key.Length);
        Assert.Equal(key, MachineKey.From(MachineGuid.ToUpperInvariant(), " " + UserSid.ToLowerInvariant() + " "));
        Assert.NotEqual(key, MachineKey.From(MachineGuid, "S-1-5-21-1004336348-1177238915-682003330-1002"));
        Assert.NotEqual(key, MachineKey.From("00000000-1111-2222-3333-444444444444", UserSid));
        Assert.DoesNotContain(MachineGuid.Replace("-", string.Empty, StringComparison.Ordinal)[..8], key, StringComparison.OrdinalIgnoreCase);
        Assert.Null(MachineKey.From(null, UserSid));
        Assert.Null(MachineKey.From(MachineGuid, " "));
    }

    [Fact]
    public void Settings_made_on_another_PC_get_a_device_id_and_name_of_their_own_here()
    {
        var here = MachineKey.From(MachineGuid, UserSid);
        var older = new SyncSettings { DeviceId = "desk-id", DeviceName = "DESK" };

        Assert.True(older.AdoptMachine(here)); // from before 0.8.0: taken as this PC's
        Assert.Equal(("desk-id", "DESK", here), (older.DeviceId, older.DeviceName, older.MachineFingerprint));
        Assert.False(older.AdoptMachine(here)); // the same PC: nothing changes
        Assert.False(older.AdoptMachine(null)); // the machine is not known: nothing changes

        var elsewhere = MachineKey.From("00000000-1111-2222-3333-444444444444", UserSid);
        Assert.True(older.AdoptMachine(elsewhere)); // the stick, the copied folder or the clone on another PC

        Assert.NotEqual("desk-id", older.DeviceId);
        Assert.Equal(SyncSettings.DefaultDeviceName(), older.DeviceName);
        Assert.Equal(elsewhere, older.MachineFingerprint);
    }

    [Fact]
    public void A_sync_json_that_is_not_valid_JSON_keeps_its_device_id_and_is_set_aside()
    {
        var path = Path.Combine(_folder, SyncSettingsStore.FileName);
        const string broken = "{ \"deviceName\": \"DESK\", \"deviceId\": \"desk-id\", \"linkedProfile\": \"Wo";
        File.WriteAllText(path, broken);

        var settings = new SyncSettingsStore(path).Load();

        Assert.Equal(("desk-id", "DESK"), (settings.DeviceId, settings.DeviceName));
        Assert.Equal(broken, File.ReadAllText(path + ".unreadable"));
    }

    [Fact]
    public void The_fingerprint_and_the_last_revision_are_saved_with_the_settings()
    {
        var store = new SyncSettingsStore(Path.Combine(_folder, SyncSettingsStore.FileName));
        var settings = new SyncSettings { DeviceId = "desk-id", MachineFingerprint = "pc-123", LastRevision = "r1" };

        store.Save(settings);
        var read = store.Load();

        Assert.Equal(("desk-id", "pc-123", "r1"), (read.DeviceId, read.MachineFingerprint, read.LastRevision));
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }
}
