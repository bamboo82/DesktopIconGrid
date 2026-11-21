using System.Text.Json;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Core.Sync;

namespace DesktopIconGrid.Core.Tests;

/// <summary>0.14.0 A3: one name for each PC, everywhere; a renamed PC is renamed on the others.</summary>
public sealed class SendToNamesTests
{
    private const string Laptop = "3fa9b2c1aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Other = "7c21d4e5bbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Format = "Send to {0}";

    private static SpecialIconItem Icon(string caption = "Send to Laptop", string? name = "Laptop") => new()
    {
        Name = caption,
        Kind = SpecialIconKinds.SendToDevice,
        ModifiedAt = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero),
        Settings = new(StringComparer.OrdinalIgnoreCase) { [SendToNames.DeviceIdKey] = Laptop, [SendToNames.DeviceNameKey] = name! },
    };

    [Fact]
    public void Two_PCs_with_one_name_are_told_apart_by_the_start_of_their_ids()
    {
        IReadOnlyList<InboxDevice> devices = [new(Laptop, "NOTEBOOK"), new(Other, "notebook"), new("cccccccccccccccccccccccccccccccc", "DESKTOP")];

        Assert.Equal(["NOTEBOOK (3fa9)", "notebook (7c21)", "DESKTOP"], SendToNames.Labels(devices));
        Assert.Equal(["NOTEBOOK"], SendToNames.Labels([new(Laptop, "NOTEBOOK")]));
    }

    [Fact]
    public void A_renamed_PC_renames_the_icon_and_its_made_caption_once_and_nothing_else()
    {
        var icon = Icon();
        var modified = icon.ModifiedAt;

        Assert.True(SendToNames.Follow(icon, [new(Laptop, "Laptop 2"), new(Other, "Desktop")], Format));

        Assert.Equal(("Send to Laptop 2", "Laptop 2"), (icon.Name, icon.GetSetting(SendToNames.DeviceNameKey)));
        Assert.Equal(modified, icon.ModifiedAt); // not an edit: every PC that sees the name writes the same icon
        Assert.False(SendToNames.Follow(icon, [new(Laptop, "Laptop 2")], Format)); // the next look finds nothing
    }

    [Fact]
    public void A_caption_typed_by_hand_stays_and_only_the_kept_name_follows()
    {
        var icon = Icon(caption: "To the laptop in the study");

        Assert.True(SendToNames.Follow(icon, [new(Laptop, "Study PC")], Format));

        Assert.Equal(("To the laptop in the study", "Study PC"), (icon.Name, icon.GetSetting(SendToNames.DeviceNameKey)));
    }

    [Fact]
    public void Two_PCs_that_see_the_same_new_name_write_the_same_icon()
    {
        var onDesktop = Icon();
        var onTablet = (SpecialIconItem)onDesktop.Clone();
        IReadOnlyList<InboxDevice> listed = [new(Laptop, "Laptop‮ 2")]; // cleaned the same way on both

        SendToNames.Follow(onDesktop, listed, Format);
        SendToNames.Follow(onTablet, listed, Format);

        Assert.Equal(JsonSerializer.Serialize<LauncherItem>(onDesktop, LayoutStorage.JsonOptions), JsonSerializer.Serialize<LauncherItem>(onTablet, LayoutStorage.JsonOptions));
        Assert.Equal("Laptop 2", onDesktop.GetSetting(SendToNames.DeviceNameKey));
    }

    [Fact]
    public void A_PC_that_is_not_listed_an_icon_without_a_PC_and_other_icons_are_left_alone()
    {
        var icon = Icon();
        Assert.False(SendToNames.Follow(icon, [new(Other, "Desktop")], Format)); // it does not receive now: its name is not known
        Assert.Equal("Laptop", icon.GetSetting(SendToNames.DeviceNameKey));

        var noPc = new SpecialIconItem { Name = "Send to PC", Kind = SpecialIconKinds.SendToDevice };
        Assert.False(SendToNames.Follow(noPc, [new(Laptop, "Laptop 2")], Format));

        var clock = Icon();
        clock.Kind = SpecialIconKinds.Clock;
        Assert.False(SendToNames.Follow(clock, [new(Laptop, "Laptop 2")], Format));

        var unnamed = Icon(caption: "Send to 3fa9b2c1", name: null); // chosen when the PC had no name: its id's start
        Assert.True(SendToNames.Follow(unnamed, [new(Laptop, "Laptop")], Format));
        Assert.Equal("Send to Laptop", unnamed.Name);
    }
}
