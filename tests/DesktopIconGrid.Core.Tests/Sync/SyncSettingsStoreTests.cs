using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests.Sync;

/// <summary>The client secret leaves the process only through the protector when one is configured.</summary>
public sealed class SyncSettingsStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dig-syncstore-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path_ => Path.Combine(_directory, "sync.json");

    /// <summary>Reversible stand-in for DPAPI.</summary>
    private sealed class ReversingProtector : ISecretProtector
    {
        public string Protect(string secret) => "sealed:" + new string(secret.Reverse().ToArray());

        public string? Unprotect(string protectedValue) =>
            protectedValue.StartsWith("sealed:", StringComparison.Ordinal) ? new string(protectedValue[7..].Reverse().ToArray()) : null;
    }

    [Fact]
    public void Secret_is_written_protected_and_read_back()
    {
        var store = new SyncSettingsStore(Path_, new ReversingProtector());
        var settings = store.Load();
        settings.ClientId = "id";
        settings.ClientSecret = "s3cret";

        store.Save(settings);

        var json = File.ReadAllText(Path_);
        Assert.DoesNotContain("s3cret", json);
        Assert.Contains("sealed:terc3s", json);
        Assert.Equal("sealed:terc3s", settings.ClientSecretProtected);
        Assert.Equal("s3cret", settings.ClientSecret); // the in-memory copy stays usable
        Assert.Equal("s3cret", store.Load().ClientSecret);
        Assert.Equal("s3cret", store.Load().Credentials.ClientSecret);
    }

    [Fact]
    public void A_clear_secret_from_an_earlier_version_is_encrypted_on_first_load()
    {
        File.WriteAllText(Path_, """{"deviceName":"PC","clientId":"id","clientSecret":"plain"}""");

        var loaded = new SyncSettingsStore(Path_, new ReversingProtector()).Load();

        Assert.Equal("plain", loaded.ClientSecret);
        var json = File.ReadAllText(Path_);
        Assert.DoesNotContain("\"plain\"", json);
        Assert.Contains("sealed:nialp", json);
    }

    [Fact]
    public void An_unreadable_protected_secret_is_kept_and_clearing_this_PCs_secret_removes_only_its_own()
    {
        File.WriteAllText(Path_, """{"clientId":"id","clientSecretProtected":"garbage"}""");
        var store = new SyncSettingsStore(Path_, new ReversingProtector());

        var loaded = store.Load();
        Assert.Null(loaded.ClientSecret);
        Assert.Equal("garbage", loaded.ClientSecretProtected); // another PC's, perhaps: not this one's to drop

        loaded.ClientSecret = "new";
        store.Save(loaded);
        loaded.ClientSecret = null;
        store.Save(loaded);

        var json = File.ReadAllText(Path_);
        Assert.DoesNotContain("sealed:", json); // this PC's own is gone
        Assert.Contains("garbage", json);
        Assert.Null(store.Load().ClientSecret);
    }

    /// <summary>DPAPI for one PC and user: what another PC protected cannot be read here.</summary>
    private sealed class PcProtector(string pc) : ISecretProtector
    {
        public string Protect(string secret) => pc + ":" + secret;

        public string? Unprotect(string protectedValue) => protectedValue.StartsWith(pc + ":", StringComparison.Ordinal) ? protectedValue[(pc.Length + 1)..] : null;
    }

    [Fact]
    public void PCs_that_share_the_settings_keep_each_their_own_client_secret()
    {
        // The review: on a portable copy, another PC could not read DESK's secret, and its next save wrote null over it.
        var desk = new SyncSettingsStore(Path_, new PcProtector("desk"), machineKey: "pc-desk");
        var laptop = new SyncSettingsStore(Path_, new PcProtector("laptop"), machineKey: "pc-laptop");
        var settings = desk.Load();
        settings.ClientId = "id";
        settings.ClientSecret = "s3cret";
        desk.Save(settings);

        var onLaptop = laptop.Load();
        Assert.Null(onLaptop.ClientSecret);
        laptop.Save(onLaptop); // any save, after an upload for one
        Assert.Equal("s3cret", desk.Load().ClientSecret);

        onLaptop.ClientSecret = "s3cret"; // entered again on LAPTOP
        laptop.Save(onLaptop);
        Assert.Equal("s3cret", desk.Load().ClientSecret);
        Assert.Equal("s3cret", laptop.Load().ClientSecret);

        var clearing = laptop.Load();
        clearing.ClientSecret = null; // LAPTOP removes its own
        laptop.Save(clearing);
        Assert.Null(laptop.Load().ClientSecret);
        Assert.Equal("s3cret", desk.Load().ClientSecret);
    }

    [Fact]
    public void A_secret_another_PC_saved_with_0_8_0_survives_this_PCs_first_save()
    {
        File.WriteAllText(Path_, """{"clientId":"id","clientSecretProtected":"desk:s3cret"}""");
        var laptop = new SyncSettingsStore(Path_, new PcProtector("laptop"), machineKey: "pc-laptop");
        var settings = laptop.Load();

        settings.ClientSecret = "laptop-secret";
        laptop.Save(settings);

        Assert.Equal("s3cret", new SyncSettingsStore(Path_, new PcProtector("desk"), machineKey: "pc-desk").Load().ClientSecret);
        Assert.Equal("laptop-secret", laptop.Load().ClientSecret);
    }

    [Fact]
    public void Without_a_protector_the_secret_stays_in_clear_and_a_protected_value_is_preserved()
    {
        File.WriteAllText(Path_, """{"clientId":"id","clientSecretProtected":"sealed:x"}""");
        var store = new SyncSettingsStore(Path_);

        var loaded = store.Load();
        Assert.Null(loaded.ClientSecret);
        Assert.Equal("sealed:x", loaded.ClientSecretProtected);

        loaded.ClientSecret = "clear";
        store.Save(loaded);
        var json = File.ReadAllText(Path_);
        Assert.Contains("\"clear\"", json);
        Assert.Contains("sealed:x", json);
    }
}
