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
        : this(Strings.Command_ApproveTitle, string.Format(Strings.Command_ApproveIntro, tileName), Describe(settings ?? throw new ArgumentNullException(nameof(settings))), Strings.Command_ApproveButton)
    {
    }

    /// <summary>Any approval of something that came with the bar (a health check too): the full details, Cancel the default.</summary>
    public CommandApprovalWindow(string title, string intro, string details, string approveText)
    {
        InitializeComponent();
        DialogTheme.Apply(this);
        Title = title;
        Intro.Text = intro;
        Command.Text = details;
        ApproveButton.Content = approveText;
        Command.AddHandler(System.Windows.Controls.ScrollViewer.ScrollChangedEvent, new System.Windows.Controls.ScrollChangedEventHandler((_, _) => UpdateApprovable()));
        Loaded += (_, _) => UpdateApprovable();
    }

    /// <summary>
    /// Approve is enabled only once the whole text has been in view (review 22 A3): padding cannot push the end of a
    /// command, or the timer and click lines, below the fold of a box whose only hint is a scroll bar.
    /// </summary>
    private void UpdateApprovable()
    {
        if (ApproveButton.IsEnabled || !Command.IsLoaded)
        {
            return;
        }

        var seenAll = Command.VerticalOffset + Command.ViewportHeight >= Command.ExtentHeight - 1;
        ApproveButton.IsEnabled = seenAll;
        ScrollHint.Visibility = seenAll ? Visibility.Collapsed : Visibility.Visible;
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
