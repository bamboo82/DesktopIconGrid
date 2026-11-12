using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.SpecialIcons;

/// <summary>
/// The Voice icon: a click makes the bar listen for one spoken name (<see cref="Services.VoiceLaunch"/>), the same as
/// the voice hotkey. It has nothing to set; the language is in Settings.
/// </summary>
public sealed class VoiceHandler(Action listen, Func<string?> language) : ISpecialIconHandler
{
    public string Kind => SpecialIconKinds.Voice;

    public string DisplayName => Strings.Special_Voice_Name;

    public string Description => Strings.Special_Voice_Desc;

    public SpecialIconItem CreateItem() => new() { Name = Strings.Special_Voice_Name, Kind = Kind, TextIcon = new TextIcon("\U0001F3A4", "Segoe UI Emoji", 22) };

    public GaugeState? CreateState(SpecialIconItem item) => null;

    public string? GetToolTip(SpecialIconItem item) =>
        language() is { } name ? string.Format(Strings.Voice_ToolTip, name) : Strings.Settings_VoiceLanguageNone;

    public LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers)
    {
        listen();
        return LaunchResult.Started;
    }

    public bool ShowProperties(SpecialIconItem item, bool isNew) => isNew; // nothing to ask: a new one is simply added
}
