using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SchulnetzSync.UI;

/// <summary>
/// Makes the wheel scroll proportionally to the actual wheel delta, and lets a
/// nested scroll area hand over to its parent once it reaches its end.
///
/// WPF behandelt jedes Rad-Ereignis gleich: Egal wie klein der Ausschlag ist,
/// es scrollt drei Zeilen. Eine Maus sendet pro Rastung genau ein Ereignis,
/// ein Präzisions-Touchpad dagegen viele kleine pro Wischbewegung — jedes davon
/// löst dann einen vollen Sprung aus, und die Seite rast unkontrolliert davon.
/// </summary>
public static class SmoothScroll
{
    /// <summary>
    /// Pixel pro Delta-Einheit. Eine Mausrastung (120) ergibt 48 px und damit
    /// genau die drei Zeilen, die Windows als Standard vorgibt — für die Maus
    /// ändert sich also nichts.
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

        // PreviewMouseWheel tunnelt von aussen nach innen. Ohne diese Prüfung
        // würde bei verschachtelten Bereichen die äussere Seite scrollen,
        // obwohl der Zeiger über dem inneren Feld steht.
        if (!ReferenceEquals(InnermostScrollViewer(e.OriginalSource as DependencyObject), sv))
            return;

        var target = FirstScrollableInDirection(sv, e.Delta);
        if (target is null) return;   // nichts kann scrollen → Ereignis weiterreichen

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
    /// Walks outwards until it finds a scroll area with room left in that
    /// direction. So a short inner box hands the movement over to the page
    /// instead of swallowing it.
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
    /// Visual parent for visuals, logical parent otherwise — der OriginalSource
    /// eines Rad-Ereignisses kann ein Textelement ohne visuellen Eltern sein.
    /// </summary>
    private static DependencyObject? ParentOf(DependencyObject element)
        => element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);
}
