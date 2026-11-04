using System.Text.Json;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Storage;

namespace DesktopIconGrid.Core.Tests;

public sealed class LayoutStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DesktopIconGrid.Tests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "nested", "layout.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Load_missing_file_is_first_run()
    {
        var storage = new LayoutStorage(FilePath);

        var result = storage.Load();

        Assert.True(result.IsFirstRun);
        Assert.Empty(result.Layout.Items);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Save_creates_directories_and_round_trips_polymorphic_items()
    {
        var storage = new LayoutStorage(FilePath);
        var layout = new LauncherLayout
        {
            Settings = new LauncherSettings { IconSize = 48, BackgroundColor = "#123456", AlwaysOnTop = true },
            Window = new WindowPlacement { Left = 10, Top = 20, Width = 500, Height = 120 },
            Items =
            [
                new ShortcutItem { Name = "Editor", Location = "C:\\ed.exe", Parameters = "--fast", MultiLaunch = true },
                new GroupItem
                {
                    Name = "Tools",
                    IconPath = "C:\\icons\\tools.ico",
                    Items = [new ShortcutItem { Name = "Inner", Location = "https://example.com" }],
                },
            ],
        };

        storage.Save(layout);
        var result = storage.Load();

        Assert.False(result.IsFirstRun);
        Assert.False(File.Exists(FilePath + ".tmp"));
        var loaded = result.Layout;
        Assert.Equal(48, loaded.Settings.IconSize);
        Assert.Equal("#123456", loaded.Settings.BackgroundColor);
        Assert.True(loaded.Settings.AlwaysOnTop);
        Assert.Equal(500, loaded.Window!.Width);

        var shortcut = Assert.IsType<ShortcutItem>(loaded.Items[0]);
        Assert.Equal(layout.Items[0].Id, shortcut.Id);
        Assert.Equal("--fast", shortcut.Parameters);
        Assert.True(shortcut.MultiLaunch);

        var group = Assert.IsType<GroupItem>(loaded.Items[1]);
        Assert.Equal("C:\\icons\\tools.ico", group.IconPath);
        var inner = Assert.IsType<ShortcutItem>(Assert.Single(group.Items));
        Assert.Equal("https://example.com", inner.Location);
    }

    [Fact]
    public void Saved_json_uses_type_discriminator_and_camel_case()
    {
        var storage = new LayoutStorage(FilePath);
        storage.Save(new LauncherLayout { Items = [new ShortcutItem { Name = "A", Location = "a.exe" }] });

        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
        var root = document.RootElement;

        Assert.Equal(LauncherLayout.CurrentVersion, root.GetProperty("version").GetInt32());
        Assert.Equal("shortcut", root.GetProperty("items")[0].GetProperty("$type").GetString());
        Assert.True(root.GetProperty("settings").TryGetProperty("iconSize", out _));
    }

    [Fact]
    public void Load_tolerates_unknown_properties_and_discriminator_order()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """
            {
              "version": 1,
              "future": { "x": 1 },
              "settings": { "iconSize": 24, "unknownSetting": true },
              "items": [
                { "name": "Late discriminator", "location": "x.exe", "$type": "shortcut" }
              ]
            }
            """);

        var result = new LayoutStorage(FilePath).Load();

        Assert.Null(result.Error);
        Assert.Equal(24, result.Layout.Settings.IconSize);
        Assert.Equal("Late discriminator", Assert.IsType<ShortcutItem>(Assert.Single(result.Layout.Items)).Name);
    }

    [Fact]
    public void Load_corrupt_file_backs_it_up_and_returns_empty_layout()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ this is not json");

        var result = new LayoutStorage(FilePath).Load();

        Assert.False(result.IsFirstRun);
        Assert.NotNull(result.Error);
        Assert.NotNull(result.CorruptFileBackupPath);
        Assert.True(File.Exists(result.CorruptFileBackupPath));
        Assert.False(File.Exists(FilePath));
        Assert.Empty(result.Layout.Items);
    }

    [Fact]
    public void Load_null_sections_are_replaced_with_defaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """{ "settings": null, "items": null }""");

        var result = new LayoutStorage(FilePath).Load();

        Assert.NotNull(result.Layout.Settings);
        Assert.NotNull(result.Layout.Items);
    }

    [Fact]
    public void Default_path_is_under_local_app_data()
    {
        var path = LayoutStorage.GetDefaultFilePath();

        Assert.EndsWith(Path.Combine(LayoutStorage.ApplicationFolderName, LayoutStorage.FileName), path);
    }
}
