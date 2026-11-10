using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Tests;

/// <summary>The bar window's keys: defaults, overrides, and what the settings store.</summary>
public class WindowKeysTests
{
    [Fact]
    public void A_key_is_its_default_unless_the_settings_give_another_or_turn_it_off()
    {
        var settings = new LauncherSettings { WindowKeys = new() { ["NewShortcut"] = "Ctrl+Shift+N", ["Remove"] = "" } };

        Assert.Equal("Ctrl+Shift+N", WindowKeys.For(settings, WindowKeyAction.NewShortcut));
        Assert.Equal(string.Empty, WindowKeys.For(settings, WindowKeyAction.Remove));
        Assert.Equal("F2", WindowKeys.For(settings, WindowKeyAction.Properties));
        Assert.Equal("F2", WindowKeys.For(new LauncherSettings(), WindowKeyAction.Properties));
    }

    [Fact]
    public void Only_what_differs_from_the_default_is_stored_and_copies_do_not_share_it()
    {
        var chosen = WindowKeys.Defaults.ToDictionary(p => p.Key, p => p.Value);
        Assert.Null(WindowKeys.Overrides(chosen));

        chosen[WindowKeyAction.Find] = "ctrl+f"; // the default, written otherwise
        chosen[WindowKeyAction.Properties] = "Alt+Enter";
        var overrides = WindowKeys.Overrides(chosen);
        Assert.Equal(new Dictionary<string, string> { ["Properties"] = "Alt+Enter" }, overrides);

        var settings = new LauncherSettings { WindowKeys = overrides };
        var copy = settings.Clone();
        copy.WindowKeys!["Properties"] = "F3";
        Assert.Equal("Alt+Enter", settings.WindowKeys!["Properties"]);
    }
}
