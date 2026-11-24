using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace DesktopIconGrid.Views;

/// <summary>
/// The picture of the window at the pointer on Settings' Keys tab (0.16.0 WP2): one tab stop around the picture, its
/// border the focus ring. For UI Automation it is one control named by the row's label whose read-only value says where
/// the pointer goes (水平 50 %，垂直 0 %), so a screen reader hears the point and each change of it.
/// </summary>
public sealed class PointerAnchorPad : Border
{
    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText), typeof(string), typeof(PointerAnchorPad), new FrameworkPropertyMetadata(string.Empty, OnValueTextChanged));

    /// <summary>Where the pointer goes, in words.</summary>
    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PadPeer(this);

    private static void OnValueTextChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    {
        if (UIElementAutomationPeer.FromElement((PointerAnchorPad)owner) is PadPeer peer)
        {
            peer.RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, e.OldValue as string ?? string.Empty, e.NewValue as string ?? string.Empty);
        }
    }

    private sealed class PadPeer(PointerAnchorPad owner) : FrameworkElementAutomationPeer(owner), IValueProvider
    {
        public bool IsReadOnly => true;

        public string Value => ((PointerAnchorPad)Owner).ValueText;

        public void SetValue(string value) => throw new ElementNotEnabledException(); // set with the boxes beside it

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;

        protected override string GetClassNameCore() => nameof(PointerAnchorPad);

        protected override bool IsControlElementCore() => true;
    }
}
