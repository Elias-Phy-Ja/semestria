using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SchulnetzSync.UI;

/// <summary>
/// Scrolls in proportion to the actual wheel delta, and lets a nested scroll area hand
/// the movement over to its parent once it has hit its end.
///
/// WPF treats every wheel event the same: however small the movement, it scrolls three
/// lines. A mouse sends exactly one event per notch, but a precision touchpad sends many
/// small ones per swipe — each of which then triggers a full jump, and the page bolts.
/// </summary>
public static class SmoothScroll
{
    /// <summary>
    /// Pixels per delta unit. One mouse notch (120) comes to 48 px, which is exactly the
    /// three lines Windows uses by default — so nothing changes for a mouse.
    /// </summary>
    private const double PixelsPerUnit = 0.4;

    /// <summary>Registers the handler for every ScrollViewer in the app.</summary>
    public static void Install()
        => EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheel));

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0) return;
        if (sender is not ScrollViewer sv) return;

        // PreviewMouseWheel tunnels from the outside in. Without this check the outer
        // page would scroll even while the pointer sits over the inner box.
        if (!ReferenceEquals(InnermostScrollViewer(e.OriginalSource as DependencyObject), sv))
            return;

        var target = FirstScrollableInDirection(sv, e.Delta);
        if (target is null) return;   // nothing here can scroll, so let the event pass

        target.ScrollToVerticalOffset(target.VerticalOffset - e.Delta * PixelsPerUnit);
        e.Handled = true;
    }

    /// <summary>The nearest ScrollViewer at or above the element under the pointer.</summary>
    private static ScrollViewer? InnermostScrollViewer(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ScrollViewer sv) return sv;
            element = ParentOf(element);
        }
        return null;
    }

    /// <summary>
    /// Walks outwards until it finds a scroll area with room left in that direction, so a
    /// short inner box passes the movement on to the page instead of swallowing it.
    /// </summary>
    private static ScrollViewer? FirstScrollableInDirection(ScrollViewer from, int delta)
    {
        DependencyObject? element = from;
        while (element is not null)
        {
            if (element is ScrollViewer sv && sv.ScrollableHeight > 0)
            {
                bool room = delta > 0
                    ? sv.VerticalOffset > 0.0
                    : sv.VerticalOffset < sv.ScrollableHeight;
                if (room) return sv;
            }
            element = ParentOf(element);
        }
        return null;
    }

    /// <summary>
    /// Visual parent for visuals, logical parent otherwise — the OriginalSource of a wheel
    /// event can be a text element that has no visual parent at all.
    /// </summary>
    private static DependencyObject? ParentOf(DependencyObject element)
        => element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);
}
