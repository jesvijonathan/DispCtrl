using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace DispCtrl.App.Views;

/// <summary>
/// Lays an open <c>SettingsExpander</c>'s cards out all at once, so scrolling one does not loop.
/// </summary>
/// <remarks>
/// The toolkit lays a <c>SettingsExpander</c>'s items out in an
/// <see cref="ItemsRepeater"/> with a virtualising stack: cards off screen are
/// dropped and counted at the average height. A display's card mixes a
/// 300-pixel overview with 70-pixel rows, so near the bottom the real heights
/// replaced the estimate, the scroll extent changed, the page jumped, cards
/// above were realised again at their real size, and it went round - a scroll
/// that glitched and never reached the bottom (measured through UI Automation:
/// a jump to the end landed at 72%, then 81%). A larger cache only stopped the
/// dropping; the repeater fills a cache over idle frames, so the end still
/// moved away for a moment after opening. A layout that does not virtualise
/// measures every card as the expander opens. The same trap as the display
/// list itself (see the ItemsControl comment on the Displays page); there are
/// a few dozen cards at most.
/// </remarks>
internal static class ExpanderLayout
{
    /// <summary>For an expander's <c>Expanded</c> event.</summary>
    public static void RealiseAll(object? sender)
    {
        if (sender is not FrameworkElement expander) return;
        // Now and once the items are in the tree: they are built as it opens.
        Apply(expander);
        expander.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Apply(expander));
    }

    private static void Apply(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is ItemsRepeater { Layout: not AllAtOnceStack } repeater)
                repeater.Layout = new AllAtOnceStack { Spacing = repeater.Layout is StackLayout stack ? stack.Spacing : 0 };
            // Nested expanders mind their own; everything else is searched.
            if (child is not CommunityToolkit.WinUI.Controls.SettingsExpander) Apply(child);
        }
    }

    /// <summary>A vertical stack that realises and measures every item.</summary>
    private sealed partial class AllAtOnceStack : NonVirtualizingLayout
    {
        public double Spacing { get; init; }

        protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
        {
            double width = 0, height = 0;
            int shown = 0;
            foreach (UIElement child in context.Children)
            {
                child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
                if (child.Visibility == Visibility.Collapsed) continue;
                width = Math.Max(width, child.DesiredSize.Width);
                height += child.DesiredSize.Height + (shown++ > 0 ? Spacing : 0);
            }
            return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
        }

        protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
        {
            double y = 0;
            int shown = 0;
            foreach (UIElement child in context.Children)
            {
                if (child.Visibility == Visibility.Collapsed) { child.Arrange(new Rect(0, y, 0, 0)); continue; }
                if (shown++ > 0) y += Spacing;
                child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
                y += child.DesiredSize.Height;
            }
            return finalSize;
        }
    }
}
