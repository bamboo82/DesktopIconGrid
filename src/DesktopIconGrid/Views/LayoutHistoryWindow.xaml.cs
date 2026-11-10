using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopIconGrid.Core.Storage;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.Views;

/// <summary>Restore an earlier layout: the kept layouts, newest first, one to pick.</summary>
public partial class LayoutHistoryWindow : Window
{
    /// <param name="load">Reads the kept layouts off the window's thread; <see langword="null"/> when it ran out of time.</param>
    public LayoutHistoryWindow(Func<Task<IReadOnlyList<LayoutHistoryEntry>?>> load)
    {
        InitializeComponent();
        DialogTheme.Apply(this);
        EmptyText.Text = Strings.History_Loading;
        EmptyText.Visibility = Visibility.Visible;
        Loaded += async (_, _) =>
        {
            List.Focus();
            var entries = await load();
            List.ItemsSource = (entries ?? []).Select(e => new Row(
                e,
                e.SavedAt.ToLocalTime().ToString("f", CultureInfo.CurrentCulture),
                string.Format(CultureInfo.CurrentCulture, Strings.History_Entry, e.Shortcuts, e.Groups))).ToList();
            EmptyText.Text = entries is null ? Strings.History_TimedOut : Strings.History_None;
            EmptyText.Visibility = entries is { Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;
        };
    }

    /// <summary>The entry picked, once the dialog returns true.</summary>
    public LayoutHistoryEntry? Chosen => (List.SelectedItem as Row)?.Entry;

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => RestoreButton.IsEnabled = List.SelectedItem is not null;

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (List.SelectedItem is not null)
        {
            DialogResult = true;
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e) => DialogResult = List.SelectedItem is not null;

    private sealed record Row(LayoutHistoryEntry Entry, string When, string What);
}
