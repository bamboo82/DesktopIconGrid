using System.Windows;
using System.Windows.Controls;
using DesktopIconGrid.ViewModels;

namespace DesktopIconGrid.Views;

/// <summary>Draws separators with their own template and everything else as an icon.</summary>
public sealed class ItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Icon { get; set; }

    public DataTemplate? Separator { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is ItemViewModel { IsSeparator: true } ? Separator : Icon;
}
