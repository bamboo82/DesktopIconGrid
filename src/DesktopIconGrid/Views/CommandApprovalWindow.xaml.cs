using System.Globalization;
using System.Windows;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.Views;

/// <summary>
/// Asks whether a command tile that came with the bar may run on this PC. It shows everything the approval covers
/// (<see cref="CommandTileSettings.Hash"/>), wrapped and scrollable; Cancel is the default button, so Enter twice on the
/// tile does not approve.
/// </summary>
public partial class CommandApprovalWindow : Window
{
    public CommandApprovalWindow(string tileName, CommandTileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        InitializeComponent();
        DialogTheme.Apply(this);
        Intro.Text = string.Format(Strings.Command_ApproveIntro, tileName);
        Command.Text = Describe(settings);
    }

    /// <summary>The text shown: what runs, where, when, and what a click does.</summary>
    public static string Describe(CommandTileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        static string OrNone(string? text) => string.IsNullOrWhiteSpace(text) ? Strings.Command_None : text;
        var timer = settings.RunsOnClickOnly
            ? Strings.Command_RunsOnClick
            : string.Format(Strings.Command_RunsEvery, settings.IntervalSeconds.ToString(CultureInfo.CurrentCulture));
        var click = settings.Click == CommandTileClick.Open
            ? string.Format(Strings.Command_ClickOpens, settings.OpenTarget)
            : Strings.Command_ClickRuns;
        return string.Join(
            Environment.NewLine,
            $"{Strings.Command_Program} {settings.Program}",
            $"{Strings.Command_Arguments} {OrNone(settings.Arguments)}",
            $"{Strings.Command_StartIn} {OrNone(settings.StartIn)}",
            $"{Strings.Command_When} {timer}",
            $"{Strings.Command_Click} {click}");
    }

    private void Approve_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
