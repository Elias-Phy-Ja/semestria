using System.Windows;
using System.Windows.Threading;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.UI.Controls;

namespace SchulnetzSync.UI;

/// <summary>
/// The guided tours: every page that implements <see cref="ITourPage"/> gets its tour on
/// the first visit (once per profile), and «Anleitung» in the sidebar replays it.
/// </summary>
public partial class MainWindow
{
    private TourOverlay? _tour;

    /// <summary>True once the loading view is gone; tours never run behind it.</summary>
    private bool _startupDone;

    /// <summary>The main window, for pages that want to show a tour or a hint.</summary>
    public static MainWindow? Current => System.Windows.Application.Current?.MainWindow as MainWindow;

    public TourOverlay Tour => _tour ??= new TourOverlay(TourLayer);

    /// <summary>Set by the help page: the next page shown starts its tour, seen or not.</summary>
    private bool _tourRequested;

    /// <summary>Hooks every newly shown page, so its tour can start once it has loaded.</summary>
    private void HookTours()
        => ContentFrame.Navigated += (_, e) =>
        {
            if (_tour?.IsActive == true) _tour.Close();
            if (e.Content is not (FrameworkElement element and ITourPage page))
            {
                _tourRequested = false;
                return;
            }
            element.Loaded += (_, _) =>
            {
                if (!_tourRequested) { MaybeStartTour(page); return; }
                _tourRequested = false;
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => StartTour(page));
            };
        };

    /// <summary>Opens a page and runs its tour, e.g. from the help page.</summary>
    /// <param name="tag">The page's navigation tag: "Dashboard", "Events", "Tasks" …</param>
    public void ShowTourOf(string tag)
    {
        _tourRequested = true;
        NavigateTo(tag);
    }

    /// <summary>Starts the page's tour unless it was seen before or the page is not ready.</summary>
    public void MaybeStartTour(ITourPage page)
    {
        if (!_startupDone || Tour.IsActive || !page.TourReady || HasSeen(page.TourKey)) return;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            // The page may have been left in the meantime
            if (ContentFrame.Content == page && !Tour.IsActive) StartTour(page);
        });
    }

    /// <summary>Runs the tour now, seen or not; ending it in any way counts as seen.</summary>
    public void StartTour(ITourPage page)
    {
        if (!page.TourReady) return;
        Tour.Start(page.TourSteps(), () => MarkSeen(page.TourKey));
    }

    private static bool HasSeen(string key)
    {
        var config = AppState.Config;
        return config.SeenTours.Contains(key) || (key == "grades" && config.GradesTutorialSeen);
    }

    private static void MarkSeen(string key)
    {
        var config = AppState.Config;
        if (!config.SeenTours.Add(key)) return;
        ConfigManager.Save(config);
    }
}
