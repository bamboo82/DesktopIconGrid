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
    public LayoutHistoryWindow(IReadOnlyList<LayoutHistoryEntry> entries)
    {
        InitializeComponent();
        DialogTheme.Apply(this);
        List.ItemsSource = entries.Select(e => new Row(
            e,
            e.SavedAt.ToLocalTime().ToString("f", CultureInfo.CurrentCulture),
            string.Format(CultureInfo.CurrentCulture, Strings.History_Entry, e.Shortcuts, e.Groups))).ToList();
        EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => List.Focus();
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
